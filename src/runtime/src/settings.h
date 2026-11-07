#pragma once

#include <string>

struct ModSettings {
    bool enabled = true;
    bool affected = true;
    float fov = 90.0f;
    bool aimEnabled = true;
    bool aimFlat = false;
    float aimZoom = 1.1f;
    bool logEffectiveFov = false;
    bool dvarScanVerbose = false;
};

std::wstring SettingsPath();
bool LoadSettings(ModSettings& out);
unsigned long long FileWriteTime(const std::wstring& path);
