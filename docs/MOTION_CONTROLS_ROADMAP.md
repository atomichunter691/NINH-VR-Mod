# NIVR motion controls: audit and roadmap

Written 2026-10-10 against NIVR 0.2.1 (`main` at `ef398a4`). This is a planning document. Nothing in it has been built or
run: the plugin only compiles against interop assemblies generated from a local copy of the game, so the audit below is
a read of the source and `docs/DEV_NOTES.md`, not a test run. Anything that depends on how the game behaves at runtime is
marked **(inferred)** and has to be confirmed on the PC with the headset or the simulator.

"Motion controls for everything" here means: every in-game interaction can be done with the body (reaching, grabbing,
poking, twisting, leaning, raising a hand) instead of only with a laser plus buttons, while the current button scheme keeps
working as a fallback.

---

## 1. Where the mod is today

### 1.1 How the controllers reach the game

The mod's design principle is "the controllers are a gamepad". Almost nothing in the game is called directly; it is
driven through the game's own input layer:

| Piece | What it does | Where |
|---|---|---|
| OpenXR input | One action set; trigger, squeeze, thumbstick, stick click, A/X, B/Y, menu, grip + aim poses, one haptic output | `plugin/Core/OpenXR/XrInput.cs` |
| Virtual gamepad | Hand-built Input System `Gamepad` state events: sticks, A/B/X/Y, Start, shoulders, triggers | `plugin/Core/VirtualGamepad.cs` |
| Laser pointer | Ray from the aim pose hits the UI panel; drives the game's gamepad cursor and the selected button | `plugin/Core/UiPointer.cs` |
| Hallway interaction ray | A 0.1 degree camera parented to the aim pose replaces `RaycastSource._camera`, reach is the game's 1 m plus 0.4 m | `UiPointer.UpdateRayCamera` |
| Direct calls | Where the gamepad route failed, the mod calls the game: menu `OnSubmit`, `TryLeave()`, `PauseController.SwitchPause()`, `Character.Crouch()` | `UiPointer.PrePress`, `PauseGuard.Exit`, `VRCoreBehaviour.HandlePhysicalCrouch` |
| Input modes | Walk (hallway), Gamepad (radio, window, peephole, movie, dream), Pointer (rooms, dialogs, phone, fridge, menus), picked from the game's `WatcherManager` state | `VRCoreBehaviour.CurrentInputMode` |

Current motion-based behaviour is small: head tracking, the controller aim pose as pointer, trigger-held controller twist
for the radio knob (`VirtualGamepad.Tick`, gain 3), optional physical crouch (off by default), and haptic ticks.
Everything else is a button press.

### 1.2 What exists vs. what motion controls need

| Need | Today |
|---|---|
| Hand models | A teal cuboid at the grip pose, eye-only, hidden in the peephole (`ControllerVisuals.cs`). The notes say hand models are "deliberately not started" (DEV_NOTES 2.8). |
| Hand velocity / acceleration | Not read. `xrLocateSpace` is called without velocity (`XrSession.LocateSpace`), so throws, strokes, swings and "knock" cannot be detected yet. |
| Finger / touch state | Not bound (no capacitive touch, no thumbrest, no hand tracking). Only `XR_KHR_D3D11_enable` is enabled (`XrSession.cs:71`). |
| Physical-contact interaction | None. The only "reach" is a ray. |
| Two-hand logic | Only "both stick clicks recenter". |
| Handedness | Fixed: A/B/Menu mapping assumes right = A/B, left = X/Y/Menu. No one-handed or left-handed remap. |
| Gesture layer | None. There is no place to put code that turns poses over time into "the player did X". |
| Tests | None, and no CI (`RELEASING.md` explains why the plugin itself cannot build in CI). |

---

## 2. Audit findings

Ranked by how much they matter for motion controls. File references are to this repo.

### 2.1 Things that block or shape the roadmap

1. **No gesture / intent layer.** The gamepad abstraction is a ceiling: a gesture would have to be faked as a button
   press, and several screens already proved that fails (menu A-presses were unreliable, so `UiPointer.PrePress` and
   `PauseGuard.Exit` call the game directly). Motion controls should follow the same pattern deliberately: a gesture
   recognizer produces an *intent* ("open this door", "drink this"), and a small dispatcher calls the game method
   (`Interact()`, `Act()`, `UIButton.Click()`, `TryLeave()`), with the gamepad press as the fallback.
2. **No hand kinematics.** Velocity and angular velocity are needed for throwing the postcard (`ThrowPovistka`), stroking
   the cat, knocking, tearing a calendar page, and rejecting accidental triggers. Add `XR_SPACE_VELOCITY` flags to the
   controller `xrLocateSpace` calls and keep a short smoothed history per hand.
3. **No hand presence.** Reach-and-touch interactions are unreadable with a floating teal cuboid. At minimum the grip
   pose needs a hand or controller model and a defined fingertip / palm point (poke point, grab sphere).
4. **Interaction targets are fixed to the game's own colliders.** The hallway uses `ARaycastTarget` boxes sized for a
   gaze ray ("doors ~1 x 2.3 m, radio 0.3 x 0.43 m", DEV_NOTES 2.11) and a 1 m reach from the eye. Hand reaching needs
   the targets' bounds and a way to ask "which target is this hand inside / near", ideally without editing game objects.
5. **Many game behaviours are canned 2D animations.** Drinking, smoking, eating, opening a tin, taking a photo, throwing
   a postcard and handling the cat are full-screen 1920x1080 sprite sequences (`EHUDAnimation`, DEV_NOTES 1.11 A) played
   on a camera-space canvas; the shotgun is a HUD sprite (`Gun`). They are triggered by `Interact()`. A gesture can
   trigger them, but the picture they produce is flat and head-locked on the UI panel unless the mod suppresses or
   replaces it. This is the largest design decision in the roadmap (see Phase 4).
6. **Method bodies are unknown.** The interop decompile has signatures only (DEV_NOTES 1.4 item 5); the notes list
   Cpp2IL pseudo-code on `GameAssembly.dll` as the next step and nobody has done it. Gesture design for items, the cat
   and the gun depends on knowing whether `Interact()` is a press, a hold, or a toggle. This is Phase 0 work and needs
   the PC.
7. **Input-device coverage is thin.** `XrInput.Create` suggests bindings for Touch, Index, Vive, WMR and the Khronos
   simple profile only. Consequences **(inferred from the binding tables, not tested)**: Index has no menu binding
   (the system button is not bindable), so left Menu = pause does not exist there; Vive and the simple profile have no
   A/B/X/Y; WMR has no B. HP Reverb G2, Pico and Meta's newer Touch profiles rely on runtime fallback mapping. Motion
   controls multiply this problem (grip + trigger + stick become gesture modifiers), so the roadmap needs an
   "alternative gesture for system actions" for devices without a menu button.
8. **Handedness and one-handed play.** Actions are hard-wired to specific hands. A motion-control design that needs
   "pick up with the left, use with the right" must also have a one-handed / swapped mode, or it will lock players out.

### 2.2 Code health

9. **`VRCoreBehaviour.cs` is the integration point for everything** (1286 lines: OpenXR frame loop, rig maths, peephole,
   debug command parser, `Update` orchestrating about fifteen subsystems in a fixed order). New gesture code must not be
   bolted into it. Add an explicit tick order and a registration point for feature modules before adding more.
10. **Mutable statics as cross-module API** (`UiPointer.TriggerSouth/TriggerWest/SwallowA`, `PauseGuard.SwallowEast`,
    `VirtualGamepad.RightStickValue`, `GameState`). They work, but each new feature that needs to suppress a button
    adds another flag. A single per-frame "input arbitration" object (who owns which button this frame) would replace
    them and is a prerequisite for gestures that consume buttons (grip to grab must not also be run).
11. **Stale comments and docs.**
    - `VRCoreBehaviour.cs:17-26` still says the game camera is reused for the eye renders; it is a per-frame copy
      (`_eyeCam.CopyFrom`) since the first pass.
    - `VirtualGamepad.cs` stacks two `<summary>` blocks on the `VRTriggerRole` enum; the class has none.
    - `docs/DEV_NOTES.md` section 1.12 says "No VR code written", 2.2 describes the desktop-capture flat screen that
      2.8 replaced, and 2.7 says the peephole camera is "inferred to be broken" although 2.8 and 2.11 fixed it. The
      notes are a chronological log, which is useful, but a reader needs a short "current architecture" page.
    - The notes refer to files that are not in the repo (`C:\VRMod\NOTES.md`, `POLISH_PROMPT.md`, `recon\`,
      `logs\shots\...`). The recon output (class and field lists) exists only on the dev PC, so any cloud-side planning
      depends on the notes alone.
12. **Dead / duplicate code.** `src/NIVR.Plugin` is a "hello world" at version 0.1.0 that the notes call unowned and
    unused. The version is written in three places (`CorePlugin.Version`, `NIVR.Core.csproj`, README heading) and has to
    be bumped by hand.
13. **Hard-coded `C:\VRMod\...` paths** in `Directory.Build.props`, scripts and notes. `README.md` documents
    `-p:GameDir=`, but nothing else in the docs does.
14. **No automated checks at all.** The plugin cannot build without game-derived files, so CI cannot compile it. The
    way around that is to keep new pure logic (gesture recognition, pose filtering, zone maths) in a separate project
    with no Unity or game references, which CI can build and test. Today no such project exists.

### 2.3 Things that are fine

- Release hygiene is good: the zip is allow-listed to four files, DevTools and game data stay out, `.gitignore` covers
  the local folders, MIT plus Apache-2.0 notices are in place.
- Debug commands are gated behind a non-empty `[Debug] CommandDir` and a simulator exists, which is exactly what
  gesture development needs (`simhand`, `aim`, `roll`, `trigger`, `btn`).
- Failure handling is conservative: per-frame work is wrapped, noisy failures are throttled (`CorePlugin.LogThrottled`),
  and `Resources.FindObjectsOfTypeAll` scans are throttled to once a second where they run per frame.
- The peephole is a deliberate one-eye design (config `PeepholeEye`); this roadmap keeps it.

---

## 3. Design principles for the motion layer

1. **Additive, never exclusive.** Every gesture has a button/laser equivalent that keeps working. A Settings toggle per
   family (reach, items, two-hand, head gestures) in the existing VR section.
2. **Recognizer, then dispatcher.** Recognizers are pure functions of poses over time and know nothing about the game.
   A dispatcher maps an intent to a game call, with the gamepad press as a fallback path.
3. **Gate by game rules.** Doors, rooms, phone and radio refuse to open at certain times (day-0 gating, energy cost).
   A gesture must go through the same `HardConditions` / `SoftConditions` the button press does and show the same
   faded hint, not bypass them.
4. **Confirm costly or scripted actions.** Opening a door, parting blinds or drinking triggers scripted camera moves and
   spends energy. Require an intent-confirming motion (push, pull, bring-to-mouth) rather than a brush of a hand.
5. **Every contact has haptics; every mode has a way out.** Hover tick, contact pulse, success pulse (extends
   `Haptics.Tick`). B / leave-by-leaning-back remain available everywhere (`PauseGuard.Exit` is the pattern).
6. **Log every dispatched intent** the way `VR UI click -> ...` is logged, so headset sessions can be debugged from
   `nivr.log`.
7. **Comfort first.** No new virtual camera motion. Locomotion options (arm swing, room-scale nudge) are optional and
   default off.

---

## 4. Interaction inventory

Every interaction found in `docs/DEV_NOTES.md` sections 1.6 to 1.11, with today's control and the motion-control target.
"Phase" refers to section 5. All game-side classes are from the notes; behaviour marked **(inferred)** must be confirmed.

### 4.1 Hallway (`s1_GameScene`, 3D)

| Interaction | Game hook | Today | Motion target | Phase |
|---|---|---|---|---|
| Walk | `InputHandling.MoveInput` via left stick | Left stick | Keep stick. Optional arm swing; optional room-scale nudge | 1, 7 |
| Turn | `AddControlYawInput`, body follows head | Right stick snap/smooth | Keep. Physical turning already works; add optional grab-and-pull turn | 7 |
| Crouch | `Character.Crouch/UnCrouch` where `canEverCrouch` | Stick click, optional head-drop | Keep head-drop; positive case still untested | 0 test |
| Run | `SetRunAvailability`, RT | Right grip | Keep; arm-swing speed as an option | 7 |
| Doors (6) | `DoorTrigger.Act()` via `ARaycastTarget` | Laser + A / trigger | Reach to the door, push or pull past a distance | 1 |
| Peephole | `PeepholeTrigger.Act()`, `TryLeave()` | Laser + trigger; B leaves | Lean the eye to the hole to look, lean back to leave | 1 |
| Window blinds (2) | `WindowBlindsTrigger` | Laser + trigger | Grab the blind and pull down / sideways; lean back to leave | 1 |
| Curtains window | `WindowCurtainsTrigger` | Laser + trigger | Grab and sweep sideways | 1 |
| Phone (hallway) | `PhoneInteractable.Interact()` -> close-up | Laser + A | Reach, lift handset (grip) | 1, 3 |
| Radio (hallway) | `RadioInteractable.Interact()` -> close-up | Laser + A | Reach to radio | 1, 3 |
| Calendar (world canvas) | `CalendarInteractable` | Laser + A | Poke / swipe on the page **(inferred: world-space canvas)** | 1 |
| Save point | `SaveInteractable` | Laser + A | Reach and press | 1 |
| Cat | `CatInteractable`; animations Take/Hold/ReleaseNaperdysh | Laser + A | Stroke to pet; grip to pick up, release to put down **(inferred: hold semantics)** | 1, 4 |
| Cigarette | `CigaretteInteractable`; `SmokeCigarette` | Laser + A | Take, bring to mouth | 4 |
| Mushroom, hatch, hole, window boards, zoom, ending launch | `MushroomInteractable`, `HatchInteractable`, `TheHoleInteractable`, `WindowBoardsInteractable`, `ZoomInteractable`, `EndingLaunchInteractable` | Laser + A | Generic reach-and-grab; specific gestures decided after recon | 0, 1 |
| Zone triggers (go to location, open door, run, crouch, close scene) | `ATriggerObject` subclasses | Walk | None needed | - |

### 4.2 2D rooms behind doors (Kitchen, Office, Bedroom, BigRoom, Bathroom, Pantry, Entrance)

| Interaction | Game hook | Today | Motion target | Phase |
|---|---|---|---|---|
| Click characters / objects | `UIButton.Click()`, hover via `IsMousePosInSpriteArea` | Laser on the room wall + trigger | Poke the picture with a fingertip (hover on proximity, click on contact), laser stays | 2 |
| Leave room | cursor / B | B | Step or lean back; B stays | 2 |
| Bedroom TV video | `VideoPlayer` | Passive | None | - |

### 4.3 Dialogs and inspection

| Interaction | Game hook | Today | Motion target | Phase |
|---|---|---|---|---|
| Choose an answer | `FakeOptionView` (Selectable) | Laser + trigger | Poke; optional head nod / shake for two-answer questions | 2 |
| Skip a line | `DialogView.SkipLine`, X | X or trigger | Forward flick of a hand; X stays | 2 |
| Visitor signs (eyes, hands, teeth, ear, armpit, photo) | `ShowSign(CharacterSOData, ECharacterSign)` | Passive display | Lean in to examine; hand-held photo to eye level | 2 |
| "Show me your hands" (player's hands) | `PlayerSigns.IsHandsFake`, `_handsSpriteHuman/Imposter` | Passive display | Raise both hands, palms out, in front of the chest to give the answer. **Needs recon: is the show-hands moment a player choice or a scripted beat?** | 2 |
| Peephole visitor conversation | Dialog over `PeepholeCam` | Laser on the peephole picture | Same as above | 2 |

### 4.4 Close-ups

| Interaction | Game hook | Today | Motion target | Phase |
|---|---|---|---|---|
| Fridge: hover items, hold to drink / consume | `FridgeCloseUpView`, `ConsumablesCloseUp`, `MouseParallax` | Laser hover, hold A | Pick an item with grip, hold it to the mouth; head position drives the parallax | 3, 4 |
| Phone keypad | `PhoneCloseUpView` | Laser + A | Poke the keys on a floating keypad | 3 |
| Radio: knob, AM/FM handles | `RadioCloseUpView`, `UIRadioKnob`, handles | Trigger + controller twist; grips for handles | Each hand grabs its own control; two-handed tuning | 3 |
| Mushroom list, notepad | `MushroomlistCloseUp`, `NotepadController` | Laser | Poke, scroll by stick or drag. **Needs recon: what the notepad does.** | 3 |
| Hold-to-close | `ACloseUpView._holdProgress` | Hold B | Pull the view away with both hands, or lean back | 3 |

### 4.5 Items, hands and the gun

| Interaction | `EHUDAnimation` | Motion target | Phase |
|---|---|---|---|
| Wash / inspect hands | `WatchCleanHands`, `WatchDirtyHands` | Raise both hands and turn them | 4 |
| Smoke | `SmokeCigarette` | Cigarette to mouth, inhale pause | 4 |
| Beer, kombucha | `DrinkBeer`, `DrinkKombucha` | Tilt the held can/bottle at the mouth | 4 |
| Eat mushroom | `EatMushroom` | Bring to mouth | 4 |
| Open tin | `OpenTin` | Two-hand pull / twist | 4 |
| Photograph | `CapturePhoto` | Hold a camera to the eyes, trigger as shutter | 4 |
| Throw postcard | `ThrowPovistka` | Throw: grip release with hand velocity above a threshold | 4 |
| Cat | `Take/Hold/ReleaseNaperdysh` | Grip to hold, release to put down | 1, 4 |
| Shotgun | `Gun`, `GunShow/GunHide`, `GunShot` | Two-hand raise, aim along the barrel, trigger fires. **Needs recon: where the game lets the player use it.** | 4 |

### 4.6 Menus and meta

| Interaction | Today | Motion target | Phase |
|---|---|---|---|
| Main menu (New game, Continue, Settings, Collection, Quit) | Laser + trigger | Poke; laser stays | 5 |
| Pause | Left Menu | Wrist-glance pause as an alternative; also required for Index | 5 |
| Settings, VR section sliders | Laser, stick | Poke and drag sliders | 5 |
| Collection (gacha), endings, credits, video skip | Laser, hold X | Poke, gesture skip | 5 |
| Recenter | Both stick clicks, F8 | Hold both fists at chest, or from wrist menu | 5 |
| Popup windows | Laser | Poke | 5 |

### 4.7 Other scenes

| Scene | Notes | Motion target | Phase |
|---|---|---|---|
| `s3_Ball` | 3D room; dancers are sprites on world-space canvases; `CameraRotate` drives the camera | Passive; verify it plays in VR (Gamepad mode) | 6 |
| `s4_StrangeMorning` | First-person 3D with its own `PlayerServiceMorning` and HUD | Same hallway rules; recon needed for what can be touched | 6 |
| `s6_MushroomDream` | Cinemachine-driven, no player controller | Passive; verify comfort | 6 |
| `s5_Titery` | Credits, flat | None | 6 |
| Basement, PreDeath, Death locations | Real 3D, inactive until used | Same as hallway | 1, 6 |

---

## 5. Roadmap

Each phase lists what can be done without the headset, what cannot, and an exit test. Phases 0 to 3 are the core; 4 is
the largest design decision; 5 to 7 are polish and options.

### Phase 0: Foundations and recon

**Goal:** know exactly what each interaction does in the game, and have a place for gesture code to live.

| # | Task | Where it can be done |
|---|---|---|
| 0.1 | Run Cpp2IL pseudo-code on `GameAssembly.dll` for: `AInteractableObject` and all subclasses, `AActionableObjectView` subclasses, `UIButton`, `HUDView.PlayAnimation`, `Gun`, `CatInteractable`, `NotepadController`, `DialogView.ShowSign`, `PlayerSigns`. Record whether each `Interact()` is press / hold / toggle, its `HardConditions` and where it plays which animation. | PC |
| 0.2 | Write `docs/INTERACTION_INVENTORY.md`: section 4 of this file, with every **(inferred)** replaced by a verified fact, per-object world position and collider size from a hallway dump. | PC to gather, cloud to write up |
| 0.3 | Add a pure-C# project `src/NIVR.Gestures` (no Unity, no game types; `System.Numerics`) with: hand kinematics filter, proximity/zone tests, push/pull, grab-hold, stroke, throw, lean-in, nod/shake, raise-hands recognizers. Add an xUnit project and a GitHub Actions workflow that builds and tests only these two projects. | Cloud (written); first CI run in GitHub |
| 0.4 | Request velocity in `LocateSpace` and expose `Velocity` / `AngularVelocity` on `VRController`. | Write in cloud; verify on PC |
| 0.5 | Introduce the feature-module seam: a small `IMotionModule` (`Tick(frame)`, `OnModeChanged`), a fixed tick order in `VRCoreBehaviour.Update`, and an input-arbitration object replacing the `Swallow*` statics for new code. | Write in cloud; compile and regression-test on PC |
| 0.6 | Extend the simulator: scripted hand paths (`simpath l|r <name>`), so gestures can be exercised and screenshotted without a headset. | PC |
| 0.7 | Confirm physical crouch's positive case and haptics in the headset (open items from 0.2.0). | Headset |
| 0.8 | Docs cleanup: a one-page "current architecture" at the top of `DEV_NOTES.md`, fix the stale comments in section 2.2, delete or archive `src/NIVR.Plugin`, single source for the version. | Cloud |

**Exit:** `INTERACTION_INVENTORY.md` has no inferred rows for hallway objects; CI is green on the gesture library; a
no-op module ticks in the headset without changing behaviour.

### Phase 1: Reach and grab in the hallway

**Goal:** doors, windows, the peephole, phone, radio, calendar, save point and cat can be used by reaching.

- Hand model with a defined palm and fingertip point; haptic hover when a hand is within reach of a target.
- `ReachTargets` provider: every `ARaycastTarget` / `LinkedActionableObject` in range with its bounds; refresh when the
  active location changes.
- Intent gestures: push/pull past 10 cm for doors, grab-and-pull for blinds, grab-and-sweep for curtains, lean-in
  to the peephole hole position and lean-back to leave, stroke to pet the cat, grip to carry. Thresholds are config
  values exposed in the VR settings page.
- Dispatcher calls `Act()` / `Interact()` through the existing conditions; on refusal show the game's faded hint and a
  "denied" pulse.
- Keep the laser: gesture and laser compete through the arbitration object, laser wins while pointing.

**Headset needed:** all of it for feel (reach distance, thresholds, hand scale, false positives). **Simulator:** the
recognizers and dispatcher paths through `simhand`/`simpath`. **Exit:** open every door, both blinds, the curtains and
the peephole by hand without touching a button, with no accidental activations in a 10 minute walk.

### Phase 2: Rooms, dialogs and inspection

**Goal:** the flat room pictures and dialog answers are touched, not only pointed at.

- Fingertip poke on the room enclosure wall and the dialog panel: hover on proximity, click on contact, haptics.
  Reuses the existing panel hit test (`FlatScreen.PanelHit`) with a fingertip point instead of a ray.
- Dialog answers by poke; forward flick to skip a line; optional nod / shake for two-answer dialogs (off by default).
- Lean-in to examine "signs" (the game's close-up sprites), photo held to eye level.
- Show-hands gesture, once 0.1 says what the game expects.

**Headset needed:** poke depth and click feel on the curved room wall, nod detection, hand-in-view comfort during the
peephole conversation. **Exit:** play a full visitor conversation and a room visit without the laser.

### Phase 3: Close-ups

**Goal:** phone, radio, fridge, consumables and lists use hands.

- Floating keypad for the phone close-up (poke).
- Radio: left hand and right hand each grab their own control; knob follows wrist roll with detent haptics (extends the
  existing twist code); AM/FM handles by grabbing.
- Fridge and consumables: pick up with grip, hold-to-drink replaced by bring-to-mouth (hand to the mouth zone under the
  head, dwell 0.8 s).
- Head offset drives the `MouseParallax` effect where the game uses it **(inferred)**.
- Close-ups close by pulling the view away or leaning back; B stays.

**Headset needed:** all hand-over-UI alignment, radio twist feel on real controllers (open since 0.2.0). **Exit:** tune
the radio, dial the phone, drink from the fridge, close each close-up, all by hand.

### Phase 4: Items, hand animations and the gun

**Goal:** decide and implement how consumable and prop animations look in VR.

Decision to take first (both options are cheap to prototype in Phase 0):

- **Option A, keep canned 2D animation on the UI panel.** The gesture only triggers `Interact()` (bring to mouth, tilt
  to drink). Cheapest, consistent with the game's art, but the animation is flat and head-locked, which may feel odd
  while the player's real hand is somewhere else.
- **Option B, suppress the HUD animation and show a 3D prop in the hand.** Needs per-item models (procedural or from
  loaded game sprites, never shipped, see DEV_NOTES 1.11 on runtime loading) and timing that matches the game's effect
  being applied. More work, much better presence.

Recommended: A for Phase 4's first release, B per item afterwards, starting with the cigarette and the drink, since
their gestures are the simplest. The gun and throw need the most care:

- Postcard throw: grip release above a hand-speed threshold, haptics at release.
- Shotgun: only after 0.1 shows where the game uses it; two-hand grab, aim along the barrel, trigger fires. If it is a
  scripted beat, the gesture is just the confirm.
- Photograph: camera held to the eye, trigger = shutter.

**Headset needed:** all. **Exit:** every `EHUDAnimation` has a motion trigger and a documented fallback.

### Phase 5: Menus and system gestures

- Poke support for main menu, pause, settings (including slider drag), collection, popups, using the existing direct
  `OnSubmit` path.
- Wrist-glance pause and recenter (needed for Index, which has no bindable menu button), and an alternative for Vive and
  simple-controller users (see 2.1 item 7).
- One-handed and swapped-hand modes (2.1 item 8).
- Extend the first-run controls card to describe the motion controls and show when a gesture family is off.

**Headset needed:** wrist glance false positives, one-handed mode on both controller families if available. **Exit:**
the game can be fully played with each supported controller profile.

### Phase 6: Other scenes

Walk through `s3_Ball`, `s4_StrangeMorning`, `s6_MushroomDream`, `s5_Titery`, Basement, PreDeath and Death in the
headset with motion controls on. Fix what the scene recon (0.2) finds. Nothing is planned beyond verification until the
recon shows scene-specific interactions.

### Phase 7: Optional and experimental

Each of these is off by default and can be dropped without affecting earlier phases.

- Hand tracking (`XR_EXT_hand_tracking`) for Quest users: fingertip poke without controllers.
- Arm-swing locomotion and room-scale nudge (the head collision fade exists already; moving the ECM2 character
  from the real position needs care around doors and scripted moves).
- Grab-and-pull turning.
- Voice confirm for dialogs (out of scope unless requested).

---

## 6. What can be done where

| Work | Cloud (no game, no headset) | PC without headset (simulator + `nivr.log`) | Headset on the PC |
|---|---|---|---|
| Pure gesture library + unit tests + CI | Write; CI builds and runs tests | - | - |
| Plugin code that touches game or Unity types | Can be written but **not compiled** here (needs interop assemblies) | Build, run simulator tests, capture screenshots | Feel and thresholds |
| Recon (Cpp2IL, dumps) | Write up results | Run | - |
| Docs, inventory, config design | Yes | - | - |
| Haptics, hand scale, comfort, false positives | - | - | Required |
| Controller families other than the owner's | - | - | Needs hardware or tester reports |

Practical flow for each phase: a PR with the recognizers and tests (verified by CI), a PR with the game-side wiring
(verified by a build and simulator run on the PC), then a headset session driven by a checklist generated from that
phase's exit criteria.

---

## 7. Open questions for the owner

1. Which controllers should be supported on day one: Quest (Touch) only, or Index / Vive / WMR as well? This decides
   how much of Phase 5's system-gesture work comes forward.
2. Phase 4 option A (canned animation, trigger by gesture) or B (3D props) as the first release?
3. Is room-scale locomotion in scope, or is the hallway stick-and-turn model final?
4. Can the next recon session include Cpp2IL output? Phases 1 and 4 cannot be finalised without it.
5. Is a separate gesture library project acceptable in the repo structure (it enables CI tests but adds a project)?

## 8. Suggested order

Phase 0, then Phase 1 (smallest change, biggest effect), Phase 3 (radio and phone, mostly extending existing code),
Phase 2, Phase 5, Phase 4, Phase 6, Phase 7. Phase 4 moves earlier if item animations turn out to feel worst in the
headset during Phases 1 to 3.
