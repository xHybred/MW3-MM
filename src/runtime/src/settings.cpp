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
    out.logEffectiveFov = GetPrivateProfileIntW(L"Debug", L"LogEffectiveFov", 0, path.c_str()) != 0;
    out.dvarScanVerbose = GetPrivateProfileIntW(L"Debug", L"DvarScan", 0, path.c_str()) != 0;
    return true;
}

unsigned long long FileWriteTime(const std::wstring& path) {
    WIN32_FILE_ATTRIBUTE_DATA data{};
    if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data)) return 0;
    return (static_cast<unsigned long long>(data.ftLastWriteTime.dwHighDateTime) << 32) |
           data.ftLastWriteTime.dwLowDateTime;
}
