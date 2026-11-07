// Proxy for X3DAudio1_7.dll. The game imports the real DirectX X3DAudio DLL at
// startup, so dropping this file next to iw5sp.exe gives us code execution with
// no injector and no extra process. Both exports are forwarded to the system
// copy; nothing here is called until after the loader lock is released.

#include <windows.h>
#include <string>

namespace {

HMODULE RealModule() {
    static HMODULE module = []() -> HMODULE {
        wchar_t systemDir[MAX_PATH]{};
        UINT length = GetSystemDirectoryW(systemDir, MAX_PATH);
        if (length == 0 || length >= MAX_PATH) return nullptr;

        const std::wstring path = std::wstring(systemDir) + L"\\x3daudio1_7.dll";
        return LoadLibraryW(path.c_str());
    }();
    return module;
}

using InitializeFn = DWORD(WINAPI*)(UINT32, float, BYTE*);
using CalculateFn = void(WINAPI*)(const BYTE*, const void*, const void*, UINT32, void*);

}  // namespace

extern "C" __declspec(dllexport) DWORD WINAPI X3DAudioInitialize(UINT32 channelMask,
                                                                 float speedOfSound,
                                                                 BYTE* instance) {
    static InitializeFn real = reinterpret_cast<InitializeFn>(
        GetProcAddress(RealModule(), "X3DAudioInitialize"));
    if (!real) return static_cast<DWORD>(E_FAIL);
    return real(channelMask, speedOfSound, instance);
}

extern "C" __declspec(dllexport) void WINAPI X3DAudioCalculate(const BYTE* instance,
                                                               const void* listener,
                                                               const void* emitter,
                                                               UINT32 flags,
                                                               void* settings) {
    static CalculateFn real = reinterpret_cast<CalculateFn>(
        GetProcAddress(RealModule(), "X3DAudioCalculate"));
    if (real) real(instance, listener, emitter, flags, settings);
}
