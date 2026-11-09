#include "overlay.h"

#include <windows.h>

#include <d3d9.h>

#include <atomic>
#include <cmath>
#include <cstring>

#include "MinHook.h"
#include "log.h"

namespace overlay {
namespace {

// The game's crosshair is four textured quads fed by an 8x8 texture, so at any
// modern resolution it is an upscaled bitmap. Drawing the same shape ourselves
// in screen pixels keeps the edges clean. The device is taken from
// Direct3DCreate9 / CreateDevice; the draw goes in at the swap chain's Present,
// which is the last thing the frame does.
std::atomic<bool> g_enabled{false};
std::atomic<int> g_style{0};
std::atomic<float> g_size{1.0f};
std::atomic<float> g_alpha{1.0f};
std::atomic<float> g_verticalOffset{0.0f};
std::atomic<ULONGLONG> g_engineDrewAt{0};
std::atomic<ULONGLONG> g_aimingAt{0};
std::atomic<bool> g_alwaysShow{false};
std::atomic<unsigned int> g_color{0xFFFFFFFFu};
std::atomic<bool> g_frameTime{false};

// Frame timing window, kept to report how expensive the frame actually is
// rather than how many screenshots were skipped.
ULONGLONG g_frameWindowStart = 0;
ULONGLONG g_lastFrameAt = 0;
unsigned g_frameCount = 0;
float g_worstFrameMs = 0.0f;

using Direct3DCreate9Fn = IDirect3D9*(__stdcall*)(UINT);
using CreateDeviceFn = HRESULT(__stdcall*)(IDirect3D9*, UINT, D3DDEVTYPE, HWND, DWORD,
                                           D3DPRESENT_PARAMETERS*, IDirect3DDevice9**);
using SwapPresentFn = HRESULT(__stdcall*)(IDirect3DSwapChain9*, const RECT*, const RECT*, HWND,
                                          const RGNDATA*, DWORD);

Direct3DCreate9Fn g_create9Original = nullptr;
CreateDeviceFn g_createDeviceOriginal = nullptr;
SwapPresentFn g_swapPresentOriginal = nullptr;

IDirect3DDevice9* g_device = nullptr;
IDirect3DStateBlock9* g_block = nullptr;

struct Vertex {
    float x, y, z, rhw;
    DWORD color;
};
constexpr DWORD kFvf = D3DFVF_XYZRHW | D3DFVF_DIFFUSE;

// Outline is a dark copy of each shape, grown by the same amount on every side.
constexpr float kOutlineAlpha = 0.55f;
constexpr float kBarThickness = 2.5f;
constexpr float kBarGap = 3.5f;
constexpr float kBarLength = 6.5f;
constexpr float kBarDot = 1.0f;
constexpr float kRingOuter = 15.0f;
constexpr float kRingThickness = 2.2f;
constexpr float kRingDot = 1.6f;

struct Batch {
    Vertex vertices[1024];
    int count = 0;

    void Triangle(float x0, float y0, float x1, float y1, float x2, float y2, DWORD color) {
        if (count + 3 > 1024) return;
        vertices[count++] = {x0, y0, 0.0f, 1.0f, color};
        vertices[count++] = {x1, y1, 0.0f, 1.0f, color};
        vertices[count++] = {x2, y2, 0.0f, 1.0f, color};
    }

    void Rect(float x0, float y0, float x1, float y1, DWORD color) {
        Triangle(x0, y0, x1, y0, x1, y1, color);
        Triangle(x0, y0, x1, y1, x0, y1, color);
    }

    void Draw(IDirect3DDevice9* device) const {
        if (count < 3) return;
        device->DrawPrimitiveUP(D3DPT_TRIANGLELIST, count / 3, vertices, sizeof(Vertex));
    }
};

DWORD OutlineColor(DWORD color) {
    const unsigned alpha = (color >> 24) & 0xFFu;
    return static_cast<unsigned>(alpha * kOutlineAlpha + 0.5f) << 24;
}

void DrawBars(IDirect3DDevice9* device, const D3DVIEWPORT9& vp) {
    const float h = static_cast<float>(vp.Height);
    const float w = static_cast<float>(vp.Width);
    const float scale = h / 1080.0f;
    const float size = g_size.load(std::memory_order_relaxed);
    const float alpha = g_alpha.load(std::memory_order_relaxed);
    if (alpha <= 0.001f) return;

    const float thickness = kBarThickness * scale;
    const float gap = kBarGap * scale * size;
    const float length = kBarLength * scale * size;
    const float half = thickness * 0.5f;
    const float dot = kBarDot * scale * size;
    const float cx = w * 0.5f;
    const float cy = h * 0.5f + g_verticalOffset.load(std::memory_order_relaxed) * scale;

    float rects[5][4] = {
        {cx - half, cy - gap - length, cx + half, cy - gap},
        {cx - half, cy + gap, cx + half, cy + gap + length},
        {cx - gap - length, cy - half, cx - gap, cy + half},
        {cx + gap, cy - half, cx + gap + length, cy + half},
        {cx - dot, cy - dot, cx + dot, cy + dot},
    };

    const DWORD color = g_color.load(std::memory_order_relaxed);
    const DWORD outlineColor = OutlineColor(color);
    const float outline = fminf(1.0f * scale, thickness * 0.35f);

    Batch outlineBatch;
    Batch fillBatch;
    for (const auto& r : rects) {
        outlineBatch.Rect(r[0] - outline, r[1] - outline, r[2] + outline, r[3] + outline,
                          outlineColor);
        fillBatch.Rect(r[0], r[1], r[2], r[3], color);
    }
    outlineBatch.Draw(device);
    fillBatch.Draw(device);
}

// A ring broken by four gaps at the cardinal directions, with a small centre
// dot, like the Halo crosshair.
void DrawRing(IDirect3DDevice9* device, const D3DVIEWPORT9& vp) {
    const float h = static_cast<float>(vp.Height);
    const float w = static_cast<float>(vp.Width);
    const float scale = h / 1080.0f;
    const float size = g_size.load(std::memory_order_relaxed);
    const DWORD color = g_color.load(std::memory_order_relaxed);
    if ((color >> 24) == 0) return;

    const float cx = w * 0.5f;
    const float cy = h * 0.5f + g_verticalOffset.load(std::memory_order_relaxed) * scale;
    const float outer = kRingOuter * scale * size;
    const float inner = outer - kRingThickness * scale;
    const float dot = kRingDot * scale * size;
    const float outline = 1.0f * scale;
    const DWORD outlineColor = OutlineColor(color);

    constexpr int kSegments = 96;
    constexpr float kPi = 3.14159265358979323846f;
    constexpr float kGap = 0.20f;  // radians either side of each cardinal

    auto ring = [&](Batch& batch, float outRadius, float inRadius, float dotHalf,
                    DWORD vertexColor) {
        for (int i = 0; i < kSegments; ++i) {
            const float a0 = (2.0f * kPi * i) / kSegments;
            const float a1 = (2.0f * kPi * (i + 1)) / kSegments;
            const float mid = (a0 + a1) * 0.5f;
            const float nearest = fabsf(remainderf(mid, kPi * 0.5f));
            if (nearest < kGap) continue;

            const float c0 = cosf(a0), s0 = sinf(a0);
            const float c1 = cosf(a1), s1 = sinf(a1);
            batch.Triangle(cx + c0 * outRadius, cy + s0 * outRadius,
                           cx + c0 * inRadius, cy + s0 * inRadius,
                           cx + c1 * outRadius, cy + s1 * outRadius, vertexColor);
            batch.Triangle(cx + c1 * outRadius, cy + s1 * outRadius,
                           cx + c0 * inRadius, cy + s0 * inRadius,
                           cx + c1 * inRadius, cy + s1 * inRadius, vertexColor);
        }
        batch.Rect(cx - dotHalf, cy - dotHalf, cx + dotHalf, cy + dotHalf, vertexColor);
    };

    Batch outlineBatch;
    Batch fillBatch;
    ring(outlineBatch, outer + outline, inner - outline, dot + outline, outlineColor);
    ring(fillBatch, outer, inner, dot, color);
    outlineBatch.Draw(device);
    fillBatch.Draw(device);
}

void ApplyDrawState(IDirect3DDevice9* device) {
    device->SetVertexShader(nullptr);
    device->SetPixelShader(nullptr);
    device->SetTexture(0, nullptr);
    device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
    device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_DIFFUSE);
    device->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG1);
    device->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_DIFFUSE);
    device->SetFVF(kFvf);
    device->SetRenderState(D3DRS_ZENABLE, D3DZB_FALSE);
    device->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
    device->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
    device->SetRenderState(D3DRS_LIGHTING, FALSE);
    device->SetRenderState(D3DRS_FOGENABLE, FALSE);
    device->SetRenderState(D3DRS_STENCILENABLE, FALSE);
    device->SetRenderState(D3DRS_SCISSORTESTENABLE, FALSE);
    device->SetRenderState(D3DRS_ALPHATESTENABLE, FALSE);
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
    device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
}

void DrawOverlay(IDirect3DDevice9* device) {
    if (!g_enabled.load(std::memory_order_relaxed)) return;
    if (g_style.load(std::memory_order_relaxed) == 0) return;

    const ULONGLONG now = GetTickCount64();
    const ULONGLONG drew = g_engineDrewAt.load(std::memory_order_relaxed);
    const ULONGLONG aimed = g_aimingAt.load(std::memory_order_relaxed);
    const bool engineDrew = drew != 0 && now - drew <= 250;
    const bool aiming = g_alwaysShow.load(std::memory_order_relaxed) && aimed != 0 &&
                        now - aimed <= 250;
    if (!engineDrew && !aiming) return;

    D3DVIEWPORT9 vp{};
    if (FAILED(device->GetViewport(&vp)) || vp.Width == 0 || vp.Height == 0) return;

    if (!g_block && FAILED(device->CreateStateBlock(D3DSBT_ALL, &g_block))) {
        g_block = nullptr;
    }
    if (g_block) g_block->Capture();
    ApplyDrawState(device);
    if (g_style.load(std::memory_order_relaxed) == 2) {
        DrawRing(device, vp);
    } else {
        DrawBars(device, vp);
    }
    if (g_block) g_block->Apply();
}

void SampleFrameTime() {
    if (!g_frameTime.load(std::memory_order_relaxed)) {
        g_frameWindowStart = 0;
        g_frameCount = 0;
        g_worstFrameMs = 0.0f;
        return;
    }

    const ULONGLONG now = GetTickCount64();
    if (g_lastFrameAt != 0) {
        const float ms = static_cast<float>(now - g_lastFrameAt);
        if (ms > g_worstFrameMs) g_worstFrameMs = ms;
    }
    g_lastFrameAt = now;

    if (g_frameWindowStart == 0) {
        g_frameWindowStart = now;
        g_frameCount = 0;
        g_worstFrameMs = 0.0f;
        return;
    }

    ++g_frameCount;
    const ULONGLONG span = now - g_frameWindowStart;
    if (span < 1000) return;

    const double seconds = static_cast<double>(span) / 1000.0;
    const double fps = g_frameCount / seconds;
    LogF(L"frame: %.1f fps, %.2f ms average, %.2f ms worst over %u frames", fps,
         1000.0 / fps, static_cast<double>(g_worstFrameMs), g_frameCount);
    g_frameWindowStart = now;
    g_frameCount = 0;
    g_worstFrameMs = 0.0f;
}

HRESULT __stdcall HookedSwapPresent(IDirect3DSwapChain9* chain, const RECT* src, const RECT* dst,
                                    HWND hwnd, const RGNDATA* dirty, DWORD flags) {
    SampleFrameTime();
    if (g_device) {
        __try {
            DrawOverlay(g_device);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            LogF(L"overlay: draw faulted");
        }
    }
    return g_swapPresentOriginal(chain, src, dst, hwnd, dirty, flags);
}

// ReShade and dgVoodoo both wrap d3d9 and can hand this code pointers that are
// not what the interface claims, so a pointer is checked before it is used.
bool ReadableMemory(const void* address) {
    if (address == nullptr) return false;
    MEMORY_BASIC_INFORMATION info{};
    if (VirtualQuery(address, &info, sizeof(info)) == 0) return false;
    if (info.State != MEM_COMMIT) return false;
    return (info.Protect & (PAGE_NOACCESS | PAGE_GUARD)) == 0;
}

HRESULT __stdcall HookedCreateDevice(IDirect3D9* self, UINT adapter, D3DDEVTYPE type, HWND focus,
                                     DWORD flags, D3DPRESENT_PARAMETERS* params,
                                     IDirect3DDevice9** out) {
    const HRESULT hr = g_createDeviceOriginal(self, adapter, type, focus, flags, params, out);
    if (FAILED(hr) || out == nullptr || g_swapPresentOriginal != nullptr) return hr;

    // A proxy in front of d3d9 (ReShade, dgVoodoo) can call this entry with
    // arguments that do not match its real signature, so the result is checked
    // and the whole probe runs guarded: a bad pointer must never take the game
    // down just because the crosshair cannot be drawn.
    IDirect3DDevice9* device = nullptr;
    IDirect3DSwapChain9* chain = nullptr;
    void* present = nullptr;

    __try {
        if (reinterpret_cast<uintptr_t>(out) >= 0x10000 && ReadableMemory(out)) {
            device = *out;
            if (device != nullptr && ReadableMemory(device) &&
                device->GetSwapChain(0, &chain) == D3D_OK && chain != nullptr &&
                ReadableMemory(chain)) {
                present = (*reinterpret_cast<void***>(chain))[3];
            }
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        device = nullptr;
        chain = nullptr;
        present = nullptr;
    }

    if (device == nullptr || present == nullptr) {
        if (chain != nullptr) chain->Release();
        LogF(L"overlay: device behind the d3d9 proxy is not usable, custom crosshair off");
        return hr;
    }

    g_device = device;
    LogF(L"overlay: device %p created", g_device);

    if (MH_CreateHook(present, reinterpret_cast<void*>(&HookedSwapPresent),
                      reinterpret_cast<void**>(&g_swapPresentOriginal)) == MH_OK &&
        MH_EnableHook(present) == MH_OK) {
        LogF(L"overlay: swap chain present hook installed");
    } else {
        g_swapPresentOriginal = nullptr;
        LogF(L"overlay: swap chain present hook failed");
    }
    chain->Release();
    return hr;
}

IDirect3D9* __stdcall HookedDirect3DCreate9(UINT sdkVersion) {
    IDirect3D9* d3d9 = g_create9Original(sdkVersion);
    if (!d3d9 || g_createDeviceOriginal != nullptr) return d3d9;

    // Hook the CreateDevice entry instead of swapping the object's vtable: a
    // wrapper in front of d3d9 (dgVoodoo for the Vulkan/D3D11 route) is free to
    // lay its vtable out the way it likes, and replacing it made the call arrive
    // with the wrong arguments.
    void** vtable = *reinterpret_cast<void***>(d3d9);
    void* createDevice = vtable[15];
    if (MH_CreateHook(createDevice,
                      reinterpret_cast<void*>(&HookedCreateDevice),
                      reinterpret_cast<void**>(&g_createDeviceOriginal)) == MH_OK &&
        MH_EnableHook(createDevice) == MH_OK) {
        LogF(L"overlay: create device hook installed");
    } else {
        g_createDeviceOriginal = nullptr;
        LogF(L"overlay: create device hook failed");
    }
    return d3d9;
}

}  // namespace

void MarkEngineCrosshair() {
    g_engineDrewAt.store(GetTickCount64(), std::memory_order_relaxed);
}

void MarkAiming() {
    g_aimingAt.store(GetTickCount64(), std::memory_order_relaxed);
}

void SetColor(float r, float g, float b, float a) {
    auto channel = [](float v) {
        if (!(v > 0.0f)) return 0u;
        if (v > 1.0f) v = 1.0f;
        return static_cast<unsigned int>(v * 255.0f + 0.5f);
    };
    g_color.store((channel(a) << 24) | (channel(r) << 16) | (channel(g) << 8) | channel(b),
                  std::memory_order_relaxed);
}

bool Install() {
    HMODULE d3d9 = GetModuleHandleW(L"d3d9.dll");
    if (!d3d9) d3d9 = LoadLibraryW(L"d3d9.dll");
    if (!d3d9) {
        LogF(L"overlay: d3d9.dll not available, custom crosshair unavailable");
        return false;
    }
    const auto create9 = reinterpret_cast<Direct3DCreate9Fn>(GetProcAddress(d3d9, "Direct3DCreate9"));
    if (!create9) {
        LogF(L"overlay: Direct3DCreate9 not exported");
        return false;
    }

    const MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED) {
        LogF(L"overlay: MH_Initialize failed (%d)", init);
        return false;
    }
    if (MH_CreateHook(reinterpret_cast<void*>(create9),
                      reinterpret_cast<void*>(&HookedDirect3DCreate9),
                      reinterpret_cast<void**>(&g_create9Original)) != MH_OK ||
        MH_EnableHook(reinterpret_cast<void*>(create9)) != MH_OK) {
        LogF(L"overlay: Direct3DCreate9 hook failed");
        return false;
    }
    LogF(L"overlay: waiting for the D3D9 device");
    return true;
}

void Update(const ModSettings& settings) {
    g_enabled.store(settings.crosshairEnabled, std::memory_order_relaxed);
    g_alwaysShow.store(settings.crosshairAlwaysShow, std::memory_order_relaxed);
    g_frameTime.store(settings.frameTimeDebug, std::memory_order_relaxed);
    g_style.store(settings.crosshairStyle, std::memory_order_relaxed);
    g_size.store(settings.crosshairSize, std::memory_order_relaxed);
    g_alpha.store(settings.crosshairOpacity, std::memory_order_relaxed);
}

void SetVerticalOffset(float offset) {
    g_verticalOffset.store(offset, std::memory_order_relaxed);
}

}  // namespace overlay
