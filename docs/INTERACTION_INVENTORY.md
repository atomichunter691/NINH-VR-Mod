# NIVR interaction inventory

Phase 0.1 / 0.2 of `docs/MOTION_CONTROLS_ROADMAP.md`. Written 2026-10-10 against game 1.3.19, NIVR 0.2.1.
This answers, for every interaction the game has, "what does the game call, is it a press, a hold or a toggle, what
gates it, and what does it play". No game code, dumps or art are in the repo; everything below is a summary.

## 0. How to read this

Every fact carries one of these marks:

| Mark | Meaning |
|---|---|
| **[S]** | Read from Cpp2IL ISIL pseudo-code of `GameAssembly.dll` (static). The control flow was read, not executed. |
| **[R]** | Observed at runtime in the simulator (`Backend = Simulator`, `SaveSandbox` on, the dev save: hallway at night; day 0 according to DEV_NOTES, the day number was not re-read here). |
| **(inferred)** | Neither. A reasoned guess that still needs checking. |

How the recon was done (so it can be repeated after a game update):

* The Cpp2IL library that BepInEx already ships in `game_copy\BepInEx\core` (`Cpp2IL.Core.dll`, `LibCpp2IL.dll`) was
  driven by a small throw-away console program. Nothing had to be downloaded. Output went to `recon\cpp2il\` (full
  disassembly + ISIL) and `recon\cpp2il_skel\` (compact: field offsets, vtable slots, resolved call targets), both
  git-ignored. 943 types of `Assembly-CSharp` in about a minute.
* ISIL is not C#. What it gives reliably: which methods a method calls, in which order, compared against which
  constants, and which fields it reads and writes. Three things needed care:
  1. Interface calls go through invoker helpers with a slot number; the slot is the method's index in the interface
     declaration. Struct-returning calls (`UniTask`) shift the slot into the second argument register.
  2. Short identical method bodies are folded by the linker, so one address can belong to many methods (shown as
     "N share" in the skeletons). Those call sites were resolved from context or left unresolved.
  3. Arguments passed on the stack from a register (not a constant) are not visible. Where that hides an enum value
     it is marked (inferred) below.
* Runtime facts come from Core's debug commands `inter`, `targets`, `objects`, `act`, `use`, `game`. `inter` and
  `objects` were extended in this pass to print the game's own gate values (see section 8).

## 1. The three interaction systems

### 1.1 Press-to-use objects: `AInteractableObject`

Base behaviour **[S]**: every frame `OnUpdate()` calls `Interact()` only if all of these hold, in this order:

1. the World map's Interact action was performed **this frame** (an edge, not a level),
2. the object has a `_raycastTarget` and that target is currently targeted by `RaycastSource`,
3. `OnUpdateExtraConditions()` is false (no subclass overrides it; it is always false),
4. `SoftConditions` is true,
5. `HardConditions` is true,
6. `_isEnabled` is true (`Enable()` / `Disable()` are counted; they also lock / unlock the ray target).

So **every `Interact()` is a press**. There is no hold and no built-in toggle in the base class. Where an object feels
like a toggle or a hold, that comes from what `Interact()` opens (a close-up, a zoom view, a dialog), listed per class
below. `EnergyCost` is 0 unless noted. `HardConditions` false is what produces the faded hint; the hint itself is
drawn by `RaycastTargetHint`, which is given the object's `HardConditions` as a delegate at `Init`.

Consequence for gestures: a dispatcher that calls `Interact()` directly **bypasses all six checks**. It must test
`_isEnabled`, `HardConditions` and `SoftConditions` itself (all three are readable through the interop, verified
**[R]**) and should require the object's ray target to be active and unlocked.

### 1.2 Look-at objects: `AActionableObjectView` (doors, peephole, blinds, curtains)

`Act()` is a **toggle** **[S][R]**:

* ignored while `_isAnimating`, or while the ray target is locked;
* not looking: if `ProhibitedToUseDaytime` contains the current time of day, plays a "cannot open" sound and a
  subtitle popup (`TryToOpenInWrongTimeOfDay`) and stops; same for `ProhibitedDays` (`TryToOpenInWrongDay`);
  otherwise `StartLooking()` (slide to `_standingPos`, forced look at `_lookAtPos`, FOV zoom, then the subclass's
  extra action);
* looking: `StopLooking()`.

`Update()` **[S]** calls `Act()` when Interact was triggered while the target is targeted and the view is not being
looked at, and when UI Exit was triggered while it is being looked at. It also keeps `_canLeave` in step with the
subclass's `CanLeave` and tells the HUD which button prompts are available. `TryLeave()` is simply "if looking,
`StopLooking()`": it does **not** check `CanLeave` **[S]**, which is why the mod's B handler works where the game's
own exit did not, and why the mod must keep checking `_canLeave` itself (it does, `PauseGuard.Exit`).

Runtime check **[R]**: `act 5` (Bedroom) set `IsLooking` true and the watcher state to `Room`; a second `act 5` set it
back to false. `act 3` (Office, prohibited at night) left `IsLooking` false.

### 1.3 Flat pictures: `UIButton` through `RoomDisplayer`

`RoomDisplayer.Update()` **[S]**, while a room is open and clicks are neither blocked nor locked: builds a pointer
event from the legacy `Input.mousePosition`, raycasts the canvas, runs `UIButton.IsMousePosInSpriteArea` (requires
`IsActiveCount >= 1`, then a per-pixel alpha test against the sprite's texture) on what it hits, calls `OnHover` /
`OnUnhover` and sets the cursor type, and on **UI Submit clicked or LMB clicked** calls `UIButton.Click()` on the
hovered button.

`UIButton.Click()` **[S]** invokes the stored action and clears the outline. It does **not** check
`AreButtonsEnabled`, `IsActive` or the alpha test; those live in `RoomDisplayer`. A press, not a hold. A gesture that
calls `Click()` directly must therefore do its own "is this button really hoverable" test, or (simpler and safer) keep
feeding the pointer position and let `RoomDisplayer` decide, as the laser does today.

## 2. Press-to-use objects, one by one

Conditions are the game's, in plain words. "Day" / "Night" is `ETimeOfDay`; day numbers are the game's day counter.

| Class | What `Interact()` does | Kind | `HardConditions` | `SoftConditions` | Energy | Plays |
|---|---|---|---|---|---|---|
| `PhoneInteractable` | Hides the hint, hides the handset model, locks its target, shows the phone close-up **[S]** | Press, opens a close-up | Time of day is Day **and** day < 14 **[S]**; false on the night save **[R]** | always | 0 | none |
| `RadioInteractable` | Hides the hint, shows the radio close-up **[S]** | Press, opens a close-up | Time of day is Day **[S]**; false at night **[R]** | always | 0 | none |
| `CigaretteInteractable` | Ignored while `_isInteracting`. Opens the consumable confirmation close-up for a cigarette, with use / close callbacks **[S]** | Press, then a Yes / No confirmation | Cigarette count > 0 **and** time of day is Day **and** day < 14 **[S]**; false on the night save **[R]** | always | 0 | On "Yes": one cigarette removed, `OnUse` plays a HUD animation, refills energy, removes tomorrow's extra energy slots, calls `PlayerSigns.Smoke()`, then re-enables after a pause **[S]**. The animation value is `SmokeCigarette` **(inferred from the adjacent constant 1; the call itself is [S])** |
| `SaveInteractable` | Same confirmation close-up, for kombucha **[S]** | Press, then a Yes / No confirmation | Kombucha count > 0 **and** day < 14 **[S]**; true on the dev save **[R]** | always | 0 | Save sequence in `InteractAsync`; which HUD animation it plays was not resolved **(inferred: `DrinkKombucha`)** |
| `CalendarInteractable` | Subclass of `ZoomInteractable`, see next row. Adds only the holiday page for the current day **[S]** | - | - | - | - | - |
| `ZoomInteractable` | Hides the hint, locks its target, sets `_isOpened`, activates a zoom camera object, shows the controls list, enters UI input state. Its `Update` closes it again on UI Exit **[S]** | Press to open, Exit to close (a toggle built from two different buttons) | always | always | 0 | none |
| `CatInteractable` | **Empty: `Interact()` returns at once** **[S]**. The cat object carries a second component, a `DialogInteractable`, on the same ray target (`CatRaycastTarget`) **[R]**. Petting and taking are dialogue commands, see 5.3 | Press, opens a dialog | always | always | 0 | `Pet()`: purr sound + particles. `Take()` / `TakeAsync`: hides the cat, locks all look-at objects, plays `TakeNaperdysh`, then `HoldNaperdysh`, waits a random time, plays a third animation and shows the cat again **[S]**; the third value is `ReleaseNaperdysh` **(inferred from the constant 9)** |
| `DialogInteractable` | One-time objects ignore repeat presses (`_isOneTime` + `_everTalked`). If the current time of day is in `_prohibitedTimeOfDay` it does nothing. Otherwise: hides the hint, makes the player immovable, enters UI input state, locks its target, activates its camera object and runs a dialog node; everything is undone in `OnDialogEnded` **[S]** | Press, opens a dialog | always | always | 0 | whatever the dialogue script asks for |
| `HatchInteractable` | Ignored while `_isInteracting`. Player immovable, tween of the hatch rotation, then `ILocationsManager.GoToLocation` (hallway <-> basement), then restores **[S]** | Press, scripted transition | Time of day is Day **and** day < 14 **[S]**; false at night **[R]** | always | 0 | none |
| `MushroomInteractable` | Subtitle popup, adds a consumable, sets a state object **[S]** | Press, one shot | always | always | 0 | none |
| `TheHoleInteractable` | Ignored while `_isInteracting`. Digging sound, player immovable, increments the hole's state (up to 3), `IDayNightController.ActAll` (spends the day's actions), a delay, a HUD animation, fade, `PlayerSigns.Dig()` (the player's hands become "fake" / dirty) **[S]** | Press, scripted sequence | Time of day is Day **[S]**; false at night **[R]** | Compares `DayActions` with `MaxDayActions` **[S]**; which way round was not resolved | **1** **[S][R]** | HUD animation value not resolved **(inferred: `WatchDirtyHands`, because of `Dig()`)** |
| `WindowBoardsInteractable` | Once only (`_hasAlreadyInteracted`). Player immovable, shows the boards and hides the listed objects, boarding sounds, `IGameplayEndingManager.TryNailUpWindowForBasement`, a subtitle popup depending on the ending, fade out **[S]** | Press, once | always | always | 0 | none found |
| `EndingLaunchInteractable` | Sets `_hasAlreadyInteracted`. Player immovable, fade in, `IGameplayEndingManager.GetEnding(...)` and its `ShowClip` **[S]** | Press, once | always | not yet interacted | 0 | ending clip |
| `CloseSceneInteractable` (not in the roadmap list) | Raises a bus event, waits, unloads a scene **[S]** | Press | always | always | 0 | none |

The roadmap listed "Zoom" and "Calendar" as separate things and the cat as a hold. Both were wrong, see section 7.

## 3. Look-at objects, one by one

All share section 1.2. Values are from the dev save **[R]** unless marked.

| Object | `CanLeave` **[S]** | Prohibited time of day | Prohibited days | `_fov` | `_awaitedPlayerLookAngle` | E key |
|---|---|---|---|---|---|---|
| Door 1 Kitchen | room has no dialog now, `CanLeaveRooms`, no close-up active | Night | 14 | 35 | 180 | no |
| Door 2 Office | same | Night | 14 | 20 | 180 | no |
| Door 3 BigRoom | same | Night | 14 | 40 | 0 | no |
| Door 4 Bathroom | same | Night | 14 | 30 | 90 | no |
| Door 5 Pantry | same | Night | 14 | 30 | 270 | no |
| Door 6 Bedroom | same | none | none | 25 | 90 | no |
| Peephole | room has no dialog now and the exit is not suspended | Day | none | 30 | 0 | no |
| Blinds 1 | the linked `WindowView.CanLeave` | Day | 13, 15, 16, 17 | 60 | 180 | yes |
| Blinds 2 | same | Day | 13, 15, 17 | 60 | 180 | yes |
| Curtains | same | Day | 13, 15, 16, 17 | 60 | 270 | no |

This explains the long-standing note in DEV_NOTES 2.8 that "Kitchen and BigRoom refuse, Bedroom opens": the dev save
is at night, and **every room door except the Bedroom is closed at night**. Windows and the peephole are the mirror
image: night only.

Extra actions **[S]**: doors enable the room's objects before opening and disable them before closing; blinds and
curtains call `WindowView.StartOfStartOpening` before opening. `_canBeOpenedByEKey` with `_eKeyChance` exists on the
blinds only; what the E-key path does differently was not read.

## 4. Hallway objects: where they are and how big

World positions in metres, from the hallway (`HouseInterior`) at runtime **[R]**. "Target" is the collider the game's
ray has to hit; its bounds are world-axis-aligned. The player's eye is 1.65 m above the floor (floor y is about -0.18).

### 4.1 Ray targets that are active on the dev save

| Object | Target centre (x, y, z) | Target size (x, y, z) | Notes |
|---|---|---|---|
| Door 1 Kitchen | (-2.11, 1.03, -11.85) | 1.01 x 2.34 x 0.06 | faces +z |
| Door 2 Office | (-0.27, 0.96, -11.82) | 1.01 x 2.34 x 0.06 | faces +z |
| Door 3 BigRoom | (0.98, 0.96, -8.83) | 1.01 x 2.34 x 0.06 | faces -z |
| Door 4 Bathroom | (-2.99, 0.90, -7.76) | 0.06 x 2.23 x 0.82 | faces -x |
| Door 5 Pantry | (-5.08, 0.95, -9.42) | 0.12 x 2.37 x 0.75 | faces +x |
| Door 6 Bedroom | (3.14, 0.95, -10.52) | 0.06 x 2.34 x 1.01 | faces -x |
| Peephole (front door) | (-4.12, 1.00, -4.23) | 1.16 x 2.40 x 0.12 | the target is the whole door; the hole itself is `_lookAtPos` (-4.12, 1.47, -4.23) |
| Blinds 1 | (-4.49, 1.52, -11.88) | 0.70 x 1.14 x 0.06 | |
| Blinds 2 | (2.05, 1.57, -11.88) | 0.66 x 1.25 x 0.06 | |
| Curtains | (-6.49, 1.46, -5.79) | 0.22 x 1.33 x 1.23 | |
| Phone | (-5.48, 0.22, -7.05) | 0.40 x 1.25 x 0.57 | target is tall and low (a table with the phone on it) |
| Radio | (2.47, 1.60, -9.04) | 0.30 x 0.43 x 0.10 | |
| Save point | (1.89, 0.86, -9.36) | 0.25 x 0.43 x 0.27 | |

Thin dimension = the door leaf thickness, so a hand "inside the box" test needs padding of 10 to 15 cm on that axis.

### 4.2 Present but inactive or without a target on the dev save

| Object | Position | State |
|---|---|---|
| Cigarettes | target at (-5.81, 0.76, -6.90) | target object inactive (no cigarettes owned); size unknown until it is active |
| Hatch (hallway) | target at (-1.12, 0.01, -10.01) | target inactive at night |
| Calendar | object at (-1.17, 1.45, -11.87) | **`_raycastTarget` is null**, so the base `OnUpdate` can never call `Interact()`. Calling it by hand opened the zoom state **[R]**. Whether the game ever enables it was not established |
| Cat | target `CatRaycastTarget` at (-4.44, 0.05, -5.88) | inactive (no cat yet) |
| Window boards (3) | (-6.61, 1.58, -5.78), (-4.45, 1.58, -12.37), (2.11, 1.58, -12.37) | inactive (ending content) |
| Peephole ending | (-4.11, 1.47, -4.23) | inactive (ending content) |
| Basement hatch, mushroom, the hole, `Podvalny_Talk` | y between -2.7 and -6.0 | inactive (Basement location) |
| `DeathDoor`, `Sphere` | far outside the house | inactive (Death / other locations) |

### 4.3 Stand and look-at points of the look-at objects

| Object | Standing point | Look-at point |
|---|---|---|
| Door 1 Kitchen | (-2.14, -0.18, -11.59) | (-2.30, 1.10, -12.98) |
| Door 2 Office | (-0.61, -0.18, -11.58) | (-0.14, 1.03, -13.09) |
| Door 3 BigRoom | (1.07, -0.18, -9.05) | (1.05, 1.21, -7.89) |
| Door 4 Bathroom | (-3.16, -0.18, -7.79) | (-2.01, 1.15, -7.74) |
| Door 5 Pantry | (-4.85, -0.18, -9.36) | (-6.01, 1.26, -9.34) |
| Door 6 Bedroom | (2.84, -0.18, -10.66) | (4.35, 1.03, -10.53) |
| Peephole | (-4.12, 0.10, -4.62) | (-4.12, 1.47, -4.23) |
| Blinds 1 | (-4.46, 0.27, -11.70) | (-4.48, 1.45, -13.34) |
| Blinds 2 | (2.01, 0.21, -11.70) | (1.98, 1.38, -13.34) |
| Curtains | (-6.00, 0.27, -5.82) | (-7.69, 1.45, -5.70) |

For "lean in to the peephole": the hole is 0.39 m in front of the standing point and 1.47 m up.

## 5. HUD animations, the gun, the signs

### 5.1 `HUDView.PlayAnimation` **[S]**

Looks up the `HUDAnimationData` whose `AnimationType` matches, starts it on the `AnimatedImage`, and waits for
`AnimationData.Duration`. It reads no input and cannot be scrubbed or cancelled: it is fire and wait. Callers reach it
through `IHUDPresenter.PlayAnimation`.

Who triggers which animation:

| `EHUDAnimation` | Triggered by | Evidence |
|---|---|---|
| `EatMushroom` | `FridgeCloseUpView.OnItemUsed` when the item is a mushroom | **[S]** constant 5 |
| `TakeNaperdysh`, `HoldNaperdysh` | `CatInteractable.TakeAsync`, started by the dialogue command `TakeCat` | **[S]** constants 7, 8 |
| `ReleaseNaperdysh` | same sequence, after the random hold time | (inferred) constant 9 |
| `SmokeCigarette` | `CigaretteInteractable.OnUse` (after "Yes" in the confirmation) | call **[S]**, value (inferred) |
| one animation | `TheHoleInteractable` sequence | call **[S]**, value not resolved |
| any of them | the dialogue command `PlayAnimation(<name>)`: `DialogCommandsInstance.PlayAnimation` -> `IDialogManager.PlayedAnimation` -> `HUDPresenter.OnPlayedAnimation` -> the view | **[S]** |
| `DrinkBeer`, `DrinkKombucha`, `OpenTin`, `CapturePhoto`, `ThrowPovistka`, `WatchCleanHands`, `WatchDirtyHands`, `GunShot` | No constant call site was found in code. They are either started by the dialogue command above (the animation name is data in the dialogue scripts, which were not read) or by the fridge's `Drink*Async` methods, which were not read | (inferred) |

Fridge **[S]**: `FridgeCloseUpView.Update` hovers items with the legacy mouse position and uses the hovered one on UI
Submit / LMB. `FridgeItemView` has `_isClicked` and `_useProgress`, set on pointer down and cleared on pointer up, so
**using a fridge item is a hold** (matches the sim observation in DEV_NOTES 2.12). `OnItemUsed` then branches per
item: coffee / energy drink call `PlayerSigns.DrinkCaffeine()`, the mushroom plays `EatMushroom` and may start a dream,
the cockroach (item 100) is killed.

Close-ups in general **[S]** (`ACloseUpView.OnUpdateAction`): if the view is "hold to close" and `CanHold`, UI Exit
must be **held** until `_holdProgress` reaches `HoldProgressTarget`, releasing early resets it; otherwise a UI Exit
press hides it. The consumable confirmation closes on a UI Exit press.

### 5.2 The shotgun **[S]**

The player never aims or fires. `Gun` has no input code at all: `Update` only sways the sprite while `_isTargeting`.
Everything is driven by **dialogue script commands** on `DialogCommandsInstance`:

| Command | Effect |
|---|---|
| `SetUpGun(true)` | `IDialogManager.GunShowed` -> `HUDPresenter.GunShow` -> `Gun.LoadGun` (pick-up sound, load frames, slide to the loaded position) |
| `SetUpGun(false)`, `DontSetUpGun()` | `GunHidden` -> `Gun.HideGun` |
| `KillCharacter(name)` | `GunShot` -> `Gun.Shot` (shot frames), which ends in `HideGun`; `DialogManager.CompleteShotAnimation` then skips the line |
| `FakeShot()` | `FakedShot` -> `Gun.FakeShot` |
| `KillCharacterWithNoGun`, `KillRoom`, `KillTomorrow` | kill without the gun animation (inferred from the names; bodies not read) |

So "where does the game let the player use the shotgun": **only inside a dialog, as the consequence of choosing an
answer**. The script raises the gun when the conversation reaches that point and fires it when the player picks the
answer that kills. For motion controls the shot is a confirm on a dialog answer, not a free action. A raise-and-aim
gesture can only ever be a different way of picking that answer.

### 5.3 Signs and "show me your hands" **[S]**

* Visitor signs: dialogue command `ShowSign(character, sign)` -> `DialogManager.OnSignShowed` ->
  `DialogView.ShowSign` -> `DialogSignsView.ShowSign`. The view activates the container for that sign (`ECharacterSign`:
  Eye, Hands, Teeth, AuraPhoto, Armpit, Ear), sets the sprite, plays a sound and **calls `Vibrator.VibrateForTime`**
  (the game's own gamepad rumble; a ready-made place to hook controller haptics). `StopShowingSign` hides them.
  Passive: no input is read while a sign is shown.
* The player's own signs: dialogue command **`ShowPlayerSign(sign)`** -> `DialogManager.OnPlayerSignShowed`, which
  asks a subscriber whether that sign of the player is currently fake, finds the Player character data and shows the
  matching sprite through the same `DialogView.ShowSign`.

So "show me your hands" is **a scripted beat inside a visitor's dialog**, not a player choice and not an input: the
script decides when the hands are shown, and the game decides which picture from `PlayerSigns`. The state behind it:

| `PlayerSigns` flag | Starts as | Changed by |
|---|---|---|
| `IsHandsFake` | false | `Dig()` (the hole) sets it; `Wash()` and `ResetHands()` clear it |
| `IsTeethFake` | true | `Smoke()` clears it; `ResetTeeth()` sets it |
| `IsEyeFake` | false | `DrinkCaffeine()` sets it; `ResetEye()` clears it |
| `IsArmpitFake` | false | `ResetArmpits()` sets it; `Wash()` clears it |
| `IsPhotoFake` | true | not changed in this class |

A raise-both-hands gesture therefore cannot "give the answer". It can only be an optional flourish while the game is
already showing the hands, or (if the dialog offers a "show hands" answer as a normal option) another way to pick that
option. Which of the two applies needs one visitor conversation watched in the game.

### 5.4 Other dialogue commands that matter for gestures **[S]**

`PetCat()`, `TakeCat()`, `GetCat()`, `WashArmpits()`, `GetItem`, `TryGiveResource`, `GoToLocation`, `FadeIn/Out`,
`PlayCutscene`, `Sound`. The cat is therefore: press on the cat -> a dialog -> the answers "pet" / "take" run these
commands. A stroke gesture has nothing to call directly except `CatInteractable.Pet()` (sound + particles, no game
state), and a grab gesture would have to call `Take()`, which starts a fixed timed sequence, not a carry.

## 6. Notepad **[S]**

`NotepadController` only has `Open()` and `Close()`: they activate the view, make the player immovable / movable and
tween the pad up or down. Nothing in the game's code calls them through `INotepadController` except start-up wiring.
`NotepadView.Update` drives itself from the **legacy input API**: two `Input.GetKeyDown` checks (key codes not
resolved), `Input.GetMouseButtonDown/Up(0)` and the mouse position (the pad tilts with the mouse and can be dragged).

It has no gamepad binding, so **the notepad cannot be opened with the VR controllers today**. If it is wanted in VR,
the mod has to call `Open()` / `Close()` itself.

## 7. Corrections to the roadmap

| Roadmap said | Fact |
|---|---|
| Cat: "grip to pick up, release to put down (inferred: hold semantics)" | No hold exists. `CatInteractable.Interact()` is empty; petting and taking are dialogue commands; `Take` is a fixed timed sequence |
| Calendar: "poke / swipe on the page (inferred: world-space canvas)" | It is a zoom view (`ZoomInteractable`) and has no ray target on the dev save, so the game itself never opens it there |
| "Zoom" and "Calendar" as separate hallway objects | `CalendarInteractable` is the only `ZoomInteractable` in the hallway |
| Shotgun: "two-hand raise, aim along the barrel, trigger fires" | Scripted by the dialogue; the player's only input is the dialog answer |
| "Show me your hands": "raise both hands ... to give the answer" | A scripted display; no answer is given by the player at that moment |
| Notepad: "poke, scroll by stick or drag" | Keyboard and mouse only; unreachable from the controllers today |
| Phase 4: "every `EHUDAnimation` has a motion trigger" | Most animations are started by dialogue scripts or after a Yes / No confirmation. The gesture can replace the confirmation, not the animation trigger |
| 2.1 item 6: "nobody has done Cpp2IL" | Done; the tool is already in `BepInEx\core` |
| Fridge "hold A" | Confirmed: `FridgeItemView` accumulates a use progress while pressed |

## 8. Things found on the way

1. **`PauseGuard` fights `ZoomInteractable`.** After `use 15` (calendar) the game was in UI input state with the
   watcher still on `World3d`; 2.5 s later the log shows "UI-state counter stuck at 1 in the hallway; releasing it"
   **[R]**. The guard cannot tell a zoom view from the trap it was written for. Not reachable in the hallway today
   (the calendar has no target), but any gesture that opens a zoom view will hit it. Fix when Phase 1 needs it: skip
   the release while any `ZoomInteractable._isOpened` is true.
2. **`TryLeave()` ignores `CanLeave`.** Correct today because `PauseGuard.Exit` checks `_canLeave` first. A lean-back
   gesture must do the same.
3. **The game already rumbles on signs** (`Vibrator.VibrateForTime`). Mapping that to controller haptics is a small,
   self-contained win.
4. **Debug commands extended** (gated by `[Debug] CommandDir` as before): `inter` now prints `enabled`, `hard`, `soft`,
   `energy` and the linked ray target with its bounds and lock state; `objects` prints `looking`, `animating`,
   `locked`, `canLeave`, the E-key flag, the look angle and the prohibited times and days.

## 9. Still open

| Question | How to answer it |
|---|---|
| Which HUD animation the hole and the save point play; the third cat animation | Re-run the Cpp2IL driver with stack-register tracking, or trigger them on a later save and watch `HUDView` |
| Which dialogue nodes call `PlayAnimation`, `SetUpGun`, `ShowPlayerSign` | Read the Yarn program at runtime (node names only, no text in the repo) |
| Daytime values: phone, radio, cigarette, hatch conditions true; doors open; windows refuse | Needs a daytime save. All of section 3 and the [R] marks in section 2 are from one night save |
| Cigarette target size; cat, hatch and basement object bounds | Same, on a save where they are active |
| `Drink*Async` in the fridge, `Blinds` E-key path, `WindowView.CanLeave` | Not read |
| Notepad key codes | Not resolved from the pseudo-code |
| `s4_StrangeMorning` interactables (`StrangeMorningInteractableManager`) | Not looked at; Phase 6 |
