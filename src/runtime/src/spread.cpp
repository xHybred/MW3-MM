#include "spread.h"

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <intrin.h>

#include "log.h"
#include "overlay.h"
#include "settings.h"

#include <MinHook.h>

// The bullet and RNG detours only exist to explain where a shot's cone comes
// from. They sit directly on the shot path, so release builds leave them out.
// Define MW3MOD_DIAGNOSTIC_HOOKS=1 in a local build when that investigation is
// needed again.
#ifndef MW3MOD_DIAGNOSTIC_HOOKS
#define MW3MOD_DIAGNOSTIC_HOOKS 0
#endif

namespace
{
    // The engine asks this one function for the cone a bullet is randomised
    // inside while aiming, and every aimed reader goes through it: the spread
    // blend the fire path consumes, the gameplay code that turns the cone into a
    // random direction, and the crosshair. Writing the stored float at +0x764 did
    // not change where the bullets went, so the value is scaled on its way out.
    constexpr size_t kAdsSpreadOffset = 0x221F0;
    constexpr unsigned char kPrologue[] = {
        0x40, 0x53, 0x48, 0x83, 0xEC, 0x50, 0x0F, 0x29, 0x74, 0x24, 0x40, 0x0F, 0xB6, 0xDA,
    };

    // float __fastcall AdsSpread(int clientNum, bool viaSlot)
    using AdsSpreadFn = float (*)(int clientNum, bool viaSlot);

    AdsSpreadFn g_original = nullptr;

    std::atomic<bool> g_enabled{true};
    std::atomic<float> g_aim{0.0f};
    std::atomic<bool> g_log{false};

    // Who asks for the cone, and what they get. A single line a second answers
    // "does the shot path really use this number" without another rebuild.
    struct Caller
    {
        size_t rva = 0;
        unsigned calls = 0;
        float sum = 0.0f;
        float max = 0.0f;
    };

    constexpr size_t kMaxCallers = 8;
    Caller g_callers[kMaxCallers];
    size_t g_callerCount = 0;
    ULONGLONG g_windowStart = 0;

    size_t ModuleBase()
    {
        return reinterpret_cast<size_t>(GetModuleHandleW(nullptr));
    }

    void Record(size_t rva, float value)
    {
        Caller* entry = nullptr;
        for (size_t i = 0; i < g_callerCount; ++i)
        {
            if (g_callers[i].rva == rva)
            {
                entry = &g_callers[i];
                break;
            }
        }

        if (!entry && g_callerCount < kMaxCallers)
        {
            entry = &g_callers[g_callerCount++];
            entry->rva = rva;
        }

        if (!entry)
            return;

        entry->calls += 1;
        entry->sum += value;
        entry->max = std::max(entry->max, value);

        const ULONGLONG now = GetTickCount64();
        if (g_windowStart == 0)
            g_windowStart = now;
        if (now - g_windowStart < 1000)
            return;

        g_windowStart = now;
        for (size_t i = 0; i < g_callerCount; ++i)
        {
            Caller& c = g_callers[i];
            LogF(L"spread: caller +0x%zx asked %u time(s), avg %.4f, max %.4f", c.rva, c.calls,
                 c.calls ? c.sum / static_cast<float>(c.calls) : 0.0f, c.max);
            c.calls = 0;
            c.sum = 0.0f;
            c.max = 0.0f;
        }
    }

    float HookedAdsSpread(int clientNum, bool viaSlot)
    {
        overlay::MarkAiming();
        const float vanilla = g_original(clientNum, viaSlot);

        float result = vanilla;
        bool scaled = false;
        if (g_enabled.load(std::memory_order_relaxed))
        {
            const float strength = std::clamp(1.0f - g_aim.load(std::memory_order_relaxed), 0.0f, 1.0f);
            if (strength < 0.999f)
            {
                result = vanilla * strength;
                scaled = true;
            }
        }

        // Logged after scaling, so the numbers below are what the engine was
        // handed rather than what it stored. Stock settings never open the log.
        if (scaled && g_log.load(std::memory_order_relaxed) && result > 0.0f)
            Record(reinterpret_cast<size_t>(_ReturnAddress()) - ModuleBase(), result);

        return result;
    }

#if MW3MOD_DIAGNOSTIC_HOOKS
    // The function that puts a shot inside its cone. It takes the cone in
    // degrees in xmm1, converts it to radians, takes the sine and walks one
    // iteration per pellet, asking the random number generator for an angle.
    // Watching the generator during a burst showed this loop, and only this
    // loop, firing once per shot, while the separate helper at +0x11CA70 that
    // looks like it does the same job is never called in single player. So the
    // cone is scaled here.
    //
    // The signature follows the Windows x64 convention: the float is the second
    // positional argument, so it lands in xmm1 and the other three pointers use
    // rcx, r8 and r9.
    constexpr size_t kBulletSpreadOffset = 0x11D4D0;
    constexpr unsigned char kBulletPrologue[] = {
        0x48, 0x8B, 0xC4, 0x55, 0x41, 0x55, 0x48, 0x8D, 0xA8,
    };

    // void __fastcall BulletSpread(void* owner, float spreadDeg, void* ray,
    //                              void* context)
    using BulletSpreadFn = void (*)(void* owner, float spreadDeg, void* ray, void* context);

    BulletSpreadFn g_bulletOriginal = nullptr;

    // The engine's own random number generator, a plain MSVC linear
    // congruential one. Only a dozen places in the whole binary call it, so
    // watching who asks for a random number while a trigger is pulled tells us
    // exactly which code puts the cone on a bullet. Diagnostic only.
    constexpr size_t kRngOffset = 0x107B0;
    constexpr unsigned char kRngPrologue[] = {0x69, 0x01, 0xFD, 0x43, 0x03, 0x00};

    // float __fastcall Rng(unsigned int* state)
    using RngFn = float (*)(unsigned int* state);

    RngFn g_rngOriginal = nullptr;
    ULONGLONG g_rngWindowStart = 0;

    struct RngCaller
    {
        size_t rva = 0;
        unsigned calls = 0;
    };

    constexpr size_t kMaxRngCallers = 12;
    RngCaller g_rngCallers[kMaxRngCallers];
    size_t g_rngCallerCount = 0;

    void RecordRng(size_t rva)
    {
        RngCaller* entry = nullptr;
        for (size_t i = 0; i < g_rngCallerCount; ++i)
        {
            if (g_rngCallers[i].rva == rva)
            {
                entry = &g_rngCallers[i];
                break;
            }
        }

        if (!entry && g_rngCallerCount < kMaxRngCallers)
        {
            entry = &g_rngCallers[g_rngCallerCount++];
            entry->rva = rva;
        }

        if (!entry)
            return;

        entry->calls += 1;

        const ULONGLONG now = GetTickCount64();
        if (g_rngWindowStart == 0)
            g_rngWindowStart = now;
        if (now - g_rngWindowStart < 1000)
            return;

        g_rngWindowStart = now;
        for (size_t i = 0; i < g_rngCallerCount; ++i)
        {
            LogF(L"spread: rng caller +0x%zx asked %u time(s) in the last second",
                 g_rngCallers[i].rva, g_rngCallers[i].calls);
            g_rngCallers[i].calls = 0;
        }
    }

    float __fastcall HookedRng(unsigned int* state)
    {
        const float value = g_rngOriginal(state);

        if (g_log.load(std::memory_order_relaxed))
            RecordRng(reinterpret_cast<size_t>(_ReturnAddress()) - ModuleBase());

        return value;
    }

    ULONGLONG g_bulletLogAt = 0;

    void __fastcall HookedBulletSpread(void* owner, float spreadDeg, void* ray, void* context)
    {
        g_bulletOriginal(owner, spreadDeg, ray, context);

        if (!g_log.load(std::memory_order_relaxed))
            return;

        const ULONGLONG now = GetTickCount64();
        if (now - g_bulletLogAt < 50)
            return;
        g_bulletLogAt = now;

        // The cone the engine hands the pellet loop, and the base direction it
        // spreads around. An aimed shot on the ACR reads 0.000 deg here, which
        // is how we know the aim cone is taken from the adsSpread query the
        // other hook scales rather than from this argument.
        const float* dir = static_cast<const float*>(ray);
        const float x = dir ? dir[0] : 0.0f;
        const float y = dir ? dir[1] : 0.0f;
        const float z = dir ? dir[2] : 0.0f;

        LogF(L"spread: bullet cone %.3f deg caller +0x%zx base %.4f %.4f %.4f",
             spreadDeg,
             reinterpret_cast<size_t>(_ReturnAddress()) - ModuleBase(), x, y, z);
    }
#endif
}

bool spread::Install()
{
    HMODULE module = GetModuleHandleW(nullptr);
    if (!module)
        return false;

    auto* base = reinterpret_cast<unsigned char*>(module);

    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED)
    {
        LogF(L"spread: MinHook init failed (%d)", static_cast<int>(init));
        return false;
    }

    bool any = false;

    if (std::memcmp(base + kAdsSpreadOffset, kPrologue, sizeof(kPrologue)) != 0)
    {
        LogF(L"spread: unexpected code at module+0x%zx, aim cone untouched", kAdsSpreadOffset);
    }
    else if (MH_CreateHook(base + kAdsSpreadOffset, &HookedAdsSpread, reinterpret_cast<void**>(&g_original)) == MH_OK &&
             MH_EnableHook(base + kAdsSpreadOffset) == MH_OK)
    {
        LogF(L"spread: hook at module+0x%zx", kAdsSpreadOffset);
        any = true;
    }
    else
    {
        LogF(L"spread: hook failed at module+0x%zx", kAdsSpreadOffset);
    }

#if MW3MOD_DIAGNOSTIC_HOOKS
    if (std::memcmp(base + kBulletSpreadOffset, kBulletPrologue, sizeof(kBulletPrologue)) != 0)
    {
        LogF(L"spread: unexpected code at module+0x%zx, bullet cone untouched", kBulletSpreadOffset);
    }
    else if (MH_CreateHook(base + kBulletSpreadOffset, &HookedBulletSpread,
                           reinterpret_cast<void**>(&g_bulletOriginal)) == MH_OK &&
             MH_EnableHook(base + kBulletSpreadOffset) == MH_OK)
    {
        LogF(L"spread: bullet hook at module+0x%zx", kBulletSpreadOffset);
        any = true;
    }
    else
    {
        LogF(L"spread: bullet hook failed at module+0x%zx", kBulletSpreadOffset);
    }

    if (std::memcmp(base + kRngOffset, kRngPrologue, sizeof(kRngPrologue)) != 0)
    {
        LogF(L"spread: unexpected code at module+0x%zx, rng watch off", kRngOffset);
    }
    else if (MH_CreateHook(base + kRngOffset, &HookedRng, reinterpret_cast<void**>(&g_rngOriginal)) == MH_OK &&
             MH_EnableHook(base + kRngOffset) == MH_OK)
    {
        LogF(L"spread: rng watch at module+0x%zx", kRngOffset);
        any = true;
    }
    else
    {
        LogF(L"spread: rng watch failed at module+0x%zx", kRngOffset);
    }
#else
    LogF(L"spread: diagnostic bullet/rng hooks disabled");
#endif

    return any;
}

void spread::Update(const ModSettings& settings)
{
    g_enabled.store(settings.spreadEnabled, std::memory_order_relaxed);
    g_aim.store(settings.spreadAimAccuracy, std::memory_order_relaxed);
    g_log.store(settings.spreadDebug || settings.recoilDebug, std::memory_order_relaxed);
}
