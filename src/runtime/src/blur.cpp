#include "blur.h"

#include <windows.h>

#include <atomic>
#include <cstring>
#include <intrin.h>

#include "MinHook.h"
#include "log.h"

namespace blur {
namespace {

// FUN_1400327A0, reached from the script builtin dispatcher: client index in
// ECX, duration in EDX, blur radius in XMM2, a flag in R9D and one stack
// argument. It stamps a per-client timer plus the radius that the renderer
// reads when it draws the full-screen blur.
constexpr uintptr_t kBlurSetOffset = 0x327A0;

// First bytes of FUN_1400327A0 in the 2026-09-03 x64 build.
constexpr unsigned char kPrologue[] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C};

using BlurSetFn = void(__fastcall*)(int, int, float, int, int);
BlurSetFn g_original = nullptr;

std::atomic<bool> g_enabled{true};
std::atomic<float> g_strength{1.0f};
std::atomic<bool> g_debug{false};
std::atomic<bool> g_loggedOnce{false};

void __fastcall HookedBlurSet(int clientNum, int duration, float radius, int flag, int pad) {
    const uintptr_t caller = reinterpret_cast<uintptr_t>(_ReturnAddress());
    const bool enabled = g_enabled.load(std::memory_order_relaxed);
    const float out = enabled ? radius * g_strength.load(std::memory_order_relaxed) : 0.0f;

    if (g_debug.load(std::memory_order_relaxed)) {
        const HMODULE module = GetModuleHandleW(nullptr);
        LogF(L"blur: client %d %.2f -> %.2f, %d ms, caller +0x%zx", clientNum, radius, out,
             duration, caller - reinterpret_cast<uintptr_t>(module));
    }

    g_original(clientNum, duration, out, flag, pad);
}

}  // namespace

bool Install() {
    const HMODULE module = GetModuleHandleW(nullptr);
    if (!module) return false;
    void* target = reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(module) + kBlurSetOffset);
    if (memcmp(target, kPrologue, sizeof(kPrologue)) != 0) {
        LogF(L"blur: unexpected code at module+0x%zx, hook skipped", kBlurSetOffset);
        return false;
    }

    // adszoom installs first and has already initialised MinHook.
    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED) {
        LogF(L"blur: MH_Initialize failed (%d)", init);
        return false;
    }
    if (MH_CreateHook(target, reinterpret_cast<void*>(&HookedBlurSet),
                      reinterpret_cast<void**>(&g_original)) != MH_OK) {
        LogF(L"blur: MH_CreateHook failed at module+0x%zx", kBlurSetOffset);
        return false;
    }
    if (MH_EnableHook(target) != MH_OK) {
        LogF(L"blur: MH_EnableHook failed at module+0x%zx", kBlurSetOffset);
        return false;
    }
    LogF(L"blur: hook at module+0x%zx", kBlurSetOffset);
    return true;
}

void Update(const ModSettings& settings) {
    g_enabled.store(settings.blurEnabled, std::memory_order_relaxed);
    g_strength.store(settings.blurStrength, std::memory_order_relaxed);
    g_debug.store(settings.blurDebug, std::memory_order_relaxed);
}

}  // namespace blur
