# NIVR 0.2.0 — No, I'm not a Human VR mod

For the Windows x64 Steam game version 1.3.19 (Unity 6000.3.10f1). Tested with Quest through Virtual Desktop and SteamVR OpenXR for the original VR core; the 0.2.0 polish features have simulator verification and still need headset testing.

## Install and launch

1. Install [BepInEx 6 be.788, Unity.IL2CPP-win-x64](https://builds.bepinex.dev/projects/bepinex_be). Select `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip`, not the Mono or x86 build. Follow the [BepInEx IL2CPP installation instructions](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html). Run the game once to generate its interop assemblies.
2. Extract this archive into your own game folder; it adds `BepInEx/plugins/NIVR/NIVR.Core.dll`, `openxr_loader.dll` and its license. BepInEx and the game are separate downloads. No game artwork or code is included.
3. Start Steam and SteamVR. Set SteamVR as the active OpenXR runtime. On Quest, connect Virtual Desktop to the PC and start SteamVR there, then start the game with `-force-d3d11`. VR activates when the headset session is ready; the game remains flat while no headset is available.
4. Run the game window at **1920×1080 or higher** for legible UI. The UI capture deliberately matches the window to preserve the game's layout and hit testing.
5. Recenter while sitting comfortably. Open the game's Settings screen and scroll to the **VR** section to adjust comfort, scale and presentation. Changes apply immediately and save to `BepInEx/config/nivr.core.cfg`.

## Controls (default Gamepad pointer scheme)

| VR control | Game action | On-screen glyph |
|---|---|---|
| Left stick | Walk; menu navigation; left/right changes sliders | Left stick / d-pad |
| Right stick left/right | Snap turn 30° while walking; cursor or radio knob in views | Right stick |
| A / pointing-hand trigger | Interact, select, submit; hold and aim along sliders | A |
| B | Back, exit room, cancel | B |
| X / other-hand trigger | Skip dialog | X |
| Y | Tutorial / hint | Y |
| Right grip | Run, speed up text, radio handle | **RT and RB** |
| Left grip | Other radio handle | **LT and LB** |
| Left Menu | Pause / close pause | Start / Menu |
| Right stick click | Game crouch (where allowed) | Right stick click |
| Hold both stick clicks for 0.5 s | Recenter | — |
| Hold right Menu for 1 s, if exposed | Recenter | — |
| F8 on keyboard / VR Settings “Recenter now” | Recenter | — |

The pointing hand starts on the right; pull the other trigger to switch. Trigger + controller twist tunes the radio. Quest's right system button is reserved by the runtime and is usually unavailable to the mod. Use both stick clicks or Settings instead.

## Config

The VR section includes turning, pointer hand, seated height, recenter, world scale, room/window widths and enclosure/dome switches, peephole size/eye, HUD distance, comfort fade and vibration. Optional vignette, lazy HUD, subtitle lowering and physical crouch are there too. “Smooth turning” unchecked selects Snap; `TurnMode = Off` is available in the config.

| Section | Keys and defaults |
|---|---|
| General | `Enabled=true`, `Backend=Auto`, `VrSettings=true`, `PauseOnFocusLoss=true`, `ShowControlsCard=true`, `ControlsCardSeen=false` |
| Tracking | `Mode=Seated`, `SeatedHeightOffset=0`, `StandingEyeHeight=1.65`, `WorldScale=1`, `IpdOverrideMm=0`, `RigRotation=YawOnly`, `BodyFollowsHead=true` |
| Tracking | `TurnMode=Snap`, `SnapTurnDegrees=30`, `SmoothTurnSpeed=60` degrees/s; `RecenterKey=F8`, `RecenterWithThumbsticks=true`, `RecenterWithRightMenu=true`, `RightMenuHoldSeconds=1` |
| Tracking | `PhysicalCrouch=false`, `CrouchDrop=0.35` m. Respects game crouch zones; stands 8 cm above the trigger threshold. |
| Input | `ControllerAsGamepad=true`, `PointerScheme=Gamepad`, `PointerHand=Right`, `InteractionRay=Controller`, `MoveSystemCursor=true` (unused while legacy mouse patch is active), `VibrationStrength=1` (0 disables), `RadioDetentDegrees=12` |
| Rendering | `RenderScale=1`, `MSAA=4`, `NearClip=0.05`, `FarClip=0` (game value), `FlipY=true`, `Mirror=GameCamera`, `ControllerVisuals=true`, `WarnLowUiResolution=true` |
| Rendering | `FlatScreen=Auto`, `UiDistance=1.5`, `UiWidth=2.1`, `RoomDistance=3`, `RoomEnclosure=true`, `RoomViewDegrees=105`, `WindowDistance=10`, `WindowDome=true`, `WindowViewDegrees=110`, `PeepholeEye=Right`, `PeepholeDegrees=60` |
| Rendering | `LazyFollowHud=false`, `HudFollowAngle=20`, `SubtitleOffset=0` canvas units (positive lowers the caption panel, clamped to keep it on screen) |
| Comfort | `ComfortFade=true`, `FadeSeconds=0.22`, `JumpDistance=0.45` m/frame, `JumpAngle=35` degrees/frame, `HeadCollisionFade=true`, `HeadCollisionRadius=0.08` m, `MotionVignette=false` |
| Comfort | `DisableMouseLook`, `DisableHeadBob`, `DisableCameraShake`, `DisableVignette`, `DisableLensDistortion`, `DisableDepthOfField`, `DisableMotionBlur`, `DisableChromaticAberration` default true; `DisableFilmGrain=false` |
| Debug | `CommandDir=` (empty disables all command files), `Verbose=false`. Development simulator and object/teleport commands are opt-in only. |

For performance try `RenderScale=0.75` (56% of the default eye pixels), `1.0` (balanced), or `1.25` (156%, sharper world). Eye resolution and MSAA changes require restarting the game. Increasing RenderScale does not increase UI capture resolution. `Backend=Simulator` is for development only; use Auto for a headset.

## Known limitations

- Rooms and window landscapes are the game's flat artwork displayed on an enclosure/dome. Peephole uses one eye. Controller markers are procedural shapes; there are no tracked hands or physical object grabbing.
- UI sharpness follows desktop resolution; supersampling the UI target independently changes Unity canvas layout on this build. Lazy HUD is optional; the interaction marker is reprojected to keep its world alignment.
- Comfort and vibration feel, runtime focus pause and recovery after a SteamVR restart need headset verification. Losing focus opens the game's own pause menu; resume manually on return. A lost runtime falls back to flat while it reconnects.
- Game rules still gate interactions and crouching; faded hints (including day-0 radio/phone and some rooms) are intentional.
- Physics remains at the game's own fixed update rate. Dream scenes, later days and every localization have not been exhaustively tested.
- Other mods that change the game camera, input scheme or UI may conflict. Game updates can require regenerating BepInEx interop and rebuilding NIVR.

## Uninstall

Delete `BepInEx/plugins/NIVR/` if it contains only this release. If other plugins share the folder, remove only `NIVR.Core.dll`, `openxr_loader.dll` and `LICENSE.openxr.txt`. Optionally delete `BepInEx/config/nivr.core.cfg`.

The unmodified Khronos OpenXR loader is distributed under [Apache-2.0](https://github.com/KhronosGroup/OpenXR-SDK-Source/blob/main/LICENSES/Apache-2.0.txt); the full license accompanies it. [Upstream OpenXR SDK source](https://github.com/KhronosGroup/OpenXR-SDK-Source). This package contains no BepInEx core, DevTools, game files, game assets, save data or generated interop assemblies.
