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
    // engine adds cg_gun_x/y/z along the view's own axes here, so the same
    // place can shift the weapon by a fixed amount.
    constexpr size_t kOriginOffset = 0x6FD70;
    constexpr unsigned char kPrologue[] = {0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C, 0x24, 0x10};

    constexpr size_t kOriginVector = 0x10;
    // client+0x26A88 is the axis the gun slides along in and out, and
    // client+0x26A94 is the lateral one. The names came from the other way
    // round and it showed in game: the depth slider moved the gun sideways.
    // 0x26A88 points away into the world (it logged as -0.983, 0.142, 0.115
    // with the camera facing west), so a positive X pushes the gun out and
    // makes it smaller. Z is negated so a positive slider moves it right,
    // and Y so it moves up.
    constexpr size_t kForwardAxis = 0x26A88;
    constexpr size_t kLeftAxis = 0x26A94;
    constexpr size_t kUpAxis = 0x26AA0;

    constexpr float kMaxOffset = 3.0f;
    constexpr float kMaxPullIn = 1.5f;

    using OriginFn = void (*)(void*, void*, void*, void*);

    OriginFn g_original = nullptr;
    std::atomic<bool> g_enabled{true};
    std::atomic<float> g_x{0.0f};
    std::atomic<float> g_y{0.0f};
    std::atomic<float> g_z{0.0f};
    std::atomic<bool> g_debug{false};
    std::atomic<ULONGLONG> g_nextLog{0};

    void __fastcall HookedOrigin(void* client, void* out, void* in, void* a4)
    {
        g_original(client, out, in, a4);

        if (!client || !out || !g_enabled.load(std::memory_order_relaxed))
            return;

        const float x = std::clamp(g_x.load(std::memory_order_relaxed), -kMaxPullIn, kMaxOffset);
        const float y = std::clamp(g_y.load(std::memory_order_relaxed), -kMaxOffset, kMaxOffset);
        const float z = std::clamp(g_z.load(std::memory_order_relaxed), -kMaxOffset, kMaxOffset);

        if (std::fabs(x) > 0.0005f || std::fabs(y) > 0.0005f || std::fabs(z) > 0.0005f)
        {
            const auto* bytes = static_cast<const unsigned char*>(client);
            auto* origin = reinterpret_cast<float*>(static_cast<unsigned char*>(out) + kOriginVector);
            const auto* left = reinterpret_cast<const float*>(bytes + kLeftAxis);
            const auto* forward = reinterpret_cast<const float*>(bytes + kForwardAxis);
            const auto* up = reinterpret_cast<const float*>(bytes + kUpAxis);

            for (size_t i = 0; i < 3; ++i)
                origin[i] += x * forward[i] - z * left[i] - y * up[i];
        }

        if (g_debug.load(std::memory_order_relaxed))
        {
            const ULONGLONG now = GetTickCount64();
            if (now >= g_nextLog.load(std::memory_order_relaxed))
            {
                g_nextLog.store(now + 500, std::memory_order_relaxed);
                LogF(L"viewmodel: x %.2f y %.2f z %.2f", x, y, z);
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

void viewmodel::Update(const ModSettings& settings)
{
    g_enabled.store(settings.viewmodelEnabled, std::memory_order_relaxed);
    g_x.store(settings.viewmodelX, std::memory_order_relaxed);
    g_y.store(settings.viewmodelY, std::memory_order_relaxed);
    g_z.store(settings.viewmodelZ, std::memory_order_relaxed);
    g_debug.store(settings.viewmodelDebug, std::memory_order_relaxed);
}
