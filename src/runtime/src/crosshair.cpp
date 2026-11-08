#include "crosshair.h"

#include <windows.h>

#include <atomic>
#include <cmath>
#include <cstring>

#include "MinHook.h"
#include "dvar.h"
#include "log.h"

namespace crosshair {
namespace {

// FUN_140024B60 fills in the crosshair material and its size in pixels:
// (client in ECX, flag in DL, Material** in R8, int* size in R9). It is called
// only from the crosshair draw path, so scaling the size it hands back resizes
// the crosshair and nothing else.
constexpr uintptr_t kCrosshairMaterialOffset = 0x24B60;

// First bytes of FUN_140024B60 in the 2026-09-03 x64 build.
constexpr unsigned char kPrologue[] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57,
                                       0x48, 0x83, 0xEC, 0x40};

using CrosshairMaterialFn = void(__fastcall*)(int, bool, void**, int*);
CrosshairMaterialFn g_original = nullptr;

std::atomic<bool> g_hookEnabled{true};
std::atomic<float> g_size{1.0f};
std::atomic<bool> g_debug{false};
std::atomic<int> g_lastBase{0};
std::atomic<int> g_lastScaled{0};

void __fastcall HookedCrosshairMaterial(int client, bool flag, void** material, int* size) {
    g_original(client, flag, material, size);

    if (!g_hookEnabled.load(std::memory_order_relaxed) || size == nullptr) return;

    const int base = *size;
    if (base <= 0) return;

    const float factor = g_size.load(std::memory_order_relaxed);
    int scaled = static_cast<int>(base * factor + 0.5f);
    if (scaled < 1) scaled = 1;
    if (scaled == base) return;

    *size = scaled;

    if (g_debug.load(std::memory_order_relaxed) &&
        (base != g_lastBase.load(std::memory_order_relaxed) ||
         scaled != g_lastScaled.load(std::memory_order_relaxed))) {
        g_lastBase.store(base, std::memory_order_relaxed);
        g_lastScaled.store(scaled, std::memory_order_relaxed);
        LogF(L"crosshair: size %d -> %d (%.2fx)", base, scaled, factor);
    }
}

struct Vanilla {
    float alpha = 1.0f;
    float alphaMin = 0.5f;
    int draw = 1;
    int dynamic = 0;
};

DvarRef g_alpha;
DvarRef g_alphaMin;
DvarRef g_draw;
DvarRef g_dynamic;
Vanilla g_vanilla{};
bool g_resolved = false;

void PushFloat(const DvarRef& dvar, float vanilla, float factor, const wchar_t* label) {
    const float want = vanilla * factor;
    const float have = ReadFloat(dvar);
    if (fabsf(have - want) <= 0.001f) return;
    WriteFloat(dvar, want);
    LogF(L"%s %.3f -> %.3f", label, have, want);
}

void PushInt(const DvarRef& dvar, int want, const wchar_t* label) {
    const int have = ReadInt(dvar);
    if (have == want) return;
    WriteInt(dvar, want);
    LogF(L"%s %d -> %d", label, have, want);
}

}  // namespace

bool Install() {
    const HMODULE module = GetModuleHandleW(nullptr);
    if (!module) return false;
    void* target = reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(module) + kCrosshairMaterialOffset);
    if (memcmp(target, kPrologue, sizeof(kPrologue)) != 0) {
        LogF(L"crosshair: unexpected code at module+0x%zx, size scaling skipped", kCrosshairMaterialOffset);
        return false;
    }

    // adszoom installs first and has already initialised MinHook.
    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED) {
        LogF(L"crosshair: MH_Initialize failed (%d)", init);
        return false;
    }
    if (MH_CreateHook(target, reinterpret_cast<void*>(&HookedCrosshairMaterial),
                      reinterpret_cast<void**>(&g_original)) != MH_OK) {
        LogF(L"crosshair: MH_CreateHook failed at module+0x%zx", kCrosshairMaterialOffset);
        return false;
    }
    if (MH_EnableHook(target) != MH_OK) {
        LogF(L"crosshair: MH_EnableHook failed at module+0x%zx", kCrosshairMaterialOffset);
        return false;
    }
    LogF(L"crosshair: size hook at module+0x%zx", kCrosshairMaterialOffset);
    return true;
}

bool Resolve() {
    if (!FindDvarTyped("cg_drawCrosshair", 0, g_draw)) return false;
    if (!FindDvar("cg_crosshairAlpha", g_alpha)) return false;
    if (!FindDvar("cg_crosshairAlphaMin", g_alphaMin)) return false;
    if (!FindDvarTyped("cg_crosshairDynamic", 0, g_dynamic)) return false;

    g_vanilla.alpha = ReadFloat(g_alpha);
    g_vanilla.alphaMin = ReadFloat(g_alphaMin);
    g_vanilla.draw = ReadInt(g_draw);
    g_vanilla.dynamic = ReadInt(g_dynamic);
    g_resolved = true;

    LogF(L"crosshair: alpha %.2f/%.2f, draw %d, dynamic %d", g_vanilla.alpha, g_vanilla.alphaMin,
         g_vanilla.draw, g_vanilla.dynamic);
    return true;
}

bool Valid() {
    if (!g_resolved) return false;

    if (!StillValid(g_draw, "cg_drawCrosshair") || !StillValid(g_alpha, "cg_crosshairAlpha") ||
        !StillValid(g_alphaMin, "cg_crosshairAlphaMin") || !StillValid(g_dynamic, "cg_crosshairDynamic")) {
        g_resolved = false;
        g_alpha = DvarRef{};
        g_alphaMin = DvarRef{};
        g_draw = DvarRef{};
        g_dynamic = DvarRef{};
        return false;
    }
    return true;
}

void Apply(const ModSettings& settings) {
    if (!Valid()) return;
    PushInt(g_draw, settings.crosshairShow ? 1 : 0, L"cg_drawCrosshair");
    PushFloat(g_alpha, g_vanilla.alpha, settings.crosshairOpacity, L"cg_crosshairAlpha");
    PushFloat(g_alphaMin, g_vanilla.alphaMin, settings.crosshairOpacity, L"cg_crosshairAlphaMin");
    PushInt(g_dynamic, settings.crosshairDynamic ? 1 : 0, L"cg_crosshairDynamic");
}

void Restore() {
    if (!Valid()) return;
    PushInt(g_draw, g_vanilla.draw, L"cg_drawCrosshair");
    PushFloat(g_alpha, g_vanilla.alpha, 1.0f, L"cg_crosshairAlpha");
    PushFloat(g_alphaMin, g_vanilla.alphaMin, 1.0f, L"cg_crosshairAlphaMin");
    PushInt(g_dynamic, g_vanilla.dynamic, L"cg_crosshairDynamic");
}

void Update(const ModSettings& settings) {
    g_hookEnabled.store(settings.crosshairEnabled, std::memory_order_relaxed);
    g_size.store(settings.crosshairSize, std::memory_order_relaxed);
    g_debug.store(settings.crosshairDebug, std::memory_order_relaxed);
}

}  // namespace crosshair
