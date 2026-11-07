#include "sway.h"

#include "log.h"
#include "settings.h"

#include <windows.h>

#include <MinHook.h>

#include <atomic>
#include <cmath>
#include <cstdint>

namespace
{
    // FUN_14002DD50 is the idle sway. It builds three sine terms from a
    // per-player counter (player+0x494) and adds them straight onto the
    // first-person view angles passed in rdx. Freezing it stops the weapon
    // drifting while the camera and the world stay still, which is exactly the
    // motion the slider is meant to control.
    constexpr size_t kSwayOffset = 0x2DD50;
    constexpr unsigned char kPrologue[] = {0x48, 0x8B, 0xC4, 0x48, 0x89, 0x58, 0x08};

    // The engine only passes two meaningful arguments, but the compiler is free
    // to clobber the other registers unless they are named here.
    using SwayFn = void (*)(void*, void*, void*, void*);

    SwayFn g_original = nullptr;
    std::atomic<bool> g_enabled{true};
    std::atomic<float> g_strength{1.0f};
    std::atomic<bool> g_report{false};
    std::atomic<bool> g_debug{false};

    uintptr_t ImageBase()
    {
        return reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    }

    void __fastcall HookedSway(void* state, void* angles, void* a3, void* a4)
    {
        float before[3] = {};
        const bool tracked = angles != nullptr;
        if (tracked)
        {
            for (int i = 0; i < 3; ++i)
                before[i] = static_cast<float*>(angles)[i];
        }

        g_original(state, angles, a3, a4);

        if (!tracked)
            return;

        const float strength = g_enabled.load(std::memory_order_relaxed)
                                   ? g_strength.load(std::memory_order_relaxed)
                                   : 1.0f;

        // Scaling the difference keeps everything the caller had already put
        // into these angles and only shrinks what this function added, so the
        // aiming pose and the weapon position are untouched.
        auto* out = static_cast<float*>(angles);
        for (int i = 0; i < 3; ++i)
            out[i] = before[i] + (out[i] - before[i]) * strength;

        if (g_report.exchange(false, std::memory_order_relaxed))
        {
            LogF(L"sway: %p scaled by %.2f (delta %.4f %.4f %.4f)", state, strength,
                 out[0] - before[0], out[1] - before[1], out[2] - before[2]);
        }
        else if (g_debug.load(std::memory_order_relaxed))
        {
            static ULONGLONG nextDump = 0;
            const ULONGLONG now = GetTickCount64();
            if (now >= nextDump)
            {
                nextDump = now + 200;
                LogF(L"sway: delta %.4f %.4f %.4f (strength %.2f)",
                     out[0] - before[0], out[1] - before[1], out[2] - before[2], strength);
            }
        }
    }
}

bool sway::Install()
{
    auto* target = reinterpret_cast<void*>(ImageBase() + kSwayOffset);

    if (memcmp(target, kPrologue, sizeof(kPrologue)) != 0)
    {
        LogF(L"sway: unexpected code at module+0x%zx, hook skipped", kSwayOffset);
        return false;
    }

    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED)
    {
        LogF(L"sway: MH_Initialize failed (%d)", static_cast<int>(init));
        return false;
    }

    if (MH_CreateHook(target, &HookedSway, reinterpret_cast<void**>(&g_original)) != MH_OK ||
        MH_EnableHook(target) != MH_OK)
    {
        LogF(L"sway: hook at module+0x%zx failed", kSwayOffset);
        return false;
    }

    LogF(L"sway: hook at module+0x%zx", kSwayOffset);
    return true;
}

void sway::Update(const ModSettings& settings)
{
    g_enabled.store(settings.swayEnabled, std::memory_order_relaxed);
    g_debug.store(settings.swayDebug, std::memory_order_relaxed);

    const float previous = g_strength.exchange(settings.swayStrength, std::memory_order_relaxed);
    if (std::fabs(previous - settings.swayStrength) > 0.0005f)
        g_report.store(true, std::memory_order_relaxed);
}
