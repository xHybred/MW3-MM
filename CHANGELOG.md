# Changelog

## 1.0.2

- Fixed hit registration at stock settings by skipping neutral weapon-state writes and removing the diagnostic bullet and RNG hooks from release builds.
- Stock settings no longer write weapon fields or open the runtime log from the shot path.

## 1.0.1

- Fixed the runtime Update button so it installs the newer runtime instead of removing it.
- ReShade now moves to `dxgi.dll` when dgVoodoo is installed, so the Direct3D 11 path works.
- Updates now require the release SHA-256 file and write the silent installer log to `update.log`.
- The manager refuses to overwrite another program's `X3DAudio1_7.dll` or `dxgi.dll`.
- The manager keeps the runtime's `[Debug]` settings when it saves.
- Removed leftover RenoDX/DLSS entries and the empty ReShade preset from the bundled files.
- Added the MIT license and third-party notices.
- Added release install steps, requirements, and troubleshooting to the README.
- The FOV slider now starts at 40.

## 1.0.0

This is the first release of MW3 Mod Manager, a quality-of-life mod manager for Call of Duty: Modern Warfare 3 (2011). It installs the mod runtime, tunes FOV, aim zoom, recoil, sway, flinch, blur, spread, viewmodel, and crosshair settings, and can add ReShade with bundled presets and shaders.

Future releases list changes as short bullet points.
