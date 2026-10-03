# NIVR: a VR mod for *No, I'm not a Human*

NIVR adds full OpenXR VR support to the Steam game *No, I'm not a Human*: stereo rendering, head tracking, motion
controllers mapped onto the game's gamepad controls, a laser pointer for menus, comfort options, and a VR section in
the game's own Settings screen.

It is a [BepInEx 6](https://github.com/BepInEx/BepInEx) IL2CPP plugin. It contains no game code or assets; everything
it needs from the game is loaded from your own install at runtime.

> **Status: 0.2.1 preview.** The core VR experience has been played on a Quest (Virtual Desktop → SteamVR OpenXR).
> Some 0.2.0 polish features (comfort fades, haptics, focus/restart recovery) still need wider headset testing.
> Bug reports are welcome in [Issues](../../issues).

## Requirements

- *No, I'm not a Human* for Windows x64 on Steam, game version **1.3.19**
- [BepInEx 6 be.788, Unity.IL2CPP-win-x64](https://builds.bepinex.dev/projects/bepinex_be)
- A PC VR headset with an OpenXR runtime (tested with SteamVR; Quest via Virtual Desktop or Link)

## Install

1. Download `NIVR-<version>.zip` from the [Releases](../../releases) page.
2. Install BepInEx be.788 (IL2CPP x64) into the game folder and launch the game once so it generates its interop files.
3. Extract the NIVR zip into the game folder. It adds `BepInEx/plugins/NIVR/`.
4. Start SteamVR (set it as the active OpenXR runtime), then launch the game with `-force-d3d11`.

Full instructions, the controls table, every config option, known limitations and uninstall steps are in
[plugin/Core/README.md](plugin/Core/README.md), which also ships inside the release zip.

## Building from source

The build needs the interop assemblies BepInEx generates from **your own copy** of the game, so it only builds on a
Windows PC that owns the game. Those assemblies are never committed or shipped.

1. Make a working copy of the game at `C:\VRMod\game_copy` (or pass `-p:GameDir=<path>`), install BepInEx be.788 into
   it and launch it once.
2. Install the .NET 6 SDK or newer.
3. Build and deploy: `powershell -ExecutionPolicy Bypass -File plugin\Core\build.ps1`
4. Package a release zip: `powershell -ExecutionPolicy Bypass -File plugin\Core\package.ps1` writes
   `release\NIVR-<version>.zip` containing only the plugin DLL, the OpenXR loader, its license and the README.

`src/NIVR.DevTools` is a development-only plugin (logging, screenshots, scene dumps, save sandbox) and is never
included in releases. See [RELEASING.md](RELEASING.md) for how to publish a new version, and
[docs/DEV_NOTES.md](docs/DEV_NOTES.md) for the development notes on how the mod works.

## Legal

NIVR is an unofficial fan project and is not affiliated with or endorsed by the developers or publishers of
*No, I'm not a Human*. You need your own legitimate copy of the game. The mod's source code is released under the
[MIT License](LICENSE). The bundled Khronos OpenXR loader is distributed unmodified under the Apache-2.0 license
([plugin/Core/lib/LICENSE.openxr.txt](plugin/Core/lib/LICENSE.openxr.txt)).
