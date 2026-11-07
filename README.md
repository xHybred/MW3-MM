# MW3 FOV Mod

Field of view override for Call of Duty: Modern Warfare 3 (2011) single player, with an
option that makes aiming down sights widen along with your FOV.

Vanilla MW3 renders ADS at a fixed 40 degrees no matter what `cg_fov` is set to, so a 1x
sight feels over-zoomed as soon as you raise your hipfire FOV. This applies the same
two-dvar trick the newer titles use, so ADS scales with your setting. It works whether
you start the game from Steam, a shortcut, or a launcher.

## Install

Run `out\manager\MW3ModManager.exe`. It finds the Steam install on its own (Browse if it
does not), then press **Install** in the FOV runtime row and pick a FOV.

The manager only writes files and settings; it does not need to stay running and does not
need to launch the game. The game loads the mod on every start.

Manual install, if you prefer:

1. Copy `out\MW3Mod.dll` into the game folder as `X3DAudio1_7.dll`.
2. Write `%LOCALAPPDATA%\MW3Mod\settings.ini`:

```ini
[FOV]
Enabled=1
Affected=1
Value=90
```

**Remove** in the manager (or deleting that one DLL) puts the game back to vanilla.

## Settings

`%LOCALAPPDATA%\MW3Mod\settings.ini`, re-read every 250 ms while the game runs, so edits
apply without a restart.

| Key | Meaning |
| --- | --- |
| `[FOV] Enabled` | 1 applies the override, 0 restores your own dvars |
| `[FOV] Affected` | 1 scales ADS with your FOV, 0 keeps the default ADS zoom |
| `[FOV] Value` | total hipfire FOV, clamped to 40..160 |
| `[Debug] LogEffectiveFov` | log the FOV the renderer actually gets, twice a second |
| `[Debug] DvarScan` | log every step of the dvar lookup |

Log: `%LOCALAPPDATA%\MW3Mod\runtime.log` (UTF-16).

## How it works

The game loads `X3DAudio1_7.dll` from its own folder before the system copy, and it is
only asked for `X3DAudioInitialize` and `X3DAudioCalculate`. The mod is that DLL: it
forwards both calls to `C:\Windows\System32\x3daudio1_7.dll` and runs its own thread.

That thread waits for a level, finds the `cg_fov` and `cg_fovScale` dvars, and writes
them. Dvar lookup scans the module image for the name string, then for pointers to that
string, then checks the candidate against the known `dvar_t` layout (type byte at +0x0C,
value at +0x10, reset at +0x30). Addresses move between processes and levels, so nothing
is hardcoded.

The engine's effective FOV is `cg_fov * cg_fovScale` for hipfire, and `40 * cg_fovScale`
for ADS. So:

- **Affected** keeps `cg_fov` at 65 and sets `cg_fovScale` to `FOV / 65`. Hipfire is your
  FOV, ADS becomes `40 * FOV / 65`.
- **Independent** sets `cg_fov` to your FOV and `cg_fovScale` to 1. Only hipfire changes.

Before the first write the current values are recorded, and disabling the mod puts them
back, so it does not matter what you had configured in game.

## Build

Needs Visual Studio 2022 Build Tools with the C++ workload, CMake, and the .NET 10 SDK.

```powershell
.\build.ps1 -Manager
```

Output: `out\MW3Mod.dll` and `out\manager\`. The runtime alone is `.\build.ps1`.

## Notes and limits

- Tested against the x64 `iw5sp.exe` from the 2026-09-03 update (menu shows 2.0.15). The
  dvar lookup does not depend on addresses, but nothing else here has been checked
  against the 32-bit builds.
- `iw5sp.exe` on disk is a Steam DRM stub, so launching it directly exits with code 53.
  Start the game through Steam.
- `cg_*` dvars only exist once a level is running, so the mod idles on the frontend. That
  is expected.
- Multiplayer is not supported or tested. Do not use anything like this with Plutonium -
  its anti-cheat treats injected DLLs as cheating.
- The manager can also drop in a DXVK `d3d9.dll` for a Vulkan renderer. The proxy sits on
  `X3DAudio1_7.dll`, so the two do not collide. DXVK is not included.
- `tools\` holds the throwaway scripts used while reversing: `set_fov.py` writes the dvars
  in a running game from outside, `inspect.py` dumps candidate dvar structs.
