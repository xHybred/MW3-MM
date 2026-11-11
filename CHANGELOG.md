# Changelog

## 1.0.5

- Added a Co-op tuning card under Setup. It writes `r_vsync`, `cl_maxpackets`, `cl_packetdup` and a 60 or 120 FPS `com_maxfps` into `players2\config.cfg`.
- Added a Make read-only / Make writable button for `config.cfg`; applying settings unlocks it only while writing.

## 1.0.4

- Fixed the firewall helper failing during Fix connection. It now uses Windows' built-in `netsh` command and reports the exact failure if a rule cannot be added.

## 1.0.3

- Added a Network card under Setup with firewall rules, UPnP port mappings, a CGNAT check, and a copyable report.
- The report warns when the router is behind carrier-grade NAT and cannot be fixed from the local machine.

## 1.0.2

- Fixed hit registration at stock settings by skipping neutral weapon-state writes and removing the diagnostic bullet and RNG hooks from release builds.
- Stock settings no longer write weapon fields or open the runtime log from the shot path.
- Added an Always show option for the Sharp and Halo crosshairs. Bodycam now uses Sharp, and Halo and Bodycam enable the option.

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
