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
std::atomic<unsigned int> g_color{0xFFFFFFFFu};

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
void** g_d3d9Vtable = nullptr;

struct Vertex {
    float x, y, z, rhw;
    DWORD color;
};
constexpr DWORD kFvf = D3DFVF_XYZRHW | D3DFVF_DIFFUSE;

void DrawBars(IDirect3DDevice9* device, const D3DVIEWPORT9& vp) {
    const float h = static_cast<float>(vp.Height);
    const float w = static_cast<float>(vp.Width);
    const float scale = h / 1080.0f;
    const float size = g_size.load(std::memory_order_relaxed);
    const float alpha = g_alpha.load(std::memory_order_relaxed);
    if (alpha <= 0.001f) return;

    const float thickness = 2.5f * scale;
    const float gap = 4.0f * scale * size;
    const float length = 11.0f * scale * size;
    const float cx = w * 0.5f;
    const float cy = h * 0.5f + g_verticalOffset.load(std::memory_order_relaxed) * scale;

    const DWORD color = g_color.load(std::memory_order_relaxed);
    Vertex vertices[24];
    int n = 0;

    auto rect = [&](float x0, float y0, float x1, float y1) {
        vertices[n++] = {x0, y0, 0.0f, 1.0f, color};
        vertices[n++] = {x1, y0, 0.0f, 1.0f, color};
        vertices[n++] = {x1, y1, 0.0f, 1.0f, color};
        vertices[n++] = {x0, y0, 0.0f, 1.0f, color};
        vertices[n++] = {x1, y1, 0.0f, 1.0f, color};
        vertices[n++] = {x0, y1, 0.0f, 1.0f, color};
    };

    const float half = thickness * 0.5f;
    rect(cx - half, cy - gap - length, cx + half, cy - gap);
    rect(cx - half, cy + gap, cx + half, cy + gap + length);
    rect(cx - gap - length, cy - half, cx - gap, cy + half);
    rect(cx + gap, cy - half, cx + gap + length, cy + half);

    device->SetTexture(0, nullptr);
    device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
    device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_DIFFUSE);
    device->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG1);
    device->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_DIFFUSE);
    device->SetFVF(kFvf);
    device->DrawPrimitiveUP(D3DPT_TRIANGLELIST, 8, vertices, sizeof(Vertex));
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
    const float outer = 15.0f * scale * size;
    const float inner = outer - 2.2f * scale;
    const float dot = 1.6f * scale;

    constexpr int kSegments = 96;
    constexpr float kPi = 3.14159265358979323846f;
    constexpr float kGap = 0.20f;  // radians either side of each cardinal

    Vertex vertices[512];
    int n = 0;

    auto add = [&](float x, float y) {
        if (n < 512) vertices[n++] = {x, y, 0.0f, 1.0f, color};
    };

    for (int i = 0; i < kSegments; ++i) {
        const float a0 = (2.0f * kPi * i) / kSegments;
        const float a1 = (2.0f * kPi * (i + 1)) / kSegments;
        const float mid = (a0 + a1) * 0.5f;
        const float nearest = fabsf(remainderf(mid, kPi * 0.5f));
        if (nearest < kGap) continue;

        const float c0 = cosf(a0), s0 = sinf(a0);
        const float c1 = cosf(a1), s1 = sinf(a1);
        add(cx + c0 * outer, cy + s0 * outer);
        add(cx + c0 * inner, cy + s0 * inner);
        add(cx + c1 * outer, cy + s1 * outer);
        add(cx + c1 * outer, cy + s1 * outer);
        add(cx + c0 * inner, cy + s0 * inner);
        add(cx + c1 * inner, cy + s1 * inner);
    }

    // Centre dot as two triangles.
    add(cx - dot, cy - dot);
    add(cx + dot, cy - dot);
    add(cx + dot, cy + dot);
    add(cx - dot, cy - dot);
    add(cx + dot, cy + dot);
    add(cx - dot, cy + dot);

    device->SetTexture(0, nullptr);
    device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
    device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_DIFFUSE);
    device->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG1);
    device->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_DIFFUSE);
    device->SetFVF(kFvf);
    device->DrawPrimitiveUP(D3DPT_TRIANGLELIST, n / 3, vertices, sizeof(Vertex));
}

void ApplyDrawState(IDirect3DDevice9* device) {
    device->SetVertexShader(nullptr);
    device->SetPixelShader(nullptr);
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

    const ULONGLONG drew = g_engineDrewAt.load(std::memory_order_relaxed);
    if (drew == 0 || GetTickCount64() - drew > 250) return;

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

HRESULT __stdcall HookedSwapPresent(IDirect3DSwapChain9* chain, const RECT* src, const RECT* dst,
                                    HWND hwnd, const RGNDATA* dirty, DWORD flags) {
    if (g_device) {
        __try {
            DrawOverlay(g_device);
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            LogF(L"overlay: draw faulted");
        }
    }
    return g_swapPresentOriginal(chain, src, dst, hwnd, dirty, flags);
}

HRESULT __stdcall HookedCreateDevice(IDirect3D9* self, UINT adapter, D3DDEVTYPE type, HWND focus,
                                     DWORD flags, D3DPRESENT_PARAMETERS* params,
                                     IDirect3DDevice9** out) {
    const HRESULT hr = g_createDeviceOriginal(self, adapter, type, focus, flags, params, out);
    if (SUCCEEDED(hr) && out != nullptr && *out != nullptr && g_swapPresentOriginal == nullptr) {
        g_device = *out;

        IDirect3DSwapChain9* chain = nullptr;
        if (FAILED(g_device->GetSwapChain(0, &chain)) || !chain) {
            LogF(L"overlay: no swap chain, custom crosshair unavailable");
            return hr;
        }
        void** vtable = *reinterpret_cast<void***>(chain);
        const auto present = reinterpret_cast<SwapPresentFn>(vtable[3]);
        if (MH_CreateHook(reinterpret_cast<void*>(present),
                          reinterpret_cast<void*>(&HookedSwapPresent),
                          reinterpret_cast<void**>(&g_swapPresentOriginal)) == MH_OK &&
            MH_EnableHook(reinterpret_cast<void*>(present)) == MH_OK) {
            LogF(L"overlay: swap chain present hook installed");
        } else {
            g_swapPresentOriginal = nullptr;
            LogF(L"overlay: swap chain present hook failed");
        }
        chain->Release();
    }
    return hr;
}

IDirect3D9* __stdcall HookedDirect3DCreate9(UINT sdkVersion) {
    IDirect3D9* d3d9 = g_create9Original(sdkVersion);
    if (!d3d9) return d3d9;

    void** vtable = *reinterpret_cast<void***>(d3d9);
    g_d3d9Vtable = new void*[32];
    memcpy(g_d3d9Vtable, vtable, sizeof(void*) * 32);
    g_d3d9Vtable[15] = reinterpret_cast<void*>(&HookedCreateDevice);
    g_createDeviceOriginal = reinterpret_cast<CreateDeviceFn>(vtable[15]);
    *reinterpret_cast<void***>(d3d9) = g_d3d9Vtable;
    return d3d9;
}

}  // namespace

void MarkEngineCrosshair() {
    g_engineDrewAt.store(GetTickCount64(), std::memory_order_relaxed);
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
    g_style.store(settings.crosshairStyle, std::memory_order_relaxed);
    g_size.store(settings.crosshairSize, std::memory_order_relaxed);
    g_alpha.store(settings.crosshairOpacity, std::memory_order_relaxed);
}

void SetVerticalOffset(float offset) {
    g_verticalOffset.store(offset, std::memory_order_relaxed);
}

}  // namespace overlay
