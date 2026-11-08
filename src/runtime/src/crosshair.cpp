#include "crosshair.h"

#include <windows.h>

#include <atomic>
#include <cmath>
#include <cstring>

#include "MinHook.h"
#include "dvar.h"
#include "log.h"
#include "overlay.h"

namespace crosshair {
namespace {

// Two getters hand the crosshair drawing code its material and a size in
// pixels, both with the same shape: (client in ECX, flag in DL, Material** in
// R8, int* size in R9). Every caller of either lives in the crosshair path, so
// scaling the size they hand back resizes the crosshair and nothing else.
//
// FUN_140024B60 serves the dot and outline styles, taking the size from the
// weapon asset. FUN_140026790 serves the four-bar style the game shows in
// single player, taking it from the per-weapon state object instead; hooking
// only the first one changed nothing on screen.
constexpr uintptr_t kCrosshairMaterialOffset = 0x24B60;
constexpr uintptr_t kCrosshairBarMaterialOffset = 0x26790;

// FUN_14003E760 is the drawer of that four-bar style. When a custom style is
// selected the engine's draw is skipped and the overlay draws instead, so the
// hook still runs first and marks the frame.
constexpr uintptr_t kCrosshairBarDrawOffset = 0x3E760;
constexpr unsigned char kBarDrawPrologue[] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48,
                                              0x89, 0x6C, 0x24, 0x10, 0x48, 0x89,
                                              0x74, 0x24, 0x18, 0x57};

// First bytes of both functions in the 2026-09-03 x64 build.
constexpr unsigned char kPrologue[] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x57,
                                       0x48, 0x83, 0xEC, 0x40};

using CrosshairMaterialFn = void(__fastcall*)(int, bool, void**, int*);
CrosshairMaterialFn g_original = nullptr;
CrosshairMaterialFn g_barOriginal = nullptr;

using CrosshairBarDrawFn = void(__fastcall*)(int, int, bool, const float*, void*, void*, void*,
                                            void*);
CrosshairBarDrawFn g_barDrawOriginal = nullptr;

std::atomic<int> g_style{0};

std::atomic<bool> g_hookEnabled{true};
std::atomic<float> g_size{1.0f};
std::atomic<bool> g_debug{false};
std::atomic<int> g_lastBase{0};
std::atomic<int> g_lastScaled{0};
void* g_dumpedMaterial = nullptr;
int g_dumpCount = 0;

void ScaleSize(int* size) {
    if (!g_hookEnabled.load(std::memory_order_relaxed) || size == nullptr) return;

    // The second getter in the same draw reads the size we already scaled, so
    // scaling it again compounded a 200% setting into 400%. Undo our own value
    // before re-applying it.
    struct Pair {
        int vanilla = 0;
        int written = 0;
    };
    static Pair recent[6];

    int base = *size;
    for (const Pair& p : recent) {
        if (p.written != p.vanilla && p.written == base) {
            base = p.vanilla;
            break;
        }
    }
    if (base <= 0) return;

    const float factor = g_size.load(std::memory_order_relaxed);
    int scaled = static_cast<int>(base * factor + 0.5f);
    if (scaled < 1) scaled = 1;
    if (scaled == *size) return;

    *size = scaled;
    for (int i = 5; i > 0; --i) recent[i] = recent[i - 1];
    recent[0] = Pair{base, scaled};

    static ULONGLONG nextLog = 0;
    const ULONGLONG now = GetTickCount64();
    if (g_debug.load(std::memory_order_relaxed) && now >= nextLog &&
        (base != g_lastBase.load(std::memory_order_relaxed) ||
         scaled != g_lastScaled.load(std::memory_order_relaxed))) {
        g_lastBase.store(base, std::memory_order_relaxed);
        g_lastScaled.store(scaled, std::memory_order_relaxed);
        nextLog = now + 2000;
        LogF(L"crosshair: size %d -> %d (%.2fx)", base, scaled, factor);
    }
}

void LogHex(const wchar_t* label, const unsigned char* bytes, size_t count) {
    wchar_t line[256];
    for (size_t i = 0; i < count; i += 16) {
        int used = swprintf_s(line, L"%s+%02zx:", label, i);
        if (used < 0) return;
        for (size_t j = 0; j < 16 && i + j < count; ++j) {
            wchar_t chunk[8];
            swprintf_s(chunk, L" %02x", bytes[i + j]);
            wcscat_s(line, chunk);
        }
        LogF(L"%s", line);
    }
}

// Temporary: the crosshair is drawn as small textured quads, which is why it
// looks soft at high resolution. This records what the material actually is so
// the fix can target the real cause instead of a guess.
void DumpMaterial(void* material, int size) {
    if (!material) {
        LogF(L"crosshair: material none, size %d", size);
        return;
    }

    const auto* bytes = static_cast<const unsigned char*>(material);
    const char* name = *reinterpret_cast<const char* const*>(bytes);
    const unsigned char textureCount = bytes[0x56];
    void* textureTable = *reinterpret_cast<void* const*>(bytes + 0x68);
    void* techniqueSet = *reinterpret_cast<void* const*>(bytes + 0x60);

    LogF(L"crosshair: material %p name %S textureCount %u table %p technique %p size %d", material,
         name ? name : "(null)", textureCount, textureTable, techniqueSet, size);
    LogHex(L"crosshair: mat", bytes, 0x80);

    const auto* textures = static_cast<const unsigned char*>(textureTable);
    for (unsigned i = 0; i < textureCount && i < 3 && textures; ++i) {
        const unsigned char* def = textures + i * 0x10;
        const unsigned int hash = *reinterpret_cast<const unsigned int*>(def);
        const unsigned char sampler = def[0x06];
        const unsigned char semantic = def[0x07];
        void* image = *reinterpret_cast<void* const*>(def + 0x08);
        LogF(L"crosshair:   tex %u hash %08x chars %c..%c sampler %02x filter %u mip %u semantic %u image %p",
             i, hash, def[0x04], def[0x05], sampler, sampler & 7u, (sampler >> 3) & 3u, semantic, image);
        if (!image) continue;

        const auto* img = static_cast<const unsigned char*>(image);
        const unsigned short width = *reinterpret_cast<const unsigned short*>(img + 0x18);
        const unsigned short height = *reinterpret_cast<const unsigned short*>(img + 0x1A);
        const unsigned char levels = img[0x1E];
        const char* imageName = *reinterpret_cast<const char* const*>(img + 0x20);
        LogF(L"crosshair:   image %p name %S %ux%u levels %u", image,
             imageName ? imageName : "(null)", width, height, levels);
        LogHex(L"crosshair: img", img, 0x40);
    }
}

void DumpOnce(void* material, int size) {
    if (!g_debug.load(std::memory_order_relaxed)) return;
    if (!material || material == g_dumpedMaterial || g_dumpCount >= 3) return;

    g_dumpedMaterial = material;
    ++g_dumpCount;
    __try {
        DumpMaterial(material, size);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        LogF(L"crosshair: material dump faulted at %p", material);
    }
}

void __fastcall HookedCrosshairMaterial(int client, bool flag, void** material, int* size) {
    g_original(client, flag, material, size);
    ScaleSize(size);
    DumpOnce(material ? *material : nullptr, size ? *size : 0);
}

void __fastcall HookedCrosshairBarMaterial(int client, bool flag, void** material, int* size) {
    g_barOriginal(client, flag, material, size);
    ScaleSize(size);
    DumpOnce(material ? *material : nullptr, size ? *size : 0);
}

void __fastcall HookedCrosshairBarDraw(int client, int style, bool flag, const float* colour,
                                       void* d, void* e, void* f, void* g) {
    overlay::MarkEngineCrosshair();
    if (colour != nullptr) overlay::SetColor(colour[0], colour[1], colour[2], colour[3]);
    if (g_style.load(std::memory_order_relaxed) != 0) return;
    g_barDrawOriginal(client, style, flag, colour, d, e, f, g);
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
DvarRef g_vertical;
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
    const uintptr_t base = reinterpret_cast<uintptr_t>(module);
    void* target = reinterpret_cast<void*>(base + kCrosshairMaterialOffset);
    void* barTarget = reinterpret_cast<void*>(base + kCrosshairBarMaterialOffset);
    if (memcmp(target, kPrologue, sizeof(kPrologue)) != 0 ||
        memcmp(barTarget, kPrologue, sizeof(kPrologue)) != 0) {
        LogF(L"crosshair: unexpected code at module+0x%zx, size scaling skipped",
             kCrosshairMaterialOffset);
        return false;
    }

    // adszoom installs first and has already initialised MinHook.
    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED) {
        LogF(L"crosshair: MH_Initialize failed (%d)", init);
        return false;
    }
    if (MH_CreateHook(target, reinterpret_cast<void*>(&HookedCrosshairMaterial),
                      reinterpret_cast<void**>(&g_original)) != MH_OK ||
        MH_CreateHook(barTarget, reinterpret_cast<void*>(&HookedCrosshairBarMaterial),
                      reinterpret_cast<void**>(&g_barOriginal)) != MH_OK) {
        LogF(L"crosshair: MH_CreateHook failed at module+0x%zx", kCrosshairMaterialOffset);
        return false;
    }
    if (MH_EnableHook(target) != MH_OK || MH_EnableHook(barTarget) != MH_OK) {
        LogF(L"crosshair: MH_EnableHook failed at module+0x%zx", kCrosshairMaterialOffset);
        return false;
    }
    LogF(L"crosshair: size hooks at module+0x%zx and module+0x%zx", kCrosshairMaterialOffset,
         kCrosshairBarMaterialOffset);

    void* barDraw = reinterpret_cast<void*>(base + kCrosshairBarDrawOffset);
    if (memcmp(barDraw, kBarDrawPrologue, sizeof(kBarDrawPrologue)) == 0 &&
        MH_CreateHook(barDraw, reinterpret_cast<void*>(&HookedCrosshairBarDraw),
                      reinterpret_cast<void**>(&g_barDrawOriginal)) == MH_OK &&
        MH_EnableHook(barDraw) == MH_OK) {
        LogF(L"crosshair: draw hook at module+0x%zx", kCrosshairBarDrawOffset);
    } else {
        g_barDrawOriginal = nullptr;
        LogF(L"crosshair: unexpected code at module+0x%zx, draw hook skipped",
             kCrosshairBarDrawOffset);
    }
    return true;
}

bool Resolve() {
    if (!FindDvarTyped("cg_drawCrosshair", 0, g_draw)) return false;
    if (!FindDvar("cg_crosshairAlpha", g_alpha)) return false;
    if (!FindDvar("cg_crosshairAlphaMin", g_alphaMin)) return false;
    if (!FindDvarTyped("cg_crosshairDynamic", 0, g_dynamic)) return false;
    // Not fatal: only the custom styles use it.
    g_vertical = DvarRef{};
    FindDvar("cg_crosshairVerticalOffset", g_vertical);

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
    if (g_vertical.valid()) overlay::SetVerticalOffset(ReadFloat(g_vertical));
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
    g_style.store(settings.crosshairStyle, std::memory_order_relaxed);
}

}  // namespace crosshair
