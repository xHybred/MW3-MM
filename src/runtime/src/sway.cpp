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
    // FUN_14002D900 works out the weapon sway for one client and leaves the
    // result in the state block passed in rcx. Writing the weapon definition's
    // sway fields is not enough on its own: the engine only ever looks at the
    // definition of the weapon it resolved first, so a weapon picked up later
    // keeps its stock sway. Scaling the produced state covers every weapon.
    constexpr size_t kSwayUpdateOffset = 0x2D900;
    constexpr unsigned char kPrologue[] = {0x48, 0x8B, 0xC4, 0x41, 0x56, 0x48, 0x81, 0xEC, 0xB0, 0x00, 0x00, 0x00};

    // Smoothed sway offsets inside the state block: two horizontal, two
    // vertical. +0x6C..+0x74 holds the view angles the sway is measured from
    // and is left alone.
    constexpr size_t kSwayFields[] = {0x54, 0x58, 0x64, 0x68};

    // The engine only passes a state pointer, but the compiler is free to
    // clobber the other argument registers unless they are named here.
    using SwayUpdateFn = void (*)(void*, void*, void*, void*);

    SwayUpdateFn g_original = nullptr;
    std::atomic<bool> g_enabled{true};
    std::atomic<float> g_strength{1.0f};
    std::atomic<bool> g_report{false};

    uintptr_t ImageBase()
    {
        return reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    }

    void __fastcall HookedSwayUpdate(void* state, void* a2, void* a3, void* a4)
    {
        g_original(state, a2, a3, a4);

        const float strength = g_enabled.load(std::memory_order_relaxed)
                                   ? g_strength.load(std::memory_order_relaxed)
                                   : 1.0f;

        if (!state || strength >= 0.999f)
        {
            g_report.store(false, std::memory_order_relaxed);
            return;
        }

        auto* base = static_cast<unsigned char*>(state);
        for (size_t offset : kSwayFields)
            *reinterpret_cast<float*>(base + offset) *= strength;

        if (g_report.exchange(false, std::memory_order_relaxed))
        {
            LogF(L"sway: state %p scaled by %.2f (now %.4f %.4f %.4f %.4f)", state, strength,
                 *reinterpret_cast<float*>(base + 0x54), *reinterpret_cast<float*>(base + 0x58),
                 *reinterpret_cast<float*>(base + 0x64), *reinterpret_cast<float*>(base + 0x68));
        }
    }
}

bool sway::Install()
{
    auto* target = reinterpret_cast<void*>(ImageBase() + kSwayUpdateOffset);

    if (memcmp(target, kPrologue, sizeof(kPrologue)) != 0)
    {
        LogF(L"sway: unexpected code at module+0x%zx, hook skipped", kSwayUpdateOffset);
        return false;
    }

    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED)
    {
        LogF(L"sway: MH_Initialize failed (%d)", static_cast<int>(init));
        return false;
    }

    if (MH_CreateHook(target, &HookedSwayUpdate, reinterpret_cast<void**>(&g_original)) != MH_OK ||
        MH_EnableHook(target) != MH_OK)
    {
        LogF(L"sway: hook at module+0x%zx failed", kSwayUpdateOffset);
        return false;
    }

    LogF(L"sway: hook at module+0x%zx", kSwayUpdateOffset);
    return true;
}

void sway::Update(const ModSettings& settings)
{
    g_enabled.store(settings.swayEnabled, std::memory_order_relaxed);

    const float previous = g_strength.exchange(settings.swayStrength, std::memory_order_relaxed);
    if (std::fabs(previous - settings.swayStrength) > 0.0005f)
        g_report.store(true, std::memory_order_relaxed);
}
