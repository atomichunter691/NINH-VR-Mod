# Task: polish pass for the "No, I'm not a Human" VR mod

You are continuing work on a VR mod for the Steam game "No, I'm not a Human" (Unity 6000.3.10f1, IL2CPP, URP,
BepInEx 6 be.788). The core VR mod works and has been tested in a Quest headset (Virtual Desktop -> SteamVR OpenXR).
Your job is a polish pass: comfort, feel, robustness and release readiness. The feature list below is prioritised;
do the Must items first, then as many Should items as you can. Don't start a Could item without asking the user first.

## Read first (mandatory)
1. `C:\VRMod\NOTES.md`, all of it, especially section 1.4 (known issues) and sections 2.x (VR core history, every
   gotcha found so far). Append your own findings as a new section 2.14+ when you finish.
2. The code in `C:\VRMod\plugin\Core\`. Main files:
   - `VRCoreBehaviour.cs`: the frame loop. It handles OpenXR, rig placement, eye rendering, the peephole, the input
     mode decision (`CurrentInputMode`) and the debug command channel (`RunDebugCommand`).
   - `UiPointer.cs`: the controller laser, the scheme switching, the pointer -> game cursor / EventSystem selection,
     and the hallway interaction ray.
   - `VirtualGamepad.cs`: the controllers as an Input System gamepad, including the radio-knob twist.
   - `FlatScreen.cs`: the screen-UI panel, the room box, the window dome and the cursor dot.
   - `UiCapture.cs`: overlay -> camera canvas conversion, UI/room render textures, and the Harmony patches
     (`OpenRoomView.Update` pixel frame, `Input.mousePosition`).
   - `Comfort.cs`, `GameState.cs`, `ButtonGate.cs`, `VRConfig.cs`, `VRRig.cs` (the public API), and `OpenXR\*` (the
     hand-written OpenXR bindings).
3. `C:\VRMod\recon\api_summary.txt`: the game's types, fields and method signatures. There are no method bodies;
   behaviour has to be found by experiment.

## Hard rules (from the project owner; never break these)
- Never modify the original game install. Work only on `C:\VRMod\game_copy\`.
- Don't copy or redistribute game assets or decompiled game code into the mod output. Anything the mod needs from
  the game is loaded from the user's install at runtime. Models, sprites and sounds the mod adds must be procedural
  or authored by you.
- The game copy shares the user's real Steam Cloud save. Only launch with `NIVR.DevTools` deployed and
  `SaveSandbox = true` in `game_copy\BepInEx\config\nivr.devtools.cfg`. Ask the user before ever turning it off.
- Stay inside `C:\VRMod\plugin\Core\`, or create `C:\VRMod\plugin\<NewModule>\` for clearly separable features.
  Other chats may work in `src\` and `scripts\`; don't edit those. The only shared file you write is NOTES.md
  (append only).
- Verify everything you can yourself (logs, eye captures, desktop screenshots). Ask the user to test in the headset
  only for what you can't verify (comfort, feel, scale), and keep each ask short and specific.
- Do the work yourself. Only delegate to sub-agents if the user explicitly asks.

## Build / run / test workflow
- Build and deploy the core: `powershell -ExecutionPolicy Bypass -File C:\VRMod\plugin\Core\build.ps1`
- Launch: `powershell -ExecutionPolicy Bypass -File C:\VRMod\scripts\run.ps1 -NoBuild -NoTail -Width 1280 -Height 720 -WaitReady 120`,
  then about 6 s later write `click Continue` to `C:\VRMod\logs\cmd\a.cmd` to enter gameplay (about 9 s).
- Simulator without a headset: set `Backend = Simulator` in `game_copy\BepInEx\config\nivr.core.cfg`. Set it back
  to `Auto` when you finish (the user's headset needs it).
- Command channel: write `*.vr` files (VR core) or `*.cmd` files (DevTools: `shot`, `dump`, `inspect`,
  `inspecttype`, `click`) into `C:\VRMod\logs\cmd\`. The full VR command list is in NOTES 2.11-2.13: `eyes <name>`,
  `game`, `status`, `simhead`, `simhand`, `aim`, `aimat`, `roll`, `trigger`, `btn`, `pad ... for <s>`, `tp`, `act`,
  `objects`, `inter`, `targets`, `closeup`, `ray`, `scheme`, `flat`, `vr off|on`.
  - Use timed inputs (`pad ... for 0.3`, `btn r secondary 0.3`). A press and its release written into separate files
    can be read in the same 0.25 s poll and be lost.
  - `ray ...` and other commands that set config values persist into the cfg file. Reset them.
  - `tp` into a spot without floor drops the player out of the map. Prefer walking with `pad`.
- Captures land in `C:\VRMod\logs\shots\`. Read the PNGs to check results. `*_submitL/R.png` are vertically flipped
  (that's the OpenXR submit format).
- The save is day 0, night: the radio, phone, Kitchen and Storage Room refuse to open (their hint is shown faded).
  That's game logic, not a bug. Bedroom, doors, windows, the peephole and the pause menu work.

## Current design in one paragraph
The rig uses the game camera as its base pose. While walking, the game's body and camera are steered to the head.
Eye cameras render the scene in stereo. The game's screen UI is captured to a render texture and shown on a panel.
Rooms appear as an enclosed box built from the illustration, windows as a curved dome behind the 3D window frame,
and the peephole as a world-fixed picture shown to one eye. Input modes:
- **Walk**: virtual gamepad; the laser is the interaction ray and appears only while the game offers an interaction.
- **Gamepad**: radio, peephole, window views.
- **Pointer**: menus, rooms, dialogs, phone, fridge. The game stays in its gamepad scheme so the prompts are A/B/X/Y.
  The laser drives the EventSystem selection, the game's `GamepadCursor` and a patched `Input.mousePosition`.

## Features
For every feature: add a config entry in `VRConfig.cs` (sensible default, clear description), verify in the
simulator where possible, and record any headset question for the final message.

### Must
1. **Comfort fade on camera jumps.** Scripted look-ats (doors, windows, peephole), teleports, scene loads and
   sudden base-pose rotations above a threshold should fade the view to black for about 0.15-0.3 s instead of
   snapping the world. Implement it as a full-screen fade in the eye render (a quad on the VR-only layer 29 or a blit
   pass). Also fade when the head moves inside geometry (a sphere check around the head against the Default layer)
   so leaning through walls doesn't show the void.
2. **Turning options.** Keep the existing snap turn (default 30°). Add smooth turn (configurable speed) and an
   optional comfort vignette during smooth turn and fast stick walking.
3. **Controller visuals.** The laser currently starts in thin air. Draw a minimal procedural controller or hand
   marker at each grip pose: a teal-tinted low-poly shape built in code, so no assets are needed. Laser origin and
   aim pose come from the aim transform (`VRRig.Controller(h).Aim`). Hide the markers in the peephole view.
4. **Haptics.** Add short ticks on: the laser landing on a new selectable or button, a click or submit, a door or
   room opening, and twist detents on the radio knob. Use `VRController.Haptic`. Verify the calls in the log; feel
   needs a headset test.
5. **Robustness and lifecycle.**
   - Handle OpenXR session state changes cleanly: when the session loses focus (headset off, SteamVR dashboard),
     pause the game the way its own pause does (gamepad Start / `PauseController` if reachable).
   - Survive a SteamVR restart: recreate the instance and session, and keep the game flat in the meantime.
   - Make the system/menu button on the right controller (if exposed) recenter on a long press.
   - Make sure nothing throws per frame after returning to the main menu or loading a new day (stale references
     like those fixed in `GameState`).
6. **Release packaging.**
   - Add `C:\VRMod\plugin\Core\package.ps1`. It builds Release and produces `C:\VRMod\release\NIVR-<version>.zip`
     containing only `BepInEx\plugins\NIVR\NIVR.Core.dll`, `openxr_loader.dll` and its Khronos Apache-2.0 license
     text, plus a README.
   - The zip must not contain DevTools, game files or the BepInEx core itself; link to BepInEx be.788 instead.
   - Gate dev-only commands (`closeup`, `tp`, `scheme`, `act`, `use`, `sim*`) behind `[Debug] CommandDir` being set
     (already the case) and make sure the release default has it empty.
   - README covers: install, launch with SteamVR / Virtual Desktop, controls table (VR button -> game action ->
     on-screen glyph; grip shows as RT/RB), config keys, uninstall (delete the folder), known limitations.

7. **VR section in the game's own Settings screen.** The pause/main-menu Settings screen is one scrolling page with
   sections ("Volume", "Text", "Controls"; see `Canvases/.../Settings/.../Viewport` in a DevTools `dump`). Add a
   "VR" section at the end of that page.
   - Build it at runtime by **cloning the game's own rows** (`Object.Instantiate` of an existing section header, a
     `VolumeSlider` row, the "Gamepad Vibration" checkbox row, and the Language dropdown if a choice list is needed),
     so it looks native. Cloning from the user's running game is fine; nothing is saved to disk or shipped.
   - Re-label clones with plain TMP text (remove or disable their `LocalizeStringEvent`), and rewire their
     `onValueChanged` listeners to the mod's config instead of the game's.
   - Settings to expose, each bound to its `VRConfig` entry, applied live and saved to the BepInEx cfg:
     - turning: snap angle / smooth turn + speed;
     - pointer hand (L/R);
     - seated height offset and a "Recenter now" button;
     - world scale;
     - room picture width (`RoomViewDegrees`) and room enclosure on/off;
     - window view width (`WindowViewDegrees`) and window dome on/off;
     - peephole size (`PeepholeDegrees`) and peephole eye;
     - HUD distance (`UiDistance`);
     - comfort fade on/off;
     - vibration strength.
   - Must work with **both input paths** the mod uses:
     - laser pointer (EventSystem selection, see `UiPointer.SelectUnderPointer`);
     - gamepad stick/d-pad navigation: set explicit `Navigation` up/down links and put the game's `SettingsMarker`
       on each new selectable, or the Watcher will refuse to keep the selection on them.
   - Left/right on a selected slider must change its value.
   - Find the Settings view type in `recon\api_summary.txt` and hook its show/init (Harmony postfix) so the section
     is built once per Settings instance, including after scene changes. If injecting into the page proves
     impossible, fall back to a "VR Settings" button in the Pause menu that opens a cloned settings page with only
     the VR rows. Document why in NOTES.
   - Verify in the simulator: open Settings from Pause, scroll to "VR", change a slider and a toggle with the laser
     + A and with stick navigation, then check the cfg file changed and the effect applied (eye capture).

### Should
8. **First-run controls card.** The first time VR becomes active, show a short controls overlay on the UI panel or a
   floating world panel: what each controller button does, and that grip is the game's RT/RB. Dismiss it with any
   button and remember that in config.
9. **UI legibility.** The UI render texture is window-sized, so a 1280x720 window gives blurry text in VR.
   - Investigate rendering the UI pass at a higher resolution without changing the canvas layout, e.g. a
     supersampled RT with the camera's `pixelRect` / `targetTexture` and the canvas scaler's reference left alone.
     The canvases must not re-layout; NOTES 2.x explains why the RT matches the window size.
   - If that's not safe, at least document "run the game window at 1920x1080 or higher" in the README and log a
     warning when the window is smaller.
10. **Walking HUD comfort.** Today the walking HUD panel is locked to the head. Add an option for a lazy-follow HUD
   (catches up when the head turns more than about 20°), and an option to lower the subtitles a little below the
   line of sight. Keep screen-projected markers aligned (see the OpenRoomView pixel-frame patch).
11. **Physical crouch.** When the real head drops more than a configurable amount below the recentered height,
    trigger the game's crouch (gamepad right-stick click or `PlayerController` crouch path); stand up when it rises
    again. Off by default.
12. **Performance pass.**
    - Measure the frame time with the in-sim `status` fps and the render-thread timing.
    - Remove per-frame allocations: the hover debug `StringBuilder` already only runs with `DebugHover`; check
      LINQ, closures and the `Resources.FindObjectsOfTypeAll` scans, all of which should stay throttled.
    - Skip the room-texture render when no room or window is open.
    - Add `[Rendering] RenderScale` presets to the README.

### Could (ask the user before starting)
13. Room illustrations with parallax: split the room's character and object images (children of `RoomDisplayer`
    room views) onto separate planes slightly in front of the background, so rooms get some depth. Hit-testing
    must keep working (the laser maps through the panel uv).
14. Peephole extras: a circular lens mask, and an option to show the picture to both eyes.
15. A quick-access VR menu (long press of Y) that opens the Settings page scrolled to the VR section.

## Done means
- Everything builds, the simulator checks pass, and eye captures show each visual feature.
- Config is back to `Backend = Auto`, the game is stopped, and `SaveSandbox` is still true.
- NOTES.md has a new section with what changed, what was verified and how, and what still needs the headset.
- Your final message to the user is short: what was done, what is unverified, and a numbered headset checklist of
  5-8 specific questions.
