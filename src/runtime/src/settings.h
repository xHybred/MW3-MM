#pragma once

#include <string>

struct ModSettings {
    bool enabled = true;
    bool affected = true;
    float fov = 90.0f;
    bool aimEnabled = true;
    bool aimFlat = false;
    float aimZoom = 1.1f;
    bool flinchEnabled = true;
    float flinchStrength = 1.0f;
    bool blurEnabled = true;
    float blurStrength = 1.0f;
    bool blurDebug = false;
    bool recoilEnabled = true;
    float recoilStrength = 1.0f;
    bool recoilDebug = false;
    bool swayDebug = false;
    bool swayEnabled = true;
    float swayStrength = 1.0f;
    bool flashEnabled = true;
    bool viewmodelEnabled = true;
    float viewmodelZ = 0.0f;
    float viewmodelY = 0.0f;
    bool viewmodelDebug = false;
    bool logEffectiveFov = false;
    bool dvarScanVerbose = false;
};

std::wstring SettingsPath();
bool LoadSettings(ModSettings& out);
unsigned long long FileWriteTime(const std::wstring& path);
