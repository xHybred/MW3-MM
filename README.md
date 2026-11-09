# MW3 FOV Mod

Field of view override for Call of Duty: Modern Warfare 3 (2011) single player, with an
option that makes aiming down sights widen along with your FOV.

Vanilla MW3 renders ADS at a fixed 40 degrees no matter what `cg_fov` is set to, so a 1x
sight feels over-zoomed as soon as you raise your hipfire FOV. This applies the same
two-dvar trick the newer titles use, so ADS scales with your setting, and it rewrites the
per-weapon aim zoom so a 1x sight zooms 1.1x instead of 1.63x. It works whether you start
the game from Steam, a shortcut, or a launcher.

## Install

Run `out\MW3ModSetup.exe`. It installs to
`C:\Program Files\Hybred Tools\MW3 Mod Manager`. The setup adds a Start Menu shortcut and
an uninstaller in Windows Settings. Launch **MW3 Mod Manager** from the Start Menu. It
finds the Steam install on its own (Browse if it does not). Open the **Setup** tab, press
**Install** in the Runtime row, then set your FOV on the **Aim** tab.

The manager only writes files and settings; it does not need to stay running and does not
need to launch the game. The game loads the mod on every start.

To run the manager without installing it, use `out\manager\MW3ModManager.exe` from a
build.

The setup only installs the manager. The mod itself is installed into the game from the
manager. Uninstalling the manager leaves the mod in the game and your settings in
`%LOCALAPPDATA%\MW3Mod`; remove the runtime in the manager first if you want a clean
uninstall.

Settings are grouped into tabs, and the preset list at the right of the tab bar applies a whole
configuration at once: **Default** puts every value back to stock, **Recommended** is a tuned
starting point, **Halo** adds the ring crosshair, and **Bodycam** is the pulled-in, perfect
hipfire look. Change anything afterwards and the list switches to `Custom`.

## Updates

The manager checks GitHub Releases when it starts. The switch in the Setup tab turns that
off; **Check now** asks immediately. When a newer version is published, the manager offers
**Install now** or **Later**. Install downloads the release setup, verifies its SHA-256
checksum and publisher metadata, closes the manager, runs the setup silently, and relaunches
it. The install folder and your `%LOCALAPPDATA%\MW3Mod` settings stay where they are.

The default repository is `xHybred/MW3-MM`. Set `MW3MOD_UPDATE_REPO` to `owner/name` to test
another repository. A release must publish `MW3ModSetup.exe` and `MW3ModSetup.exe.sha256`;
the build writes both to `out`. Release notes live in `CHANGELOG.md`; the first release
describes the tool, and later releases list changes as short bullets.

Manual install, if you prefer:

1. Copy `out\MW3Mod.dll` into the game folder as `X3DAudio1_7.dll`.
2. Write `%LOCALAPPDATA%\MW3Mod\settings.ini`:

```ini
[FOV]
Enabled=1
Affected=1
Value=90

[Aim]
Enabled=1
Flat=0
Zoom=1.1

[Flinch]
Enabled=1
Strength=1

[Blur]
Enabled=1
Strength=1

[Recoil]
Enabled=1
Strength=1

[Sway]
Enabled=1
Strength=1

[Flash]
Enabled=1

[Spread]
Enabled=1
Accuracy=0
AimAccuracy=0
Bloom=0

[Viewmodel]
Enabled=1
XOffset=0
ZOffset=0
YOffset=0

[Crosshair]
Enabled=1
Size=1
Opacity=1
Dynamic=0
Show=1
Style=0

[Startup]
SkipIntro=0
```

**Remove** in the manager (or deleting that one DLL) puts the game back to vanilla.

## Settings

`%LOCALAPPDATA%\MW3Mod\settings.ini`, re-read every 250 ms while the game runs, so edits
apply without a restart.

| Key | Meaning |
| --- | --- |
| `[FOV] Enabled` | 1 applies the override, 0 restores your own dvars |
| `[FOV] Affected` | 1 scales ADS with your FOV, 0 keeps the aiming FOV fixed |
| `[FOV] Value` | total hipfire FOV, clamped to 40..130 |
| `[Aim] Enabled` | 1 applies the aiming zoom, 0 leaves the weapon data alone |
| `[Aim] Zoom` | how much a 1x sight zooms, 1.0 = no zoom, 1.1 = default, 1.35 max |
| `[Aim] Flat` | 0 keeps scopes magnified, 1 gives every weapon the same zoom |
| `[Flinch] Enabled` | 1 applies the flinch scaling, 0 restores your own dvars |
| `[Flinch] Strength` | 0..1, how hard damage kicks the view. 1 = stock, 0 = no kick |
| `[Blur] Enabled` | 1 leaves the blur that lands with a hit, 0 removes it |
| `[Blur] Strength` | 0..1, how heavy that blur is. 1 = stock, 0 = the same as Enabled=0 |
| `[Recoil] Enabled` | 1 scales the gun's own kick, 0 leaves the weapon data alone |
| `[Recoil] Strength` | 0..1, how far the gun kicks when you fire. 1 = stock, 0 = no kick |
| `[Sway] Enabled` | 1 scales how far the gun drifts while aiming and moving, 0 leaves it untouched |
| `[Sway] Strength` | 0..1, how far the gun drifts. 1 = stock, 0 = no drift |
| `[Flash] Enabled` | 1 leaves the muzzle flash and its smoke puff, 0 removes both |
| `[Spread] Accuracy` | 0..1, how much of the hipfire cone is removed. 1 = every hipfire bullet through the crosshair |
| `[Spread] AimAccuracy` | 0..1, how much of the aimed cone is removed. 1 = every aimed bullet through the sight |
| `[Spread] Bloom` | 0..1, how much of the per-shot cone growth is removed. 1 = sustained fire stays as tight as the first shot |
| `[Viewmodel] Enabled` | 1 applies the three offsets below, 0 leaves the gun where the game puts it |
| `[Viewmodel] XOffset` | -1.5..3, pulls the gun in (negative) for the oversized side pose, pushes it out (positive) |
| `[Viewmodel] ZOffset` | -3..3, slides the gun left (negative) or right (positive) |
| `[Viewmodel] YOffset` | -3..3, slides the gun down (negative) or up (positive) |
| `[Crosshair] Enabled` | 1 applies the settings below, 0 restores your own dvars |
| `[Crosshair] Size` | 0.5..2.0, how large the crosshair is drawn. 1 = stock |
| `[Crosshair] Opacity` | 0..1, how solid the crosshair is. 1 = stock |
| `[Crosshair] Dynamic` | 1 lets the crosshair open up with your spread, 0 keeps it fixed |
| `[Crosshair] Show` | 0 hides the crosshair entirely |
| `[Crosshair] Style` | 0 draws the game's own crosshair, 1 the same bars drawn by the mod, 2 a Halo-style ring |
| `[Startup] SkipIntro` | 1 launches straight to the main menu, 0 plays the opening movie. The manager writes this into `players2\config.cfg` |
| `[Update] CheckOnLaunch` | 1 checks GitHub Releases when the manager starts, 0 leaves it to the Check now button |
| `[Debug] LogEffectiveFov` | log each weapon's aim FOV when it changes |
| `[Debug] DvarScan` | log every step of the dvar lookup |

Logs: `%LOCALAPPDATA%\MW3Mod\Log`

- `runtime.log` (UTF-16): the game-side trail. It records the runtime version, game exe, settings path, hook setup, and FOV changes.
- `manager.log` (UTF-8): the manager-side trail. It records the Windows and .NET versions, game folder, installed runtime and renderers, settings saves, install and remove actions, and full exception details.

`settings.ini` stays in `%LOCALAPPDATA%\MW3Mod`, next to the `Log` folder. Logs from earlier builds are kept in `Log\legacy`. Share both current logs when reporting a problem.

## How it works

The game loads `X3DAudio1_7.dll` from its own folder before the system copy, and it is
only asked for `X3DAudioInitialize` and `X3DAudioCalculate`. The mod is that DLL: it
forwards both calls to `C:\Windows\System32\x3daudio1_7.dll` and runs its own thread.

That thread waits for a level, finds the `cg_fov` and `cg_fovScale` dvars, and writes
them. Dvar lookup scans the module image for the name string, then for pointers to that
string, then checks the candidate against the known `dvar_t` layout (type byte at +0x0C,
value at +0x10, reset at +0x30). Addresses move between processes and levels, so nothing
is hardcoded.

The engine's effective FOV is `cg_fov * cg_fovScale` for hipfire, and `W * cg_fovScale`
for ADS, where `W` is the equipped weapon's `adsZoomFov`. So:

- **Affected** keeps `cg_fov` at 65 and sets `cg_fovScale` to `FOV / 65`. Hipfire is your
  FOV and ADS becomes `W * FOV / 65`, which keeps the weapon's zoom ratio exactly.
- **Independent** sets `cg_fov` to your FOV and `cg_fovScale` to 1. Only hipfire changes.

`W` is what sets the zoom, and vanilla iron sights have `W = 40` against a base of 65,
a 1.63x magnification. To make a 1x sight zoom less, the mod hooks `FUN_140022630` (the
function that returns `W`, attachment modifiers included) with MinHook and returns
`pivot / newZoom` instead:

- Iron sights land on `[Aim] Zoom`, 1.1 by default. Weapons the game gives little or
  no zoom to are grouped by how much they do have, so a revolver and a rifle with the
  same kind of sight no longer zoom differently: below 1.15x native they all land on
  1.06x, up to 1.45x they land on 1.08x.
- Scopes keep the extra magnification, scaled the same way:
  `zoom = 1 + (vanillaZoom - 1) * (wantedZoom - 1) / (1.625 - 1)`. A 4x stays near 1.5x.
- `[Aim] Flat` skips that and gives every weapon the wanted zoom, including the ones the
  game leaves at 1x.

Before the first write the current values are recorded, and disabling the mod puts them
back, so it does not matter what you had configured in game.

Damage flinch needs no hook. The engine scales a hit by `bg_viewKickScale`, clamps the
result between `bg_viewKickMin` and `bg_viewKickMax` degrees, and picks the direction with
`bg_viewKickRandom`. The mod captures all four and writes them scaled by `[Flinch] Strength`.
The minimum matters more than it looks: it is 5 degrees by default,
so lowering only the scale would leave a hard floor that still throws the view around.

The blur that covers the screen when a shot lands is not a dvar; nothing in the 1,560
registered settings touches it. The scripts call `setBlur`, which lands in the engine at
`iw5sp.exe+0x327A0` with a radius and a fade time, and that is what the runtime hooks. With
`[Blur] Enabled=0` every call goes through with a radius of zero, so the fade still runs and
the picture stays sharp. Because it is the same entry point the scripts use for any blur,
scripted moments lose their blur too.

Weapon recoil and hipfire spread are per weapon data, not dvars. The engine keeps two objects
per weapon: the `WeaponDef` the game hands the renderer, and a per-slot copy that gameplay reads
for spread and kick. They are often the same address, but not in every mission, so the runtime
writes both whenever they differ. It scales the four kick amplitudes (ADS and hipfire pitch and
yaw), the six hipfire spread limits, the aimed cone and the per-shot bloom. Accel, speed and decay stay
untouched, so the gun keeps its firing rhythm and only travels less far. Values are recorded per
object the first time it is seen, so switching or picking a weapon up never compounds the
scaling.

Weapon sway is the viewmodel's own idle drift. It is not a weapon field and not a dvar: the engine
builds three sine terms from a per-player counter and adds them straight onto the first-person view
angles. The runtime hooks that function (`iw5sp.exe+0x2DD50`), remembers what the angles were before
it ran, and scales only the difference it added. At 0 the gun is locked to the camera and the world
keeps moving normally; at 1 it is stock. Because only that function's contribution is scaled, the
aiming pose, the raise and the weapon animation are untouched.

The muzzle flash is an effect asset per weapon (`viewFlashEffect`, a pointer at +0x70 in the weapon
definition). Nothing in the game exposes it as a setting, so the runtime clears that pointer when the
toggle is off and puts it back when it is on. The smoke puff at the barrel comes from the same asset,
so both go together. A null effect pointer is safe; the engine simply spawns nothing.

The gun is placed relative to the camera, and `iw5sp.exe+0x6FD70` offsets it along the view's own
axes. The runtime adds its own offsets after the engine has built the position, so the gun moves while
the camera and the world stay put. X runs along the depth axis: negative pulls the gun in and makes it
larger, positive pushes it out and makes it smaller. Past about a metre of pull-in the root passes the
eye and the model swings out to the side, which is the wide, low pose the slider exists for. Z slides
the gun left and right, positive moving it right, Y moves it up. The ranges are -1.5..3 for X and
-3..3 for Y and Z. The offsets are applied on every frame, aiming included, and nothing is written
while all three are zero.

Hipfire spread comes from the weapon definition, not from a dvar. Each weapon stores a resting cone per
stance (`hipSpreadStandMin`, `hipSpreadDuckedMin`, `hipSpreadProneMin` and their max counterparts) and a
per-shot `hipSpreadFireAdd` that widens it while firing. The engine reads those floats every time it
works out the cone, so the runtime scales them in place: the Hipfire cone slider scales the six stance
values, and with the max at zero the cone cannot grow either; the Bloom slider scales only the
per-shot add, so the first shot keeps the weapon's own resting accuracy and later shots stop widening
it. Aiming has its own field too: `adsSpread` (a float at +0x764 in the weapon definition) is a single
fixed cone rather than one that grows with fire. That field is read in exactly one place, the query at
`iw5sp.exe+0x221F0` that returns the cone the game reports for the weapon in hand, so the Aim cone slider
scales that query's result instead of the stored float. That query is what the fire path asks for on an
aimed shot: measured on the ACR, the cone handed to the pellet randomiser is 0.120 deg at Aim cone 0 and
0.000 deg at Aim cone 100%, so the slider really does decide where an aimed bullet can go. The runtime
also logs that per-shot cone while `[Debug] Spread` is on, which is how the two values were compared.
Every reader of the field therefore sees the shrunk cone, on every weapon and scope.

The viewmodel cannot have its own field of view. A probe on the D3D9 device showed the game never
calls `SetTransform(D3DTS_PROJECTION)` and draws the first-person model with the same projection as
the world, so there is no separate matrix to widen.

The crosshair has no size setting. The engine asks one of two getters for the material and an
integer size in pixels every frame, and every caller of both lives in the crosshair drawing code.
FUN_140024B60 serves the dot and outline styles from the weapon asset; FUN_140026790 serves the
four-bar style single player actually shows and reads its size from the per-weapon state object.
The runtime hooks both and scales the size they hand back, which resizes the crosshair and nothing
else. The second getter can see the value the first one already scaled, so the runtime recognises
its own number and re-applies the scale once instead of compounding it.
Opacity, the dynamic mode and hiding the crosshair are plain dvars (`cg_crosshairAlpha`,
`cg_crosshairAlphaMin`, `cg_crosshairDynamic`, `cg_drawCrosshair`) that the runtime writes and
restores like the others.

The stock crosshair is also blurry at high resolution, and no setting fixes that: it is four
textured quads fed by an 8x8 texture (`side_small`) with linear filtering and no mip levels, so at
1440p the game stretches a 64 pixel bitmap over twenty screen pixels. `[Crosshair] Style` replaces
it. The runtime takes the D3D9 device from `Direct3DCreate9`/`CreateDevice` and, at the swap chain's
`Present`, draws the crosshair itself as geometry in screen pixels, using the colour the engine's
own drawer computed (so the red crosshair over an enemy still shows) and skipping the engine's
draw. Style 1 is the same four bars, slightly shorter and with a centre dot; style 2 is a ring with
four gaps and a centre dot. Both draw a dark outline first, grown one pixel on every side, so the
shape stays readable against a bright sky; the outline's alpha follows the crosshair's, so lowering
Opacity fades it with the bars. Both scale with `[Crosshair] Size` and follow
`cg_crosshairVerticalOffset`. A custom style does not open up with spread, so `[Crosshair] Dynamic`
only shows with style 0.

The opening movie is not a file the engine streams blindly: it is gated by the archived `intro` dvar,
which the game reads from `players2\config.cfg` before it draws a frame. `[Startup] SkipIntro` makes
the manager rewrite that one line to `seta intro "0"`, leaving the rest of the file byte for byte as it
was. The runtime cannot do this, because the engine resolves the dvar and starts the movie long before
the proxy's thread gets a look, so the toggle needs the manager and a closed game. The game rewrites
`config.cfg` when it exits, which is why the manager refuses to touch it while the game is running.

## Build

Needs Visual Studio 2022 Build Tools with the C++ workload, CMake, the .NET 10 SDK, and
Inno Setup 6.

```powershell
.\build.ps1 -Manager
```

Output: `out\MW3Mod.dll` and `out\manager\`. The runtime alone is `.\build.ps1`.

To build the installer, run:

```powershell
.\build-setup.ps1
```

It builds the runtime, publishes a self-contained manager (no .NET runtime needed on the
target machine), stages the ReShade assets, and writes `out\MW3ModSetup.exe` plus
`out\MW3ModSetup.exe.sha256`. The setup installs to
`C:\Program Files\Hybred Tools\MW3 Mod Manager` and adds an uninstaller. Upload both files
to the GitHub release so the in-app updater can verify the download.

## Notes and limits

- Tested against the x64 `iw5sp.exe` from the 2026-09-03 update (menu shows 2.0.15). The
  dvar lookup does not depend on addresses. The aiming-zoom hook does: it checks the
  first bytes of the function before hooking, and if a future update changes them the mod
  logs `ADS zoom: unexpected code` and keeps the dvar part working.
- `iw5sp.exe` on disk is a Steam DRM stub, so launching it directly exits with code 53.
  Start the game through Steam.
- `cg_*` dvars only exist once a level is running, so the mod idles on the frontend. That
  is expected.
- Multiplayer is not supported or tested. Do not use anything like this with Plutonium -
  its anti-cheat treats injected DLLs as cheating.
- The manager can also install DXVK for a Vulkan renderer, or dgVoodoo 2 for Direct3D 11,
  either by downloading the latest release from GitHub or from a `d3d9.dll` you already
  have. Both occupy `d3d9.dll`, so only one can be installed at a time; the proxy sits on
  `X3DAudio1_7.dll`, so the runtime does not collide with them. Neither is bundled.
- ReShade 6.8 ships with the manager together with its preset pack, shader library and
  `ShaderToggler` addon. It installs as `d3d9.dll` for native Direct3D 9, or as `dxgi.dll`
  next to dgVoodoo when the Direct3D 11 wrapper is active. The manager moves it
  automatically when the wrapper is added or removed, and keeps any `ReShade.ini`, preset
  or shader file already in the game folder. The overlay key is the one set in
  `ReShade.ini`. ReShade cannot share `d3d9.dll` with DXVK, so remove it before installing
  DXVK; running both
  needs ReShade's Vulkan install, which the manager does not set up.
- dgVoodoo 2 ships a configuration that reports 256 MB of video memory, and with that the
  game renders a mission but never starts it: the loading card stays on screen and the
  level's dvars never register. The manager raises that line to 1024 MB on install, which
  is what its own DirectX 9 preset uses, and missions load normally afterwards.
- dgVoodoo 2 also hides the game's Direct3D 9 device from this mod, so the custom crosshair
  styles and the frame-time counter do not work while it is installed. The game's own
  crosshair does.
- `tools\` holds the throwaway scripts used while reversing: `set_fov.py` writes the dvars
  in a running game from outside, `inspect.py` dumps candidate dvar structs, and
  `patch_weapon_fov.py` served as the proof that overriding the weapon ADS FOV is enough
  (a runtime patch of that function, now superseded by the hook).
