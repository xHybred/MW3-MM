#include "settings.h"

#include "log.h"

#include <windows.h>
#include <cwchar>
#include <cstdlib>

std::wstring SettingsPath() {
    return DataDir() + L"\\settings.ini";
}

static float ReadFloatKey(const std::wstring& path, const wchar_t* section, const wchar_t* key, float fallback) {
    wchar_t buffer[64]{};
    GetPrivateProfileStringW(section, key, L"", buffer, static_cast<DWORD>(std::size(buffer)), path.c_str());
    if (buffer[0] == L'\0') return fallback;
    wchar_t* end = nullptr;
    double value = wcstod(buffer, &end);
    if (end == buffer) return fallback;
    return static_cast<float>(value);
}

static float Clamp01(float value) {
    if (value < 0.0f) return 0.0f;
    if (value > 1.0f) return 1.0f;
    return value;
}

bool LoadSettings(ModSettings& out) {
    const std::wstring path = SettingsPath();
    if (GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES) return false;

    out.enabled = GetPrivateProfileIntW(L"FOV", L"Enabled", 1, path.c_str()) != 0;
    out.affected = GetPrivateProfileIntW(L"FOV", L"Affected", 1, path.c_str()) != 0;
    out.fov = ReadFloatKey(path, L"FOV", L"Value", 90.0f);
    out.aimEnabled = GetPrivateProfileIntW(L"Aim", L"Enabled", 1, path.c_str()) != 0;
    out.aimFlat = GetPrivateProfileIntW(L"Aim", L"Flat", 0, path.c_str()) != 0;
    out.aimZoom = ReadFloatKey(path, L"Aim", L"Zoom", 1.1f);
    out.flinchEnabled = GetPrivateProfileIntW(L"Flinch", L"Enabled", 1, path.c_str()) != 0;
    out.flinchStrength = Clamp01(ReadFloatKey(path, L"Flinch", L"Strength", 1.0f));
    out.blurEnabled = GetPrivateProfileIntW(L"Blur", L"Enabled", 1, path.c_str()) != 0;
    out.blurStrength = Clamp01(ReadFloatKey(path, L"Blur", L"Strength", 1.0f));
    out.blurDebug = GetPrivateProfileIntW(L"Debug", L"BlurCalls", 0, path.c_str()) != 0;
    out.recoilEnabled = GetPrivateProfileIntW(L"Recoil", L"Enabled", 1, path.c_str()) != 0;
    out.recoilStrength = Clamp01(ReadFloatKey(path, L"Recoil", L"Strength", 1.0f));
    out.recoilDebug = GetPrivateProfileIntW(L"Debug", L"Recoil", 0, path.c_str()) != 0;
    out.swayDebug = GetPrivateProfileIntW(L"Debug", L"Sway", 0, path.c_str()) != 0;
    out.swayEnabled = GetPrivateProfileIntW(L"Sway", L"Enabled", 1, path.c_str()) != 0;
    out.swayStrength = Clamp01(ReadFloatKey(path, L"Sway", L"Strength", 1.0f));
    out.flashEnabled = GetPrivateProfileIntW(L"Flash", L"Enabled", 1, path.c_str()) != 0;
    out.spreadEnabled = GetPrivateProfileIntW(L"Spread", L"Enabled", 1, path.c_str()) != 0;
    out.spreadAccuracy = Clamp01(ReadFloatKey(path, L"Spread", L"Accuracy", 0.0f));
    out.spreadBloom = Clamp01(ReadFloatKey(path, L"Spread", L"Bloom", 0.0f));
    out.spreadDebug = GetPrivateProfileIntW(L"Debug", L"Spread", 0, path.c_str()) != 0;
    out.viewmodelEnabled = GetPrivateProfileIntW(L"Viewmodel", L"Enabled", 1, path.c_str()) != 0;
    out.viewmodelZ = ReadFloatKey(path, L"Viewmodel", L"ZOffset", 0.0f);
    out.viewmodelY = ReadFloatKey(path, L"Viewmodel", L"YOffset", 0.0f);
    out.viewmodelX = ReadFloatKey(path, L"Viewmodel", L"XOffset", 0.0f);
    if (out.viewmodelX < -1.5f) out.viewmodelX = -1.5f;
    if (out.viewmodelX > 3.0f) out.viewmodelX = 3.0f;
    if (out.viewmodelZ < -3.0f) out.viewmodelZ = -3.0f;
    if (out.viewmodelZ > 3.0f) out.viewmodelZ = 3.0f;
    if (out.viewmodelY < -3.0f) out.viewmodelY = -3.0f;
    if (out.viewmodelY > 3.0f) out.viewmodelY = 3.0f;
    out.viewmodelDebug = GetPrivateProfileIntW(L"Debug", L"Viewmodel", 0, path.c_str()) != 0;
    out.crosshairEnabled = GetPrivateProfileIntW(L"Crosshair", L"Enabled", 1, path.c_str()) != 0;
    out.crosshairSize = ReadFloatKey(path, L"Crosshair", L"Size", 1.0f);
    if (out.crosshairSize < 0.5f) out.crosshairSize = 0.5f;
    if (out.crosshairSize > 2.0f) out.crosshairSize = 2.0f;
    out.crosshairOpacity = Clamp01(ReadFloatKey(path, L"Crosshair", L"Opacity", 1.0f));
    out.crosshairDynamic = GetPrivateProfileIntW(L"Crosshair", L"Dynamic", 0, path.c_str()) != 0;
    out.crosshairShow = GetPrivateProfileIntW(L"Crosshair", L"Show", 1, path.c_str()) != 0;
    out.crosshairStyle = GetPrivateProfileIntW(L"Crosshair", L"Style", 0, path.c_str());
    if (out.crosshairStyle < 0) out.crosshairStyle = 0;
    if (out.crosshairStyle > 2) out.crosshairStyle = 2;
    out.crosshairDebug = GetPrivateProfileIntW(L"Debug", L"Crosshair", 0, path.c_str()) != 0;
    out.logEffectiveFov = GetPrivateProfileIntW(L"Debug", L"LogEffectiveFov", 0, path.c_str()) != 0;
    out.dvarScanVerbose = GetPrivateProfileIntW(L"Debug", L"DvarScan", 0, path.c_str()) != 0;
    out.frameTimeDebug = GetPrivateProfileIntW(L"Debug", L"FrameTime", 0, path.c_str()) != 0;
    return true;
}

unsigned long long FileWriteTime(const std::wstring& path) {
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data)) return 0;
    return (static_cast<unsigned long long>(data.ftLastWriteTime.dwHighDateTime) << 32) |
           data.ftLastWriteTime.dwLowDateTime;
}
