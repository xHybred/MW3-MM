#include "spread.h"

#include <windows.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <intrin.h>

#include "log.h"
#include "settings.h"

#include <MinHook.h>

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
        const float vanilla = g_original(clientNum, viaSlot);

        float result = vanilla;
        if (g_enabled.load(std::memory_order_relaxed))
        {
            const float strength = std::clamp(1.0f - g_aim.load(std::memory_order_relaxed), 0.0f, 1.0f);
            if (strength < 0.999f)
                result = vanilla * strength;
        }

        // Logged after scaling, so the numbers below are what the engine was
        // handed rather than what it stored.
        if (g_log.load(std::memory_order_relaxed) && result > 0.0f)
            Record(reinterpret_cast<size_t>(_ReturnAddress()) - ModuleBase(), result);

        return result;
    }
}

bool spread::Install()
{
    HMODULE module = GetModuleHandleW(nullptr);
    if (!module)
        return false;

    auto* base = reinterpret_cast<unsigned char*>(module);
    if (std::memcmp(base + kAdsSpreadOffset, kPrologue, sizeof(kPrologue)) != 0)
    {
        LogF(L"spread: unexpected code at module+0x%zx, aim cone untouched", kAdsSpreadOffset);
        return false;
    }

    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED)
    {
        LogF(L"spread: MinHook init failed (%d)", static_cast<int>(init));
        return false;
    }

    if (MH_CreateHook(base + kAdsSpreadOffset, &HookedAdsSpread, reinterpret_cast<void**>(&g_original)) != MH_OK ||
        MH_EnableHook(base + kAdsSpreadOffset) != MH_OK)
    {
        LogF(L"spread: hook failed at module+0x%zx", kAdsSpreadOffset);
        return false;
    }

    LogF(L"spread: hook at module+0x%zx", kAdsSpreadOffset);
    return true;
}

void spread::Update(const ModSettings& settings)
{
    g_enabled.store(settings.spreadEnabled, std::memory_order_relaxed);
    g_aim.store(settings.spreadAimAccuracy, std::memory_order_relaxed);
    g_log.store(settings.spreadDebug || settings.recoilDebug, std::memory_order_relaxed);
}
