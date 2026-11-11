#include <windows.h>
#include <cmath>
#include <string>

#include "adszoom.h"
#include "blur.h"
#include "crosshair.h"
#include "overlay.h"
#include "sway.h"
#include "viewmodel.h"
#include "weapon.h"
#include "spread.h"
#include "dvar.h"
#include "flinch.h"
#include "log.h"
#include "settings.h"

#define MW3MOD_VERSION "1.0.3"

namespace {

// Fallback pivot when the dvars cannot be read yet: cg_fov's default. The real
// pivot is captured from the live dvar, this only keeps the aiming-zoom math
// sane on the frontend.
constexpr float kPivotFov = 65.0f;
constexpr float kMaxFov = 130.0f;

// What the dvars held before we touched them, captured per level so disabling
// the mod puts the player's own settings back rather than the defaults.
struct Baseline {
    bool captured = false;
    float fov = kPivotFov;
    float scale = 1.0f;
};

bool IsSinglePlayer() {
    wchar_t path[MAX_PATH]{};
    if (GetModuleFileNameW(nullptr, path, MAX_PATH) == 0) return false;
    const wchar_t* name = wcsrchr(path, L'\\');
    name = name ? name + 1 : path;
    return _wcsicmp(name, L"iw5sp.exe") == 0;
}

void ApplySettings(const DvarRef& fovDvar, const DvarRef& scaleDvar, const ModSettings& settings,
                   const Baseline& baseline) {
    float target = settings.fov;
    if (target < 40.0f) target = 40.0f;
    if (target > kMaxFov) target = kMaxFov;

    const float pivot = baseline.fov;
    const float wantFov = settings.affected ? pivot : target;
    const float wantScale = settings.affected ? (target / pivot) : 1.0f;

    const float haveFov = ReadFloat(fovDvar);
    const float haveScale = ReadFloat(scaleDvar);

    if (fabsf(haveFov - wantFov) > 0.001f) {
        WriteFloat(fovDvar, wantFov);
        LogF(L"cg_fov %.3f -> %.3f", haveFov, wantFov);
    }
    if (fabsf(haveScale - wantScale) > 0.001f) {
        WriteFloat(scaleDvar, wantScale);
        LogF(L"cg_fovScale %.3f -> %.3f", haveScale, wantScale);
    }
}

void RestoreVanilla(const DvarRef& fovDvar, const DvarRef& scaleDvar, const Baseline& baseline) {
    if (fabsf(ReadFloat(fovDvar) - baseline.fov) > 0.001f) WriteFloat(fovDvar, baseline.fov);
    if (fabsf(ReadFloat(scaleDvar) - baseline.scale) > 0.001f) WriteFloat(scaleDvar, baseline.scale);
}

bool ResolveDvars(DvarRef& fovDvar, DvarRef& scaleDvar) {
    const ULONGLONG start = GetTickCount64();
    if (!FindDvar("cg_fov", fovDvar)) return false;
    if (!FindDvar("cg_fovScale", scaleDvar)) return false;
    LogF(L"cg_fov %p (+0x%zx), cg_fovScale %p (+0x%zx), scan took %llu ms",
         reinterpret_cast<void*>(fovDvar.addr), fovDvar.valueOffset,
         reinterpret_cast<void*>(scaleDvar.addr), scaleDvar.valueOffset,
         GetTickCount64() - start);
    return true;
}

DWORD WINAPI ModThread(LPVOID) {
    wchar_t exe[MAX_PATH]{};
    GetModuleFileNameW(nullptr, exe, _countof(exe));
    LogF(L"MW3Mod %S attached to %s (pid %lu)", MW3MOD_VERSION, exe, GetCurrentProcessId());
    LogF(L"installing hooks");
    adszoom::Install();
    blur::Install();
    sway::Install();
    viewmodel::Install();
    crosshair::Install();
    overlay::Install();
    spread::Install();
    LogF(L"hooks installed");

    DvarRef fovDvar;
    DvarRef scaleDvar;
    Baseline baseline;
    bool applied = false;
    bool resolved = false;
    bool flinchApplied = false;
    ULONGLONG nextResolve = 0;
    ULONGLONG nextFlinchResolve = 0;
    bool crosshairApplied = false;
    ULONGLONG nextCrosshairResolve = 0;
    bool loggedSettings = false;

    // cg_* dvars only exist once a level is running, so keep looking for the
    // whole session instead of giving up on the frontend.
    for (;;) {
        ModSettings settings;
        const bool loaded = LoadSettings(settings);
        if (!loaded) settings = ModSettings{};
        if (!loggedSettings) {
            LogF(L"settings %s: %s", loaded ? L"loaded" : L"missing", SettingsPath().c_str());
            LogF(L"settings fov=%.2f affected=%d aim=%d/%.2f flinch=%d/%.2f blur=%d/%.2f recoil=%d/%.2f sway=%d/%.2f spread=%d/%.2f/%.2f/%.2f viewmodel=%d/%.2f/%.2f/%.2f crosshair=%d/%.2f/%.2f/style=%d",
                 settings.fov, settings.affected ? 1 : 0,
                 settings.aimEnabled ? 1 : 0, settings.aimZoom,
                 settings.flinchEnabled ? 1 : 0, settings.flinchStrength,
                 settings.blurEnabled ? 1 : 0, settings.blurStrength,
                 settings.recoilEnabled ? 1 : 0, settings.recoilStrength,
                 settings.swayEnabled ? 1 : 0, settings.swayStrength,
                 settings.spreadEnabled ? 1 : 0, settings.spreadAccuracy,
                 settings.spreadAimAccuracy, settings.spreadBloom,
                 settings.viewmodelEnabled ? 1 : 0, settings.viewmodelX,
                 settings.viewmodelZ, settings.viewmodelY,
                 settings.crosshairEnabled ? 1 : 0, settings.crosshairSize,
                 settings.crosshairOpacity, settings.crosshairStyle);
            LogF(L"settings debug blur=%d recoil=%d sway=%d spread=%d viewmodel=%d crosshair=%d frame=%d",
                 settings.blurDebug ? 1 : 0, settings.recoilDebug ? 1 : 0,
                 settings.swayDebug ? 1 : 0, settings.spreadDebug ? 1 : 0,
                 settings.viewmodelDebug ? 1 : 0, settings.crosshairDebug ? 1 : 0,
                 settings.frameTimeDebug ? 1 : 0);
            loggedSettings = true;
        }
        g_dvarScanVerbose = settings.dvarScanVerbose;
        adszoom::Update(settings, baseline.captured ? baseline.fov : kPivotFov);
        blur::Update(settings);
        sway::Update(settings);
        viewmodel::Update(settings);
        crosshair::Update(settings);
        overlay::Update(settings);
        weapon::Update(settings);
        spread::Update(settings);

        // The flinch dvars exist from startup, so they resolve before a level
        // is anywhere near loaded and survive the level changes below.
        if (!flinch::Valid()) {
            const ULONGLONG now = GetTickCount64();
            if (now >= nextFlinchResolve) {
                nextFlinchResolve = now + 2000;
                flinch::Resolve();
            }
        }
        if (flinch::Valid()) {
            if (settings.flinchEnabled) {
                flinch::Apply(settings);
                flinchApplied = true;
            } else if (flinchApplied) {
                flinch::Restore();
                LogF(L"flinch restored");
                flinchApplied = false;
            }
        }

        // The crosshair dvars are cg_* and only appear with a level.
        if (!crosshair::Valid()) {
            const ULONGLONG now = GetTickCount64();
            if (now >= nextCrosshairResolve) {
                nextCrosshairResolve = now + 2000;
                crosshair::Resolve();
            }
        }
        if (crosshair::Valid()) {
            if (settings.crosshairEnabled) {
                crosshair::Apply(settings);
                crosshairApplied = true;
            } else if (crosshairApplied) {
                crosshair::Restore();
                LogF(L"crosshair restored");
                crosshairApplied = false;
            }
        }

        if (!StillValid(fovDvar, "cg_fov") || !StillValid(scaleDvar, "cg_fovScale")) {
            if (resolved) {
                LogF(L"dvars went away (level change?), waiting for the next one");
                resolved = false;
                applied = false;
            }
            fovDvar = DvarRef{};
            scaleDvar = DvarRef{};
            baseline = Baseline{};

            const ULONGLONG now = GetTickCount64();
            if (now >= nextResolve) {
                nextResolve = now + 2000;
                resolved = ResolveDvars(fovDvar, scaleDvar);
                if (resolved) {
                    baseline.fov = ReadFloat(fovDvar);
                    baseline.scale = ReadFloat(scaleDvar);
                    baseline.captured = true;
                    LogF(L"baseline cg_fov %.3f, cg_fovScale %.3f", baseline.fov, baseline.scale);
                }
            }

            if (!resolved) {
                Sleep(250);
                continue;
            }
        }

        if (settings.enabled) {
            ApplySettings(fovDvar, scaleDvar, settings, baseline);
            applied = true;
        } else if (applied) {
            RestoreVanilla(fovDvar, scaleDvar, baseline);
            LogF(L"mod disabled, restored cg_fov %.3f / cg_fovScale %.3f", baseline.fov, baseline.scale);
            applied = false;
        }

        Sleep(250);
    }
}

}  // namespace

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(instance);
        if (IsSinglePlayer()) {
            HANDLE thread = CreateThread(nullptr, 0, ModThread, nullptr, 0, nullptr);
            if (thread) CloseHandle(thread);
        }
    }
    return TRUE;
}

// Version marker: the manager greps the file for it to tell this proxy apart
// from the system X3DAudio1_7.dll it forwards to.
extern "C" __declspec(dllexport) const char* MW3ModVersion() {
    return "MW3Mod " MW3MOD_VERSION;
}
