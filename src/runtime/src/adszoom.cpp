#include "adszoom.h"

#include <windows.h>

#include <atomic>
#include <cmath>
#include <cstring>

#include "MinHook.h"
#include "log.h"
#include "settings.h"
#include "weapon.h"

namespace adszoom {
namespace {

// FUN_140022630 returns the equipped weapon's ADS FOV in degrees, already
// multiplied by its attachment modifiers. GetEffectiveFov lerps from cg_fov
// toward min(cg_fov, this value) while the player aims, then multiplies by
// cg_fovScale, so this one function decides how much aiming zooms.
constexpr uintptr_t kWeaponFovOffset = 0x22630;

// First bytes of FUN_140022630 in the 2026-09-03 x64 build. If they are gone
// the game was updated and every offset in this file is a guess, so refuse to
// hook instead of crashing mid-frame.
constexpr unsigned char kPrologue[] = {0x40, 0x53, 0x48, 0x83, 0xEC, 0x50, 0x0F, 0x29};

using WeaponFovFn = float (*)(int, int);
WeaponFovFn g_original = nullptr;

std::atomic<bool> g_enabled{false};
std::atomic<int> g_mode{0};  // 0 scales the excess, 1 flattens every weapon
std::atomic<float> g_zoom{1.1f};
std::atomic<float> g_pivot{65.0f};

// Debug readout. This runs inside the game's own FOV path, which is the only
// place the effective value can be read safely: GetEffectiveFov walks live
// client entities and faults if called from another thread during a level
// teardown.
std::atomic<bool> g_log{false};
std::atomic<ULONGLONG> g_nextLog{0};
std::atomic<float> g_lastLogged{-1.0f};

void LogIfChanged(float vanilla, float patched) {
    if (!g_log.load(std::memory_order_relaxed)) return;
    const ULONGLONG now = GetTickCount64();
    if (now < g_nextLog.load(std::memory_order_relaxed)) return;
    if (fabsf(g_lastLogged.load(std::memory_order_relaxed) - patched) < 0.01f) return;
    g_lastLogged.store(patched, std::memory_order_relaxed);
    g_nextLog.store(now + 200, std::memory_order_relaxed);
    const float pivot = g_pivot.load(std::memory_order_relaxed);
    LogF(L"ADS zoom: weapon %.2f -> %.2f (%.3fx)", vanilla, patched,
         patched > 0.0f ? pivot / patched : 0.0f);
}

float HookedWeaponFov(int clientNum, int flag) {
    const float vanilla = g_original(clientNum, flag);

    weapon::ApplyFromClient(clientNum);

    if (!g_enabled.load(std::memory_order_relaxed)) return vanilla;
    if (!(vanilla > 0.5f) || vanilla > 179.0f) return vanilla;

    const float pivot = g_pivot.load(std::memory_order_relaxed);
    const float vanillaMag = pivot / vanilla;
    const float mag = MappedMagnification(vanillaMag, g_zoom.load(std::memory_order_relaxed),
                                          g_mode.load(std::memory_order_relaxed) == 1);

    const float patched = pivot / mag;
    LogIfChanged(vanilla, patched);
    return patched;
}

}  // namespace

bool Install() {
    HMODULE module = GetModuleHandleW(nullptr);
    if (!module) return false;

    void* target = reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(module) + kWeaponFovOffset);
    if (std::memcmp(target, kPrologue, sizeof(kPrologue)) != 0) {
        LogF(L"ADS zoom: unexpected code at module+0x%zx, hook skipped", kWeaponFovOffset);
        return false;
    }

    if (MH_Initialize() != MH_OK) {
        LogF(L"ADS zoom: MinHook init failed");
        return false;
    }

    if (MH_CreateHook(target, reinterpret_cast<LPVOID>(&HookedWeaponFov),
                      reinterpret_cast<LPVOID*>(&g_original)) != MH_OK) {
        LogF(L"ADS zoom: MinHook could not hook module+0x%zx", kWeaponFovOffset);
        return false;
    }

    if (MH_EnableHook(target) != MH_OK) {
        LogF(L"ADS zoom: MinHook could not enable the hook");
        return false;
    }

    LogF(L"ADS zoom: hook at module+0x%zx", kWeaponFovOffset);
    return true;
}

void Update(const ModSettings& settings, float pivotFov) {
    g_enabled.store(settings.enabled && settings.aimEnabled, std::memory_order_relaxed);
    g_mode.store(settings.aimFlat ? 1 : 0, std::memory_order_relaxed);
    g_zoom.store(settings.aimZoom, std::memory_order_relaxed);
    g_pivot.store(pivotFov > 1.0f ? pivotFov : 65.0f, std::memory_order_relaxed);
    g_log.store(settings.logEffectiveFov, std::memory_order_relaxed);
}

}  // namespace adszoom
