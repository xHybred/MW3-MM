#include "viewmodel.h"

#include "log.h"

#include <windows.h>

#include <MinHook.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>

namespace
{
    // FUN_14006FD70 builds the first-person model's origin for the frame. The
    // last thing it does is add cg_gun_z times the view's forward axis
    // (client+0x26AA0), which moves the gun along the view axis and changes
    // how large it looks without touching the world camera. Doing the same
    // here per frame keeps the correction smooth through the aim transition.
    constexpr size_t kOriginOffset = 0x6FD70;
    constexpr unsigned char kPrologue[] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C, 0x24, 0x10};

    constexpr size_t kOriginVector = 0x10;
    constexpr size_t kForwardAxis = 0x26AA0;
    constexpr size_t kAimFraction = 0x338;

    // Distance from the camera to the visible part of the gun, measured by
    // matching cg_gun_z against the stock size at 110 degrees. Only used to
    // turn a wanted size ratio into a distance, so an approximate value keeps
    // the slider proportional.
    constexpr float kGunDistance = 3.5f;
    constexpr float kMaxOffset = 3.0f;
    constexpr float kPi = 3.14159265358979f;

    using OriginFn = void (*)(void*, void*, void*, void*);

    OriginFn g_original = nullptr;
    std::atomic<bool> g_enabled{true};
    std::atomic<float> g_influence{1.0f};
    std::atomic<float> g_fov{65.0f};
    std::atomic<float> g_pivot{65.0f};
    std::atomic<bool> g_debug{false};
    std::atomic<float> g_lastOffset{0.0f};
    std::atomic<ULONGLONG> g_nextLog{0};

    float OffsetFor(float fov, float pivot, float influence)
    {
        if (!(fov > 1.0f) || !(pivot > 1.0f))
            return 0.0f;

        // Apparent size falls off as 1/tan(fov/2). Anything above 1 means the
        // gun is drawn smaller than it was at the pivot field of view.
        const float r = std::tan(fov * kPi / 360.0f) / std::tan(pivot * kPi / 360.0f);
        if (!(r > 0.0f))
            return 0.0f;

        const float k = std::pow(r, 1.0f - influence);
        if (!std::isfinite(k) || k < 0.05f || k > 20.0f)
            return 0.0f;

        const float offset = kGunDistance * (1.0f - 1.0f / k);
        return std::clamp(offset, -kMaxOffset, kMaxOffset);
    }

    void __fastcall HookedOrigin(void* client, void* out, void* in, void* a4)
    {
        g_original(client, out, in, a4);

        if (!client || !out)
            return;

        const auto* bytes = static_cast<const unsigned char*>(client);
        const float blend = 1.0f - std::clamp(*reinterpret_cast<const float*>(bytes + kAimFraction), 0.0f, 1.0f);

        float offset = 0.0f;
        if (g_enabled.load(std::memory_order_relaxed))
            offset = OffsetFor(g_fov.load(std::memory_order_relaxed), g_pivot.load(std::memory_order_relaxed),
                               g_influence.load(std::memory_order_relaxed));

        // Aiming already narrows the view to roughly the pivot, so the
        // correction fades out with the aim fraction instead of shoving the
        // raised weapon into the camera.
        offset *= blend;
        g_lastOffset.store(offset, std::memory_order_relaxed);

        if (std::fabs(offset) > 0.0005f)
        {
            auto* origin = reinterpret_cast<float*>(static_cast<unsigned char*>(out) + kOriginVector);
            const auto* forward = reinterpret_cast<const float*>(bytes + kForwardAxis);
            origin[0] += offset * forward[0];
            origin[1] += offset * forward[1];
            origin[2] += offset * forward[2];
        }

        if (g_debug.load(std::memory_order_relaxed))
        {
            const ULONGLONG now = GetTickCount64();
            if (now >= g_nextLog.load(std::memory_order_relaxed))
            {
                g_nextLog.store(now + 500, std::memory_order_relaxed);
                LogF(L"viewmodel: fov %.2f pivot %.2f influence %.2f aim %.2f offset %.3f",
                     g_fov.load(std::memory_order_relaxed), g_pivot.load(std::memory_order_relaxed),
                     g_influence.load(std::memory_order_relaxed), 1.0f - blend, offset);
            }
        }
    }
}

bool viewmodel::Install()
{
    auto* target = reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr)) + kOriginOffset);

    if (memcmp(target, kPrologue, sizeof(kPrologue)) != 0)
    {
        LogF(L"viewmodel: unexpected code at module+0x%zx, hook skipped", kOriginOffset);
        return false;
    }

    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED)
    {
        LogF(L"viewmodel: MH_Initialize failed (%d)", static_cast<int>(init));
        return false;
    }

    if (MH_CreateHook(target, &HookedOrigin, reinterpret_cast<void**>(&g_original)) != MH_OK ||
        MH_EnableHook(target) != MH_OK)
    {
        LogF(L"viewmodel: hook at module+0x%zx failed", kOriginOffset);
        return false;
    }

    LogF(L"viewmodel: hook at module+0x%zx", kOriginOffset);
    return true;
}

void viewmodel::Update(const ModSettings& settings, float pivotFov)
{
    g_enabled.store(settings.viewmodelEnabled, std::memory_order_relaxed);
    g_influence.store(settings.viewmodelInfluence, std::memory_order_relaxed);
    g_fov.store(settings.fov, std::memory_order_relaxed);
    g_pivot.store(pivotFov, std::memory_order_relaxed);
    g_debug.store(settings.viewmodelDebug, std::memory_order_relaxed);
}
