# NIVR - VR mod for "No, I'm not a Human" - shared notes

Shared by all chats working in `C:\VRMod\`. Read first, append what you learn. Keep entries factual;
mark anything not verified at runtime as **(inferred)**.

---

# 1. Recon + build environment (chat 1, 2026-10-02)

## 1.1 TL;DR

| Fact | Value |
|---|---|
| Steam install (never touch) | `C:\Program Files (x86)\Steam\steamapps\common\No, I'm not a Human\` (appid 3180070, buildid 23967901, game version 1.3.19) |
| Working copy | `C:\VRMod\game_copy\` (full copy + BepInEx + `steam_appid.txt`) |
| Engine | Unity **6000.3.10f1** (Unity 6.3), x64, D3D11 at runtime |
| Scripting backend | **IL2CPP**, metadata v39, not encrypted/obfuscated (real class names) |
| Render pipeline | **URP** (`URP-HighFidelity` asset, quality level 2), Forward, linear, HDR, MSAA 4x |
| Mod loader | **BepInEx 6.0.0-be.788** `Unity.IL2CPP-win-x64` (bleeding edge; builds < 755 can't read metadata v39) |
| Plugin target | `net6.0`, `BasePlugin`, references `BepInEx\core` + generated `BepInEx\interop\*.dll` |
| Anti-tamper | None found. No anti-cheat/DRM dlls, Doorstop injection + Harmony detours run fine. Only Steamworks.NET (`SteamManager`) and AES-encrypted saves (`AesCrypter`), which is not a binary integrity check. |
| XR in the build | `UnityEngine.XRModule`/`VRModule` are present, URP `XRSystem` initialises, but there is **no** XR plugin (no `UnitySubsystems` folder, no OpenXR/XR Management assemblies). The VR chat must bring its own. |
| Libraries in game | Zenject (DI), UniTask, DOTween, Cinemachine 3 (using legacy `CinemachineVirtualCamera`), ECM2 (Easy Character Movement 2), Input System, Yarn Spinner, TextMeshPro + RTLTMPro, Addressables (localization only), IngameDebugConsole |

## 1.2 Folder layout and ownership

```
C:\VRMod\
  NOTES.md                    shared
  Directory.Build.props       chat 1  - shared build settings (GameDir, references, auto-deploy)
  scripts\                    chat 1  - build.ps1 run.ps1 cmd.ps1 shot.ps1 restore.ps1 dumptree.py
  src\NIVR.DevTools\          chat 1  - dev-only plugin (log mirror, screenshots, dumps, save sandbox)
  src\NIVR.Plugin\            UNOWNED skeleton ("hello world"), unused - the VR core lives in plugin\Core instead
  plugin\Core\                chat 2  - VR core: OpenXR, stereo rendering, head tracking, VRRig API (see section 2)
  recon\                      chat 1  - decompile + dumps (reference only, never ship)
  tools\                      chat 1  - BepInEx zip, ilspycmd
  backup\saves_2026-10-02\    chat 1  - save backups (Steam Cloud files + PlayerPrefs .reg)
  logs\                       runtime output: nivr.log, shots\, dumps\, cmd\
  game_copy\                  working copy of the game (BepInEx\plugins\NIVR\ = deploy target)
```

New modules: add `src\NIVR.<Something>\NIVR.<Something>.csproj` containing only
`<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>…</AssemblyName></PropertyGroup></Project>`;
`Directory.Build.props` supplies target framework, references and deploy. Or add files to `NIVR.Plugin`.

## 1.3 Build / deploy / log loop

```
.\scripts\run.ps1                 # build all + deploy + launch windowed 1280x720 + tail logs\nivr.log
.\scripts\run.ps1 -NoTail -WaitReady 90   # scripted use: returns when BepInEx chainloader is up
.\scripts\build.ps1               # build + deploy only (stops the game first so dlls can be replaced)
.\scripts\cmd.ps1 shot myname     # in-engine screenshot  -> logs\shots\myname.png
.\scripts\cmd.ps1 dump myname     # full scene dump       -> logs\dumps\myname.txt
.\scripts\shot.ps1 myname         # external window capture (works even if plugins are broken)
.\scripts\restore.ps1 -DisableMod | -EnableMod | -RebuildCopy | -VerifyOriginal
python .\scripts\dumptree.py <dump> tree|sub|stats|grep ...   # query a scene dump
```

* Logs: `logs\nivr.log` mirrors every BepInEx log line incl. Unity `Debug.Log` (previous run kept as
  `nivr.log.prev`); BepInEx's own log is `game_copy\BepInEx\LogOutput.log`; Unity's is
  `%USERPROFILE%\AppData\LocalLow\Trioskaz\NoImNotAHuman\Player.log`.
* Steam must be running. Launching the copy directly works because of `steam_appid.txt`.
* Startup to main menu takes about 10 s; `Continue` to gameplay about 3 s more.
* Verified: hello-world plugin logs `NIVR hello world - Unity 6000.3.10f1, game NoImNotAHuman 1.3.19`.

### DevTools command channel (`src\NIVR.DevTools`)
Drop a `*.cmd` text file in `logs\cmd\` (that is what `cmd.ps1` does), one command per line. Hotkeys: F9 shot, F10 dump.

| Command | Effect |
|---|---|
| `shot [name]` / `dump [name]` | screenshot / scene dump (also automatic 4 s after every scene change, as `auto_<scene>`) |
| `click <GameObjectName>` | invoke the `Button.onClick` of the active GameObject with that name (`click Continue` on the menu enters gameplay) |
| `inspect <GameObjectName>` | IL2CPP-reflection field dump of its components -> `dumps\inspect_<name>.txt` |
| `inspecttype <TypeName>` | same for every loaded MonoBehaviour/ScriptableObject of that type -> `dumps\type_<name>.txt` |
| `savesprite <SpriteName>` | PNG preview of a loaded sprite -> `logs\shots\sprites\` (**game art, dev only, never ship**) |
| `inputjson`, `buildscenes`, `scenes` | input action asset JSON, build scene list, loaded scenes |
| `loadscene <i|name>`, `openscene <i>`, `timescale <f>`, `quit` | `SceneManager.LoadScene`, game's `ScenesChanger.OpenScene`, `Time.timeScale`, quit |

Config: `game_copy\BepInEx\config\nivr.devtools.cfg` (`LogDir`, `AutoDumpOnSceneLoad`, `AutoDumpDelay`, `SaveSandbox`).

## 1.4 KNOWN ISSUES / gotchas (read before writing code)

1. **The copy shares the real save.** Saves live in PlayerPrefs (`HKCU\Software\Trioskaz\NoImNotAHuman`, keys
   `GameSaveData`, `MetaPrefsData`, AES-encrypted JSON) **and Steam Cloud** (same appid; the user has real
   progress there). DevTools' `SaveSandbox` (default on) swallows `PlayerPrefs.SetString` and
   `SteamRemoteStorage.FileWrite/FileWriteAsync/FileDelete`, so loading works but nothing is persisted.
   Verified after all recon sessions: cloud files and prefs keys byte-identical to the backup.
   Do not turn the sandbox off, and do not ship DevTools. Backup: `backup\saves_2026-10-02\`.
2. **Unity 6 blittable-array returns are broken in the interop assemblies.**
   `UnityEngine.Bindings.BlittableArrayWrapper.Unmarshal` throws `InvalidOperationException: Instances of
   abstract classes cannot be created`. Verified for `ImageConversion.EncodeToPNG`; expect the same for any
   engine API that returns a blittable array (`GetPixels32`, `Mesh.vertices`, ...) **(inferred)**. Workaround:
   resolve the `*_Injected` icall yourself, see `src\NIVR.DevTools\UnityWorkarounds.cs`. Arrays of objects
   (`GetComponents`, `sharedMaterials`, `GetRootGameObjects`, `FindObjectsOfTypeAll`) work.
3. `ScreenCapture.CaptureScreenshot` throws `MissingMethodException` (ReadOnlySpan unstrip gap). Use
   `ReadPixels` at `WaitForEndOfFrame` (see `DevToolsBehaviour.CaptureAtEndOfFrame`).
4. Boxed IL2CPP primitives: `Il2CppSystem.Object.ToString()` on a boxed float/int returns garbage; use `Unbox<T>()`.
5. Interop decompile has **signatures and fields only, no method bodies**. Behaviour described below comes from
   names, signatures and live field values; method internals are **(inferred)** unless stated otherwise.
6. Coroutines from a plugin: `StartCoroutine(MyEnumerator().WrapToIl2Cpp())` (`BepInEx.Unity.IL2CPP.Utils.Collections`).
   Injected MonoBehaviours need the `(IntPtr ptr) : base(ptr)` constructor and `AddComponent<T>()` from `BasePlugin`.
7. Harmony patches on game methods with simple signatures work (verified on `PlayerPrefs.SetString`,
   `SteamRemoteStorage.FileWrite`). Methods returning `UniTask` were not tested.
8. Many GameObject names are Cyrillic. Dumps are UTF-8; in PowerShell use `Get-Content -Encoding UTF8`.
9. Loading `s3_Ball` or `s5_Titery` directly with `loadscene` logs errors (they expect to be entered through
   the game flow; `s5` looks for an editor path `Assets\_Presentation\Titles.txt`).
10. BepInEx first launch after a game update regenerates `BepInEx\interop` (about 30 s, downloads Unity base
    libs from unity.bepinex.dev once). Rebuild plugins afterwards.
11. Harmless log noise: `Class::Init signatures have been exhausted, using a substitute!`.

## 1.5 Recon artifacts (`C:\VRMod\recon\`)

* `api_summary.txt` - every class in `Assembly-CSharp`, `Assembly-CSharp-firstpass`, `ECM2`: fields, properties,
  method signatures, enum values (11k lines; grep `^=== .*ClassName`).
* `decompiled\<assembly>\` - ilspycmd output of the interop assemblies (verbose, same information).
* `runtime_dumps\auto_<scene>.txt` - full hierarchy + component details for all 7 scenes.
* `runtime_dumps\inspect_*.txt`, `type_*.txt` - live field values (player, HUD, input, dialog, volumes, URP assets, characters).
* `runtime_dumps\input_PlayerInputActions.txt` - the input action asset JSON.

## 1.6 Scenes

| # | Scene | What it is | 3D or flat |
|---|---|---|---|
| 0 | `s0_MainMenuScene` | main menu, collection (gacha), settings | **flat**: one ScreenSpaceCamera canvas (523 objects), no 3D geometry |
| 1 | `s1_GameScene` | the whole game | **3D first-person house** + many flat UI layers (details below) |
| 2 | `s2_Loading` | transient loading scene between 0 and 1 | not dumped (lives < 1 s) |
| 3 | `s3_Ball` | "Ball" dream | 3D room (6 meshes, 8 lights), dancers are sprites on **WorldSpace canvases** (`CoupleCanvas` x4, `FarWoman`), `CameraRotate` drives the camera |
| 4 | `s4_StrangeMorning` | "strange morning" dream | **3D first-person** (47 meshes), own `PlayerInstance` (`PlayerServiceMorning`), `HUD` ScreenSpaceCamera canvas, global volume `BodyEater` |
| 5 | `s5_Titery` | credits | **flat**: overlay canvas |
| 6 | `s6_MushroomDream` | mushroom dream | 3D (39 meshes), Cinemachine-driven camera fov 90, no player controller, volume `MushroomDream` |

### `s1_GameScene` roots
`Game view` (3D world + global Volume), `Canvases` (all screen UI), `Logic` (Zenject `SceneContext` +
`GameplaySceneInstaller`, `ViewProviders`, `InputHandler`, `Dialogue System`, `EventSystem`), `Cutscenes`
(`3D_Fek` PlayableDirector), `-=DEV UTILS=-`, `PlayerInstance`.

**Real 3D geometry** (`Game view/Locations/*`, each has a `Location` component, switched by `ILocationsManager` /
`TriggerObjectGoToLocation`, player moved with `IPlayerService.TeleportTo(StartPoint)`):
* `HouseInterior` - the hallway ("Коридор") with 6 doors, peephole, 2 blinds windows + 1 curtain window, phone,
  radio, calendar, save point, cat. 111 MeshRenderers, 5 skinned meshes, 25 lights, 8 particle systems, URP/Lit.
  This is the only place the player walks around during normal play.
* `Basement` (28 meshes), `PreDeath`, `Death` - inactive until used; own volume profile/skybox/fog via `Location`.

**Flat 2D content shown as UI** (all `UnityEngine.UI.Image`, no SpriteRenderers anywhere in the scene):

| View | Where | Canvas | Notes |
|---|---|---|---|
| Rooms behind the doors: Kitchen, Office, Bedroom, BigRoom, Bathroom, Pantry, Entrance | `Canvases/RoomDisplayer/<Room>` (`KitchenView` etc., base `ARoomView`) | `RoomDisplayer` canvas, **ScreenSpaceCamera**, worldCamera = Main Camera, planeDistance 2 (changed per room by `RoomDisplayer.SetData(Camera roomLinkedCamera, float roomCameraDistance, float dialogCanvasDistance)`) | Each room is a full-screen `BG` illustration with child Images for characters/objects (`CharacterRoomObjectView`, `ObjectRoomObjectView`, `NarrativeRoomObject`), clickable through `UIButton`. Bedroom has a `VideoPlayer` + `RawImage` (TV). |
| Window views | `Canvases/RoomDisplayer/Blinds1WindowView`, `Blinds2WindowView`, `CurtainsWindowView` (`WindowView`) | own **WorldSpace** canvas 1920x1080 | Layers: sky Image (`небо`), view Image (`fake_2window_0day` 1920x1080, changes per day via `WindowView.InitImages(int day)`), animated extras (`AnimatedImage`). Has `LinkedCamera`, `CameraDistance`. |
| Peephole | `.../Actionable objects/Peephole` (`PeepholeTrigger`) | - | Switches to a second **3D camera** `PeepholeCam` (fov 32.9, Skybox clear, at the front door) and drives `LensDistortion` on the global volume (`_distortionScale` 0.5). The visitor is a **2D sprite** (`DialogView._character`, an `AnimatedImage` on the dialog canvas) under a full-screen `peepholeOverlay` 1920x1080 sprite (`EDialogOverlayType.Peephole`, black at 77% alpha). Not visually verified in recon (needs a visitor at night). |
| Dialog (text, answers, character portrait, "sign" close-ups of eyes/hands/teeth/ear/armpit/photo) | `Logic/Dialogue System/Canvas` (`DialogView`, `DialogSignsView`, Yarn `LineView`/`OptionsListView`) | **ScreenSpaceCamera**, planeDistance **0.13** | Overlays: Peephole, TV (`TV 1` sprite), FullBlack. |
| Item / hand animations | `Canvases/HUD_Camera/AnimationsLayer` | **ScreenSpaceCamera**, planeDistance **0.10001**, order 1000, layer `OverUI` | Full-screen 1920x1080 frame animations (see hands section). |
| HUD | `Canvases/HUD_Overlay` (`HUDView`, `Gun`) | **ScreenSpaceOverlay**, order 10 | Energy pips (`ActionsView`), interaction hint, controls list, gun, screamers (`RawImage`), dream `RawImage` (`_dreamImage`), fade `ColorOverlay`, save icon, subtitles, ending BG. |
| Close-ups: fridge, phone, radio, consumables, mushroom list | `Canvases/FridgeCloseUp`, `PhoneCloseUp`, `RadioCloseUp`, `ConsumablesCloseUp`, `MushroomlistCloseUp` (`ACloseUpView` subclasses) | **ScreenSpaceCamera**, planeDistance 0.11 to 1 | Flat images; `MouseParallax` on fridge and phone. |
| Notepad | `Canvases/Notepad` | ScreenSpaceOverlay | |
| Pause + settings | `Canvases/Pause` (218 objects) | ScreenSpaceOverlay, order 999 | |
| Popup, Ending (video) | `Canvases/PopupCanvas`, `Canvases/Ending` (`VideoPlayer` + `RawImage`) | ScreenSpaceOverlay, order 1000 / 100 | |
| In-world UI | `CalendarCanvas`, `PhonePin (n)/Canvas` under `Actionable objects` | **WorldSpace** | already VR-friendly |
| Cutscene blackout | `Cutscenes/3D_Fek/BlackScreenCanvas` | ScreenSpaceOverlay | |

All screen-space canvases checked use `CanvasScaler` ScaleWithScreenSize, reference 1920x1080, match 0.5.
VR consequence: every ScreenSpaceOverlay canvas is invisible in a headset and the ScreenSpaceCamera ones sit
0.1 to 0.13 m from the eye (main camera near plane is 0.1); all need re-hosting as world-space panels.

### `s0_MainMenuScene`
`Canvas` (ScreenSpaceCamera, planeDistance 100, `MainMenuView` on `MainMenuViewProvider`); buttons are
GameObjects `NewGame`, `Continue`, `Settings`, `Collection`, `Quit` under `Canvas/NotBg`. Also `InGameDevMenu`
(developer cheat canvas shipped in the build, ScreenSpaceOverlay), `IngameDebugConsole` (inactive),
`EndingShower` (overlay + VideoPlayer). Main Camera: fov 60, post-processing off.

## 1.7 Player and camera (`s1_GameScene`, same rig in `s4`)

```
PlayerInstance                         _Code.Infrastructure.Player.PlayerInstance (.Character, .PlayerController, ...)
  PlayerCameraController
    CM Normal Camera                   CinemachineVirtualCamera + Cinemachine3rdPersonFollow + BasicMultiChannelPerlin (head bob)
    CM Crouched Camera                 same, lower
    CM FixedLook Camera                CinemachineVirtualCamera + CinemachineHardLookAt (forced look-at/zoom)
    Main Camera   (tag MainCamera)     Camera fov 60 near 0.1 far 1000, AudioListener, UniversalAdditionalCameraData
                                       (Base, postProcessing on), CinemachineBrain, CameraResolutionFix (disabled)
  Player                               Rigidbody, CapsuleCollider (r 0.3, h 2), ECM2.CharacterMovement, ECM2.Character,
                                       _Code.Player.PlayerController, PlayerFootstepSoundPlayer, _Scripts.Raycast.RaycastSource
    Camera Target                      empty; eye point 1.65 m above the feet (crouched vcam is 1.0 m above the feet)
```

* The **Main Camera is not a child of the player**; `CinemachineBrain` copies the live virtual camera onto it
  every frame. A VR rig must disable/override the brain (or drive `Camera Target`) or it will fight head tracking.
* `_Code.Player.PlayerController` (MonoBehaviour): fields `maxPitch` 80, `minPitch` -80, `mouseSensitivity` (1.5, 1.25),
  `cameraTarget`, `normalCamera`, `crouchedCamera`, `lookedAtCamera`, `cameraNoiseAmplitudeMultiplier` 0.5,
  `_cameraTargetPitch`, `_character`, `_inputHandler`, `_isLookingAt`; properties `RealCamera` (the Main Camera),
  `CurrentCamera`. Methods: `Init(InputHandling)`, `Update`, `LateUpdate`, `HandleMovement`, `HandleCrouching`,
  `HandleRun`, `HandleRotation`, `AddControlYawInput(float)`, `AddControlPitchInput(float value, float min = -80, float max = 80)`,
  `SetIsLookingAtState(bool)`, `ResetNoiseAmplitude`, `UpdateNoiseAmplitude`, `OnCrouched`, `OnUnCrouched`.
  Yaw rotates the `Player` transform, pitch rotates `Camera Target` **(inferred from names; values verified)**.
* `ECM2.Character` live values: `_maxWalkSpeed` 2, `_maxWalkSpeedCrouched` 0.75, `_maxAcceleration` 6, `_canEverJump` false,
  `_canEverCrouch` false in the hallway (crouch/run are zone-gated: `TriggerObjectCrouchZone`, `TriggerObjectRunZone`,
  `IPlayerService.SetRunAvailability`), `_rotationMode` None, `_camera` = Main Camera. Collision layers mask 5.
* `_Code.Infrastructure.Player.PlayerService` (Zenject service implementing `IPlayerService`; `PlayerServiceMorning` in s4):
  `CanMove`, `CanLookAround`, `IsInRoom`, `IsCrouched`, `Position`, `LookDirection`, `MakeMovable()`, `MakeImmovable()`
  (counter based), `LookAtWithZoom(Transform lookAtPos, float fov, float duration)`, `ResetFov(float)`,
  `MoveXZ(Vector3, float)`, `TeleportTo(StartPoint)`, `SetRunAvailability(bool)`. Forced look + FOV zoom is used by
  every door/window/peephole interaction; it needs a VR-safe replacement.
* Getting the rig from a plugin: `Camera.main`, or find `PlayerInstance` and use `.PlayerController.RealCamera`.
* Other cameras: `PeepholeCam` (inactive until used). Dream scenes have their own `Main Camera`.
* Camera scripts that exist but matter little: `CameraRotate` (s3 dance), `CamOnRail`, `CameraResolutionFix`,
  `ScreenResolutionBoxesAdjuster` (letterboxing HUD boxes).

## 1.8 Input

Unity **Input System** only. One generated wrapper `PlayerInputActions` (asset `PlayerInputActions`), schemes
`Keyboard And Mouse` and `Gaypad` (sic), a `PlayerInput` component (InvokeCSharpEvents) and `InputSystemUIInputModule`.

`_Code.Player.InputHandling` (MonoBehaviour; on `Logic/InputHandler` in s1, on `EventSystem` in s0; reached through
`IInputHandlerProvider.InputHandler`) is the **single choke point**: all game code polls its properties.
Patching these getters is the simplest way to inject VR controller input:

`MoveInput` (Vector2), `LookInput` (Vector2), `CrouchTriggered`, `CrouchPressed`, `RunTriggered`, `RunPerformedThisFrame`,
`RunReleasedThisFrame`, `InteractTriggered`, `PauseTriggered`, `UIDialogSkipClicked`, `UISubmitClicked`, `UISubmitDown`,
`UISubmitUp`, `UIExitTriggered`, `UIExitReleased`, `UIExitPauseTriggered`, `UITutorTriggered`, `UIRadioKnob` (Vector2),
`UIRadioLeftHandle`, `UIRadioRightHandle`, `UICancelClicked`, `UISwitchTab`, `UIScroll` (Vector2), `LMBClicked`,
`SkipVideoPressed/Clicked/Released`, `SpeedUpPerformedThisFrame`, `SpeedUpReleasedThisFrame`, `SpeedUpValue`, `AnyKeyClicked`,
`CurrentDevice` (`EInputDevice` None/MouseAndKeyboard/Gaypad), event `InputDeviceChanged`.
Methods: `SetIsInUIState(bool)` (switches World/UI maps, counter `_inUiCounter`), `SetMouseSensitivity(float, bool invertY)`,
`SetGamepadSensitivity`, `GetKey(EControl)`, `GetGaypadType()`, `SetGamepadCursorActiveState(bool)`, `ForceSetUI()`,
`OnControlsChanged(PlayerInput)`, `OnLocked()`, `OnUnlocked(bool)`.

Bindings (from the asset JSON):

| Map/Action | Keyboard+mouse | Gamepad |
|---|---|---|
| World/Move | WASD | leftStick |
| World/Look | mouse delta (x0.1) | rightStick |
| World/Interact | LMB, E, Space | buttonSouth |
| World/Crouch | LeftCtrl | rightStickPress |
| World/Run | Shift | rightTrigger |
| World/Pause | Esc | start |
| UI/Point | mouse position | rightStick (scaled, drives `GamepadCursor`) |
| UI/LMB, UI/Select | LMB | buttonSouth (Select) |
| UI/Submit | Enter | buttonSouth |
| UI/DialogSkip | Space, LMB | buttonWest |
| UI/Exit | Q, Esc | buttonEast, start |
| UI/Cancel | Q | buttonEast |
| UI/PauseExit | Esc | start |
| UI/Tutor | F | buttonNorth |
| UI/Navigate | WASD, arrows | dpad, leftStick |
| UI/Scroll | wheel | rightStick |
| UI/SkipVideo | Space | buttonSouth |
| UI/SpeedUp | Shift | rightTrigger |
| UI/RadioKnob, RadioLeftHandle, RadioRightHandle, ChangeTab | - | rightStick, LB, RB, LB/RB |

UI module wiring: point = `UI/Point`, left click = `UI/LMB`, submit, cancel, scroll, navigate.

Related classes:
* `GamepadCursor` (on `Logic/InputHandler`): software cursor for gamepads (`_cursorSpeed` 128, `_cursorPosition`,
  `Enable/Disable/SetActiveState`). A VR laser pointer can reuse this path (warp the virtual mouse position).
* `_Code.Player.WatcherManager` + `Watcher` + `AMarker` subclasses: stack of `EWatcherState` (World3d, Room, Dialog, Phone,
  Pause, Settings, MainMenu, Movie, ConsumableController, Radio, Credits, Popup, Peephole, Window, CollectionEndings,
  CollectionCharacters, Dream). `EnterState/LeaveState/ChangeInput`. Decides cursor visibility per device and gamepad
  focus. **Best single hook for "what mode is the game in"** when choosing VR presentation.
* `_Code.Infrastructure.Cursor.CursorController` (`ICursorController`): `Lock()`, `Unlock()`, `SetType(ECursorType)`,
  `SetThrough(bool)`, `Reset()`, events `Locked`/`Unlocked`. Cursor is hidden + locked in the hallway.

## 1.9 Interaction

**3D hallway: centre-of-view raycast.**
* `_Scripts.Raycast.RaycastSource` (on `Player`): `_camera` = Main Camera, `_maxRayDistance` **1** (metre), `_layerMask` 1
  (Default), `Target` / `_currentTarget`; `Update()` raycasts from the camera. For VR hand pointing, swap `_camera`
  or patch `Update`.
* `_Scripts.Raycast.ARaycastTarget` (on a BoxCollider child named `RaycastTarget`/`RaycastTrigger`): `OnFocused()`,
  `OnLostFocus()`, `OnTargetedCorrectConditions()`, `OnTargetedWrongConditions()`, `IsTargeted`, `IsLocked`,
  `SetLockedState(bool)`, `HardConditions`, `LinkedActionableObject`. Subclasses: `_Code.Raycast.RaycastTargetHint`
  (shows the HUD hint via `IHUDPresenter.ShowHint(string subject, string action, Transform target, ERaycastHintIcon)`),
  `RaycastTargetUnityEvent`.
* **Look-at objects** `_Code.Infrastructure.ActionableObjects.AActionableObjectView` - subclasses `DoorTrigger` (6 doors),
  `PeepholeTrigger`, `WindowBlindsTrigger`, `WindowCurtainsTrigger`. Each has a trigger BoxCollider (stand zone),
  `_raycastTarget`, `_lookAtPos`, `_standingPos`, `_fov` (30 for the peephole), `_transitionTime`, `_awaitedPlayerLookAngle`.
  Methods: `Act()`, `StartLooking()`, `StopLooking()`, `TryLeave()`, `SetLockedState(bool)`, `Enable()`, `Disable()`,
  `OnUpdateAction()`; hooks `ExtraActionIn/Out`, `ExtraActionInE/OutE`. On use the player is slid to `_standingPos`
  (`IPlayerService.MoveXZ`), the camera is forced onto `_lookAtPos` with a FOV zoom (`LookAtWithZoom`), then the
  room / window / peephole view opens **(sequence inferred, fields verified)**. Managed by `ActionableObjectsManager`.
* **Press-to-use objects** `_Code.Infrastructure.AInteractableObject` (`Interact()`, `HardConditions`, `SoftConditions`,
  `EnergyCost`, `Enable/Disable`): `PhoneInteractable`, `RadioInteractable`, `CigaretteInteractable`, `SaveInteractable`,
  `CalendarInteractable`, `CatInteractable`, `DialogInteractable`, `HatchInteractable`, `MushroomInteractable`,
  `TheHoleInteractable`, `WindowBoardsInteractable`, `ZoomInteractable`, `EndingLaunchInteractable`. Managed by
  `InteractablesManager`; triggered by `InputHandling.InteractTriggered`.
* Walk-in volumes `_Code.Infrastructure.TriggerObjects.ATriggerObject`: `TriggerObjectGoToLocation`, `...OpenDoor`,
  `...RunZone`, `...CrouchZone`, `...CloseScene`, `...FollowLight`.

**2D rooms: mouse over sprites.**
* `_Code.Infrastructure.Rooms.RoomDisplayer` (on the `RoomDisplayer` canvas): fields `_raycaster` (GraphicRaycaster),
  `_eventSystem`, `_camera`, `_selectedButton`; methods `Update()`, `Hover()`, `Unhover()`, `LockClicks()`, `UnlockClicks()`,
  `SetBlockerActive(bool)`, `OpenFadeLocal/OpenFadeGlobal/CloseFade`, `SetData(...)`.
* `_Code.Rooms.UIButton` (on each clickable Image): `Click()`, `OnHover()`, `OnUnhover()`, `SetAction(Action)`,
  `IsMousePosInSpriteArea(Vector3 position, Camera cam)` (per-pixel alpha test, static `AlphaThreshold`), static
  `AreButtonsEnabled`, `_outlineMaterial`. A VR pointer must supply a screen position for the same camera, or call
  `Click()` directly.
* `_Code.Rooms.UIRayCaster` (`Instance`, `Raycast()`) exists but no instance was loaded while in the hallway.
* Rooms logic: `RoomsManager` (`IRoomsManager`), `ARoom`/`IRoom` (`Enter()`, `Leave()`, `AddCharacter`, ...), views `ARoomView`.

**Dialogs:** `_Code.DialogSystem.DialogManager` (`IDialogManager`) and `DialogView`:
`StartDialog(CharacterSOData, string nodeName, EDialogOverlayType, Camera cam, DialogViewData, bool hideCharacter)`,
`SkipLine()`, `EndDialog()`, `ShowSign(CharacterSOData, ECharacterSign)`, `StopShowingSign()`, `SetupOverlay(...)`,
`ShowSubtitle(...)`, `EnableCrtShader()`/`DisableCrtShader()`, `LastUsedCamera`. Answer buttons: `HoverableButton`.

**Close-ups:** `CloseUpsController` (`ICloseUpsController`: `Fridge`, `Phone`, `Radio`, `Consumable`, `Mushroomlist`,
`IsAnyCloseUpActive`), views derive from `ACloseUpView` (`Show()`, `Hide()`, `OnShown()`, `OnHidden()`, `IsEntered`,
hold-to-close via `_holdProgress`).

**Menus:** `MainMenuController`/`MainMenuView` (s0), `PauseController`/`PauseMenuView` (`IPauseController`),
`SettingsManager` + `ScreenSettings`/`SoundSettings`/`ControlSettings`/`TextSettings`, `PopupWindow`, `NotepadController`/`NotepadView`,
`GachaWindow` (collection), `EndingShower`/`EndingView` (video endings), `CutscenesManager`, `TitlesController` (s5).
Scene changes: static `_Code.Infrastructure.Scenes.ScenesChanger.OpenScene(int index)`.
Everything is wired by Zenject installers: `CommonInstaller`, `MainSceneInstaller` (s0), `GameplaySceneInstaller` (s1),
`StrangeMorningSceneInstaller` (s4), `TitlesSceneInstaller` (s5).

## 1.10 Post-processing and camera effects

* URP assets: `URP-HighFidelity` (active; HDR, MSAA 4x, shadows 4096, LDR colour grading), `URP-Balanced`, `URP-Performant`.
  Renderer `URP-HighFidelity-Renderer`: Forward, intermediate texture Always, features **SSAO**,
  **FullScreenPassRendererFeature** (material `Post2`, AfterRenderingPostProcessing, inactive by default; most likely the
  CRT effect toggled by `DialogView.EnableCrtShader` **(inferred)**), **DecalRendererFeature**.
* One global `Volume` in s1: `Game view/[POSTPROCESS] Volume`. `EffectsController`/`EffectsViewProvider` swap its profile:
  * `Night`: ColorCurves, Bloom, Tonemapping, Vignette, ChannelMixer, LensDistortion
  * `Day`: ColorCurves, Bloom, ShadowsMidtonesHighlights, SplitToning, Tonemapping, Vignette, ColorAdjustments, LensDistortion
  * `PreDeath` (same set as Night), `Death` (Day set + FilmGrain + DepthOfField) through `Location.OverrideVolumeProfile`
    (locations can also override skybox and fog).
  * s4: `BodyEater` (ColorCurves, Bloom, SplitToning, Vignette, LensDistortion); s6: `MushroomDream` (Vignette,
    ColorAdjustments, ShadowsMidtonesHighlights).
* `PeepholeTrigger` animates `LensDistortion` (`_distortionScale` 0.5) for the fisheye look.
* Cinemachine Perlin noise = head bob on the normal/crouched vcams (`UpdateNoiseAmplitude`).
* FOV zooms via `PlayerService.LookAtWithZoom/ResetFov`. Full-screen fades via `HUDView.FadeOverlay` (`ColorOverlay` Image).
* Main Camera has post-processing on, no camera AA. Menu/credits/s3 cameras have post-processing off.
* The Post Processing v2 assembly is in the build but no PPv2 components were found in any scene.
* VR concerns: LensDistortion, Vignette, head bob, forced camera rotation/zoom, FilmGrain/DoF in `Death`.

## 1.11 Hand sprites

There is **no hand cursor and no set of hand-state sprites** (open / point / grab / hold). The mouse cursor is a face
(`CursorSOData 'CursorData'`: `face_cursor2` Normal, `face_cursor` Hover, 32x32 Texture2D). Hand art exists in three forms:

**A. First-person hand animations (best source for VR hands).** Full-screen 1920x1080 frames, colour, hands with blue
sleeves entering from the bottom edge, drawn from the player's viewpoint. Data: `HUDView._animationsData`
(`HUDAnimationData[]`: `AnimationType` (`EHUDAnimation`) + `AnimationData` {`Frames` (`Sprite[]`), `FramesPerSecond`,
`CyclingType`}). Played by `IHUDPresenter.PlayAnimation(EHUDAnimation)` -> `HUDView.PlayAnimation` on
`Canvases/HUD_Camera/AnimationsLayer` (`Image` + `_Code.Utils.UI.ImageAnimating.AnimatedImage`: `SetData(AnimationData)`,
`StartPlayingAnimation()`, `StopAnimation()`).

| `EHUDAnimation` | Sprites | Frames @ fps | Sheet texture | What the hands do |
|---|---|---|---|---|
| WatchCleanHands | `fake_hands1_0..15` | 16 @ 12 | `fake_hands1` 7680x4320 | **both empty hands** raised and turned (left hand left, right hand right, fingers up) |
| WatchDirtyHands | `fake_hands2_0..18` | 19 @ 12 | `fake_hands2` 9600x4320 | same, dirty variant |
| SmokeCigarette | `fake_cigarettes_0..28` | 29 @ 12 | `fake_cigarettes` 11520x5400 | right hand, bottom right, holding a cigarette (pinch grip) |
| DrinkBeer | `fake_beer_0..16` | 17 @ 12 | `fake_beer` 9600x4320 | hold item |
| DrinkKombucha | `fake_kombucha_0..12` | 13 @ 12 | `fake_kombucha` 7680x4320 | hold item |
| EatMushroom | `fake_mushroom_0..13` | 14 @ 12 | `fake_mushroom` 7680x4320 | hold item |
| OpenTin | `fake_tin_0..19` | 20 @ 12 | `fake_tin` 9600x4320 | hold item |
| CapturePhoto | `fake_photo_0..15` | 16 @ 12 | `fake_photo` 7680x4320 | hold item |
| ThrowPovistka | `fake_postcard_0..17` | 18 @ 12 | `fake_postcard` 9600x4320 | hold item |
| TakeNaperdysh / HoldNaperdysh / ReleaseNaperdysh | `fake_Naperdish_0..3` / `_3` / `_4..12` | 4 @ 8 / 1 / 9 @ 9 | `fake_Naperdish` 3840x7560 | pick up / hold / release the cat |
| GunShot | `fake_gun shoots_0..6` | 7 @ 12 | `fake_gun shoots` 2048x1104 | see B |

Sheets are regular grids of 1920x1080 cells, **not** packed atlases (`packed=False`, no rotation). Frame `n` of a sheet
`W x H`: `cols = W/1920`, `col = n % cols`, `row = n / cols`, pixel rect `(col*1920, H - (row+1)*1080, 1920, 1080)`
(row 0 is the top row); UV rect = that divided by `(W, H)`. Verified: `fake_cigarettes_0` = (0, 4320, 1920, 1080) on
11520x5400; `fake_Naperdish_3` = (1920, 5400, ...) on 3840x7560. Each frame is mostly transparent; the tight
`textureRect` (e.g. 424x266 for `fake_cigarettes_0`) is where the hand is.

**B. Gun.** `_Code.Menues.Gun` (on `Canvases/HUD_Overlay`, Image `Canvases/HUD_Overlay/Gun`): `_loadSprites`
`fake_gun idle_0..5` (sheet `fake_gun idle` 2048x736, cells 682x368, 3x2) and `_shotSprites` `fake_gun shoots_0..6`
(2048x1104, 3x3). Two hands holding a double-barrel shotgun, entering from the bottom right, barrel pointing up-left.
Shown/hidden by `IHUDPresenter.GunShow()/GunHide()` between `_hiddenPosition` (425,-650) and `_loadedPosition` (425,-250).

**C. "Show me your hands" inspection image.** One standalone Texture2D per character, **820x464**, sprite rect = whole
texture, pivot centre, 100 ppu, not in an atlas. It depicts **both hands in one image**: backs of the hands toward the
viewer, **fingers pointing up**, wrists cut off by the bottom edge, left hand on the left / right hand on the right
(thumbs toward the centre), grayscale dithered. No separate left/right art.
* Player: `CharacterSOData 'Player'` (`_characterType == ECharacterType.Player`): `_handsSpriteHuman` = `mainchar_human_hands`,
  `_handsSpriteImposter` = `mainchar_fake_hands`. Which one applies: `PlayerSigns.IsHandsFake` (`IPlayerService.Signs`).
* NPCs: `human_hands_<n>`, `fake_hands_<n>`, `other_hands_1` in each `CharacterSOData` (`_handsSpriteHuman`,
  `_handsSpriteImposter`; property `HandsSprite` picks by imposter status **(inferred)**).
* Display path: `DialogView.ShowSign(CharacterSOData, ECharacterSign.Hands)` -> `IDialogUI.ShowHands(Sprite)` /
  `HideHands()` -> `DialogSignsView._handsContainer` (`DialogSignElementViewMovingContent`, slides in) at
  `Logic/Dialogue System/Canvas/Line View/HandsContainer`: `HandsBack` (Image `fake_hands back` 832x478 + `Mask`) >
  `HandsContent` (Image, the hands sprite), plus `HandsFront` (Image `fake_hands frame` 832x478).

**Loading at runtime without bundling anything** (all of these are plain serialized references that Unity loads with
the scene; nothing goes through Addressables or `Resources.Load`):
```csharp
// A/B: needs s1_GameScene loaded
var hud = Resources.FindObjectsOfTypeAll<_Code.Menues.HUD.HUDView>()[0];
foreach (var d in hud._animationsData)
    if (d.AnimationType == EHUDAnimation.WatchCleanHands) { Sprite[] frames = d.AnimationData.Frames; /* frames[i].texture, .rect */ }
// C: CharacterSOData assets are loaded in s0 (verified) and referenced by s1's providers
foreach (var so in Resources.FindObjectsOfTypeAll<_Code.Characters.CharacterSOData>())
    if (so._characterType == ECharacterType.Player) { Sprite hands = so._handsSpriteHuman; }
```
Use the `Sprite`/`Texture2D` reference directly (material main texture + UV rect from `sprite.rect`); do not read
pixels or write files. The textures may be non-readable; DevTools previews them through `Graphics.Blit`, which
also confirmed they are stored upright (no flip). Field/property names above are the exact interop names; the generic
`FindObjectsOfTypeAll<GameType>()` call itself was not run in recon (DevTools filtered by IL2CPP type name instead).

## 1.12 Not done / open questions

* Peephole view, a window view and a 2D room were not opened at runtime (structure comes from the dump of inactive
  objects). Worth one sandboxed play-through with `shot`/`dump` once the VR chat needs exact distances.
* `s2_Loading` hierarchy not dumped.
* Method bodies are unknown (see issue 5). If exact logic is needed, Cpp2IL's ISIL/pseudo-code output on
  `GameAssembly.dll` is the next step.
* No VR code written; `NIVR.Plugin` is only the hello-world skeleton.

---

# 2. VR core: OpenXR, stereo rendering, head tracking (chat 2, 2026-10-02)

**Owner: chat 2. Directory: `C:\VRMod\plugin\Core\` (do not edit from other chats; ask for API changes).**
Output: `NIVR.Core.dll` + `openxr_loader.dll` (Khronos loader, Apache-2.0, copied from SteamVR; not a game file) in
`game_copy\BepInEx\plugins\NIVR\`. Plugin GUID `nivr.core`. `src\NIVR.Plugin` (hello world) is still unowned and unused.

Build: `.\plugin\Core\build.ps1` (`scripts\build.ps1` only builds `src\`), then `.\scripts\run.ps1 -NoBuild`.
Other modules: add `<ProjectReference Include="..\..\plugin\Core\NIVR.Core.csproj" Private="false" />` and
`[BepInDependency("nivr.core")]`.

## 2.1 Status

| What | State |
|---|---|
| Stereo render of menu + hallway into two eye textures, post-processing intact | **verified** in the simulator backend (eye captures in `logs\shots\hall_L.png` etc.) |
| Head offset on top of the game camera, no drift, scripted yaw followed | **verified** (simulated head pose; base pose stays put, player turn of 30 deg moves the rig 30 deg) |
| Mouse-look blocked while VR is active | **verified** (controlled test: patch off = camera turns, patch on = it does not) |
| Desktop window unchanged (layout, mouse UI) | **verified** |
| Flat screen in VR for menus/2D views | **verified** in the simulator |
| OpenXR instance on SteamVR, "no headset" fallback to flat | **verified** (`XR_ERROR_FORM_FACTOR_UNAVAILABLE`, game stays flat, retries every 10 s) |
| OpenXR session, swapchain copy, frame submission, controller actions | **NOT yet run** (no headset was connected). Written, struct layouts checked by hand against the spec. First headset test pending. |

## 2.2 How it works (so nobody has to rediscover it)

* **No Unity XR plugin.** The build has no XR provider and its XR shader variants are stripped, so Core talks to OpenXR
  directly (`OpenXR\Xr.cs`, hand-written P/Invoke, extension `XR_KHR_D3D11_enable`) on Unity's own D3D11 device
  (reached through `Texture.GetNativeTexturePtr()` -> `ID3D11DeviceChild::GetDevice`).
* **Rendering = multi-pass by hand.** One extra disabled camera `NIVR Eye Camera` is `CopyFrom`'d from the game camera
  every frame (plus its URP camera data) and rendered twice with `Camera.Render()` into `NIVR Eye 0/1` (sRGB, MSAA),
  with the headset's asymmetric projection. No stereo shader keywords needed. Each eye is blitted (MSAA resolve + Y flip)
  into `NIVR Submit 0/1`, whose D3D texture is copied into the OpenXR swapchain image **on the render thread**
  (`GL.IssuePluginEvent`), followed by `xrBeginFrame`/`xrEndFrame` there. `xrWaitFrame` runs in Core's `Update`.
* **Pre-render hook = `Canvas.willRenderCanvases`** (after all LateUpdates incl. Cinemachine, before rendering).
  `Application.onBeforeRender` is stripped ("Method unstripping failed" in `BeforeRenderHelper`), and a Harmony prefix
  on `RenderPipelineManager.DoRenderLoop_Internal` is already inside the render loop (`Camera.Render()` there gives
  "Recursive rendering is not supported in SRP").
* **Rig.** `NIVR Rig` (DontDestroyOnLoad) = tracking-space origin. Each frame: base = the pose the game gave
  `Camera.main` (Cinemachine result), origin = base (yaw only by default) minus the recenter offset, head/eyes on top.
  If the camera has an enabled `CinemachineBrain`, Core then leaves **the game camera on the head pose** until the brain
  overwrites it next LateUpdate: desktop view, AudioListener and `RaycastSource` (centre-of-view interaction) follow the
  head for free. Cameras without a brain (menu, s3 `CameraRotate`) are never moved.
* **Game camera is otherwise untouched** and still renders the desktop window. Do not reuse it for eye renders:
  changing its `targetTexture` re-lays-out every ScreenSpaceCamera canvas to the eye size and breaks the desktop UI
  (tried, reverted).
* **Flat screen** (`FlatScreen.cs`): while the game has the cursor unlocked (menu, rooms, dialogs, pause, close-ups) the
  finished desktop frame (`ScreenCapture.CaptureScreenshotIntoRenderTexture`, works) is shown on a 2.6 m quad 2 m in
  front of the player, with a red dot for the mouse position. Rendered only by the eye camera. This is a stop-gap so
  the game is usable in the headset before the UI chat re-hosts canvases; turn it off with `VRRig.FlatScreenMode = VRFlatScreenMode.Off`.
  In the hallway (cursor locked) it is hidden, so **overlay HUD (hints, energy pips) is not visible in VR yet**.

## 2.3 API for other modules (`namespace NIVR.Core`)

```csharp
VRRig.IsActive            // stereo rendering on (headset session running, or simulator)
VRRig.IsSimulated
VRRig.Head                // VRHead: .Transform (world), .LocalPosition/.LocalRotation (tracking space), .IsTracked
VRRig.LeftController / RightController / Controller(VRHand)
    // VRController: .IsConnected .IsTracked .Aim .Grip (world Transforms, ray along +Z of Aim)
    //               .Trigger .GripValue (0..1) .Stick (Vector2)
    //               .GetButton/GetButtonDown/GetButtonUp(VRButton)  .Haptic(amplitude, seconds, frequency)
    // VRButton: Trigger, Grip, Primary (A/X), Secondary (B/Y), Menu (left only on Touch), Stick (click),
    //           StickLeft, StickRight, StickUp, StickDown   (analog ones: pressed > 0.6, released < 0.4)
VRRig.Origin              // Transform of the tracking space (parent of head + controllers; scale = 1/WorldScale)
VRRig.GameCamera          // the game camera being mirrored into VR (Camera.main)
VRRig.GameCameraPosition / GameCameraRotation   // where the game wants the camera, before head tracking
VRRig.GetEyeTexture(0|1)  // last eye image, upright
VRRig.Recenter()
VRRig.FlatScreenMode      // null = config, or Auto / Always / Off
event VRRig.ButtonPressed / ButtonReleased (VRHand, VRButton)   // raised in Core's Update
event VRRig.Recentered, VRRig.ActiveChanged(bool)
VRConfig.*                // the BepInEx config entries (read-only for other modules)
```
Poses are updated in Core's `Update` (predicted display time) and refreshed right before rendering. Read them from
`Update`/`LateUpdate`; never move, reparent or destroy `NIVR Rig`, its children, `NIVR Eye Camera` or `NIVR Flat Screen`.

**Other chats must not:** patch `PlayerController.HandleRotation` (Core's Harmony prefix skips it while VR is active),
change `Camera.main`'s transform/projection/targetTexture, touch `QualitySettings.vSyncCount` /
`Application.targetFrameRate` / `runInBackground` while `VRRig.IsActive` (Core forces 0 / -1 / true so the headset paces
frames), re-enable the volume overrides listed below, or call OpenXR themselves. To turn the player use
`PlayerController.AddControlYawInput(degrees)` (verified: adds exactly that many degrees of body yaw).
Core already binds **right stick left/right = snap turn** (`SnapTurnDegrees`, 0 = off); a locomotion module that wants
its own turning should set that to 0 in its docs or ask.

## 2.4 Config (`game_copy\BepInEx\config\nivr.core.cfg`)

`[General] Enabled, Backend (Auto|OpenXR|Simulator)` - `[Tracking] Mode (Seated|Standing), SeatedHeightOffset,
StandingEyeHeight (1.65), WorldScale, IpdOverrideMm (0 = headset), RigRotation (YawOnly|Full), RecenterKey (F8),
RecenterWithThumbsticks (hold both sticks 0.5 s), SnapTurnDegrees (30)` - `[Rendering] RenderScale, MSAA (4), NearClip
(0.05), FarClip (0 = game), FlipY, Mirror (GameCamera|LeftEye), FlatScreen (Auto|Always|Off), FlatScreenDistance,
FlatScreenWidth` - `[Comfort] DisableMouseLook, DisableHeadBob, DisableCameraShake, DisableVignette,
DisableLensDistortion, DisableDepthOfField, DisableMotionBlur, DisableChromaticAberration (all true), DisableFilmGrain
(false)` - `[Debug] CommandDir, Verbose`.

Comfort implementation: volume components are switched off by setting `VolumeComponent.active = false` on every loaded
`VolumeProfile` (rescanned once a second, restored when VR goes inactive; 14 components in s1). Head bob:
`PlayerController.cameraNoiseAmplitudeMultiplier = 0`; shake: `AmplitudeGain = 0` on every
`CinemachineBasicMultiChannelPerlin` each frame. FOV zooms (`LookAtWithZoom`) have no effect on the eyes because the
projection comes from the headset.

## 2.5 Dev tools in Core

* **Simulator**: `Backend = Simulator` renders two fake eyes (1440x1600, 100 deg) with no headset. Use it to test
  anything visual without asking the user to put the headset on.
* **Command channel**: with `[Debug] CommandDir = C:\VRMod\logs\cmd`, drop a `*.vr` file there (DevTools owns `*.cmd`):
  `status` | `eyes <name>` (-> `logs\shots\<name>_L.png`, `_R`, `_submitL`, `_flat`) | `recenter` |
  `simhead x y z [yaw pitch roll]` | `turn <deg>` | `mirror GameCamera|LeftEye` | `mouselook on|off` | `flat Auto|Always|Off`.
* Core's lines before DevTools loads are only in `game_copy\BepInEx\LogOutput.log` (Core loads first), the rest also in `logs\nivr.log`.

## 2.6 Gotchas found

1. `[UnmanagedCallersOnly]` does not compile here: the interop `UnityEngine.CoreModule` declares its own
   `System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute` (CS0433). Use a delegate + `Marshal.GetFunctionPointerForDelegate`.
2. Injected icalls (`*_Injected`) take the **native** object: `obj.m_CachedPtr`, not `obj.Pointer` (that one crashes coreclr with 0xc0000005).
3. `Resources.FindObjectsOfTypeAll<T>()` with game/URP/Cinemachine types works through the interop (answers the open point in 1.11).
4. SteamVR's OpenXR runtime: `xrCreateInstance` succeeds without a headset, but `xrStringToPath` then fails with
   `XR_ERROR_RUNTIME_FAILURE` and `xrGetSystem` with `XR_ERROR_FORM_FACTOR_UNAVAILABLE`; SteamVR is not left running.
   Core therefore creates actions only after a headset is found and recreates the instance every third retry.
5. Game frame rate is uncapped in the menu even with vSyncCount 1 (about 800 fps windowed); three renders per frame in
   the hallway still ran at about 280 fps on this PC (1440x1600 x2 + desktop).
6. Entering s1 logs five DOTween "Target or field is missing/null" warnings; not checked whether they also occur without Core **(unverified)**.

## 2.7 Known limits / to do

* First real headset run pending (see 2.1). If the image is upside down set `FlipY = false`.
* Only `Camera.main` is mirrored; `PeepholeCam` (second 3D camera) is not handled **(inferred to be broken in VR)**.
* ScreenSpaceCamera/Overlay canvases are not in the eye render at all (only via the flat screen). UI chat: re-host
  them, then switch the flat screen off.
* The post-processing of the hallway is applied a second time to the flat screen quad (slightly darker UI).
* Physics still steps at 50 Hz; movement may judder at 72-120 Hz headset rates (not addressed).

## 2.8 Second pass after the first headset test (chat 2, 2026-10-02)

First headset run (Quest via Virtual Desktop, SteamVR OpenXR 2.17.10, 3040x3228 per eye, swapchain format 29): the
whole OpenXR path of 2.1 worked first time (**verified by the user**: height, 3D hallway, head tracking, snap turn all
good). User feedback led to the changes below. Everything in this section is **verified in the simulator only**
(eye captures `logs\shots\r1_L.png`, `p4_R.png`, `w7_L.png`, `k0_L.png`), not yet in the headset.

Direction agreed with the user: **controller-first** (gamepad-style input + head tracking + 3D); hand models and
hand interaction are not being built for now.

* **Controllers = a gamepad** (`VirtualGamepad.cs`). An Input System `Gamepad` device ("NIVR VR Controllers") is added
  and fed with hand-built `StateEvent`s (`InputSystem.QueueEvent(InputEventPtr)`; the generic `QueueStateEvent<T>` is
  not callable from a plugin). The game's own "Gaypad" scheme then does everything: left stick walks (verified, 2 m/s),
  A / right trigger = South (interact, select), B = East (back), X / left trigger = West (dialog skip), Y = North,
  left Menu = Start (pause), stick clicks, grips = triggers + shoulders (run / speed-up, radio handles). The right stick
  is the game's menu cursor when the cursor is free and Core's snap turn when it is locked.
  `InputSettings.backgroundBehavior` is set to `IgnoreFocus` while feeding. Config `[Input] ControllerAsGamepad`.
  **Other chats: do not patch `InputHandling` getters for controller input; use this device or VRRig.**
* **Screen UI in VR** (`UiCapture.cs` + `FlatScreen.cs`), replaces the desktop-capture screen of 2.2:
  * Every ScreenSpaceOverlay canvas is switched to ScreenSpaceCamera on the game camera (sortingOrder + 10000,
    layer UI) while VR is active; layout and mouse behaviour are unchanged. Restored when VR goes inactive.
  * The game camera is rendered one extra time per frame with only the UI layers and a transparent background into
    `NIVR UI` (window size, so canvases do not re-layout; HDR and post off so alpha survives). `RoomDisplayer`
    (room illustrations) and its nested `*WindowView` canvases are moved to layer 31 and rendered into `NIVR Room`.
    Nested canvases draw with **their own** GameObject layer. Real world-space canvases go to layer 30 (kept out of both).
    Layers 30/31 are added to the game camera's culling mask.
  * `NIVR UI` is shown on a quad 1.5 m away (2.1 m wide): a HUD that lazily follows the head while the cursor is locked
    (hints, subtitles, button prompts now visible in the hallway), fixed in front of the game camera pose otherwise.
  * `NIVR Room` is shown on a quad along the game camera's view at `RoomDistance` (3 m) for rooms and `WindowDistance`
    (10 m) for windows, depth-tested: the game really opens the 3D door / parts the blinds, so the picture is seen
    **through the doorway / between the slats** with head parallax. Same view angle as the UI quad, so a screen
    position is the same direction on both.
  * UI sharpness in VR = desktop window resolution. Launch with `run.ps1 -Width 1920 -Height 1080` for testing.
* **Peephole**: `PeepholeTrigger._peepholeCam` (a second camera drawing street + visitor + dialog + fisheye over the
  main one) gets a window-sized target texture while VR is active; that picture goes head-locked (`PeepholeFov` 90
  deg) into `PeepholeEye` (Right), the other eye is black. Post effects are left on while it is active
  (`Comfort.KeepPostEffects`). The desktop gets the same picture blitted. HUD button prompts are not composited into it.
* The game camera is moved onto the head only while the cursor is locked; in flat views it stays on the game's pose
  (canvases are laid out for that pose).
* New debug commands (`*.vr`): `pad lx ly [rx ry [buttonBits]]` / `pad off` (South = 64, East = 32), `tp x y z yaw`,
  `objects`, `act <index>`, `vr off|on`, and `eyes` now also saves `_ui`, `_room`, `_submitR`.
* Findings: opening a room is `cursor = Confined`, walking is `Locked`, window views and the peephole keep `Locked`.
  In the current save the Kitchen and BigRoom doors refuse (X hint), Bedroom opens. `RoomDisplayer._isOpened` = a room
  is shown. `AddControlYawInput`, `Character.TeleportPosition/TeleportRotation` work from a plugin.

Still open: hand models / hand interaction (deliberately not started); close-ups (phone, fridge, radio), dialogs in
rooms, pause menu and dream scenes were not looked at individually in VR (they go through the same UI layer, so they
should appear on the UI quad); peephole picture's vertical placement on asymmetric headset FOV is unverified.

## 2.9 Third pass after the second headset test (chat 2, 2026-10-02)

User: everything works, but the crosshair was inconsistent, doors sometimes needed several approaches, the bathroom
showed the hallway from "inside", and room pictures floated in black. Changes (**simulator-verified only**:
`logs\shots\u2_L.png`, `t3_L.png`; bedroom opened by gaze + A with the game window in the background):

* **Root cause of the flaky crosshair / doors: `RaycastSource` builds its ray from the mouse position through
  `_camera`.** The mouse is only at the screen centre while the game window has focus and the cursor is really locked;
  with a headset on the window usually has no focus, so the ray went wherever the OS cursor was. Verified:
  `_currentTarget` is null in the background and set the moment the window is focused.
  Fix (`[Tracking] GazeRayFromHead`, default on): `RaycastSource._camera` is replaced by `NIVR Head Camera`, a disabled
  camera on the head with a 0.1 degree field of view, so every screen position maps to the head's forward ray.
  **Any test of interaction in the simulator must keep this on, or focus the game window.** Doors themselves:
  `_canBeOpenedByEKey` false, `_awaitedPlayerLookAngle` 90, not locked; they open on Interact while the ray is on
  `Door*/RaycastTarget` (a trigger box about 0.3 m in front of the standing position, ray length 1 m).
* **Body follows head** (`[Tracking] BodyFollowsHead`, default on; only with RigRotation = YawOnly, cursor locked, not
  `_isLookingAt`): the tracking space keeps its own world heading (`_originYaw`), and every frame the player is steered
  to the head: `Character.SetYaw(head yaw)`, `cameraTarget.localRotation = head pitch`. The game camera, walking
  direction (verified: left stick walks where you look) and HUD projection all agree with the headset, and nothing the
  game does to its camera can rotate the world while walking. Snap turn now turns `_originYaw`. When the game takes
  the camera (door, window) the rig hands over without a jump and follows the scripted move; since the body already
  faced where the head looked, that move is small (3 degrees in the test).
  **Other chats: do not write the Player's yaw or `Camera Target` rotation while the cursor is locked.**
* **HUD panel while walking** = the game's own screen put back at the game camera pose with the game camera's field
  of view (so screen-projected markers sit on the objects), no longer a lazily following panel.
* **Rooms** (`[Rendering] RoomEnclosure`, default on): the room picture is the front wall of a closed box around the
  player (left/right/floor/ceiling quads show a 1.5 % edge strip of the picture stretched along the wall, back wall
  black), drawn over the house with ZTest Always, and `RoomViewDegrees` (105) wide. No black void and no view of the
  hallway from inside a room. `RoomEnclosure = false` gives the previous "picture through the doorway". Windows are
  unchanged (quad behind the blinds).
* Debug commands added: `follow on|off`, `ray main|head`; `tp` sets the tracking heading in follow mode.
* Dead end: Harmony postfix on `PlayerService.LookDirection` is never called for door checks (removed).
* Not verified: the bathroom (door would not open in the simulator session), the enclosure with dialogs/characters in
  a room, comfort of the head-steered HUD in the headset.

### 2.10 Fourth pass (door marker, window views) - 2026-10-02

User: crosshair "always rotated one way", invisible at the window by the entrance door; wanted window views curved
around the player (window model in front, no non-picture visible at any angle, little warping).
* **The "crosshair" is the game's door marker** `HUD_Overlay/ToOpen` (`_Code.Menues.OpenRoomView`: `_crosshair` = an
  X sprite at screen centre, `_box` = "<Room> / Open (A)" box at the target). Its `Update()` assigns **screen pixels
  to `Transform.position`** (valid only on an Overlay canvas, where world space = pixel space). On our converted
  ScreenSpaceCamera canvas the box landed at anchored (62961, 551080) and the X turned edge-on (a 3 px sliver).
  Fix (`UiCapture.ApplyPatches`): Harmony prefix/postfix on `OpenRoomView.Update` puts the canvas root on the overlay
  pixel frame (pos = screen centre, rot identity, scale = scaleFactor) while it runs, then restores it; the local
  values it writes are right in both frames. Verified in sim: X flat at the gaze point, box beside it.
  **Any other script that positions overlay-canvas elements in pixels needs the same patch.**
* Walking HUD panel is now centred on the real head pose (position + yaw/pitch, no roll) instead of the game camera
  pose, so the X sits on the interaction ray even when the head is off the recenter point. Scripted looks keep the
  game camera pose.
* **The curtains window (by the entrance) opens the RoomDisplayer too** (`_isOpened` true + `CurtainsWindowView`
  active), so it got the room box (ZTest Always) drawn over the window frame. Active `*WindowView` children are now
  checked first.
* **Window dome** (`[Rendering] WindowDome` true, `WindowViewDegrees` 110): window view on a sphere section (radius
  `WindowDistance`, +-120 deg yaw, +-88 deg pitch) centred on the game camera, angle-linear UVs (picture spans 110 deg
  x 110/aspect), beyond the picture: mirrored sideways (`wrapModeU = Mirror`), sky/ground clamped up/down
  (`wrapModeV = Clamp`; vertical mirror put a second horizon in the sky). Material = URP `Universal Render
  Pipeline/Unlit`, queue 1990, Cull Off, depth-writing, so the frame/blinds/curtains (some transparent while
  highlighted) draw over it. Verified in sim at the curtains window looking straight, up-right and down-left.
* Sim gotcha: `tp` into a spot without floor drops the player out of the map (y -463); walk with `pad` instead.
  The interaction ray reaches only 1 m (`RaycastSource._maxRayDistance`).
* Not verified: blinds windows with the dome (same code path), comfort in the headset.

### 2.11 Fifth pass (controller pointer, peephole, close-ups) - 2026-10-02

User: peephole disorienting / picture not fully visible; cursor should follow controller pointing (laser in the game's
style); crosshair too high, should be where the pointer points, pointer only while interacting; radio knobs did not
move; check phone / radio / fridge.
* **Input modes** (`VRCoreBehaviour.CurrentInputMode`, from `WatcherManager` top state via `GameState`):
  Walk (World3d + cursor locked), Gamepad (Radio, Peephole, Window, Movie, Dream), Pointer (everything else: rooms,
  dialogs, phone, fridge = ConsumableController, pause, settings, main menu). Unknown state -> cursor lock decides.
* **The game sets `PlayerInput.neverAutoSwitchControlSchemes`**, so the scheme active at start stays (in the
  simulator it was Keyboard And Mouse forever). `UiPointer.EnsureScheme` now switches it explicitly with
  `SwitchCurrentControlScheme`: Gaypad + virtual gamepad in Walk/Gamepad, "Keyboard And Mouse" + **virtual mouse
  ("NIVR Pointer") and virtual keyboard ("NIVR Keys")** in Pointer mode. Devices fed with hand-built STAT events
  (offsets from `control.stateBlock`, size from `stateBlock.sizeInBits`; the interop has no `sizeInBytes`).
  Pointer mapping: pointing hand's trigger or A = LMB, B = Q (exit/cancel), X = Space (skip line), Y = F (tutor),
  Menu = Escape (pause; in a room Escape = leave room), R grip = Shift (speed up), left stick = WASD, right stick y =
  scroll. Glyphs in pointer screens are therefore keyboard keys.
* **Legacy `Input.mousePosition` is live and some screens use it** (fridge hover). `MoveSystemCursor` (default on)
  warps the real OS cursor (`Mouse.WarpCursorPosition` on the native mouse) to the pointer position.
* **`ButtonGate`**: buttons held across a mode change are ignored until released (Menu opened pause as Start, then
  closed it again as Escape; a click on "Continue" would otherwise also interact).
* **Laser**: `LineRenderer` (UI/Default, ZTest Always, teal 0.36/0.95/0.86, 3.5 -> 1.5 mm), on layer 29
  (`UiCapture.VrOnlyLayer`, added to the eye camera mask; the game camera's "Everything" mask shows it on the desktop
  too). Pointer mode: laser to where it meets the screen panel (`FlatScreen.PanelHit`), teal dot there (2.2 cm).
  Walk mode: only while `OpenRoomView._isShowing` (the game offers an interaction), haptic tick on a new target.
* **Hallway interaction ray from the pointing hand** (`[Input] InteractionRay = Controller|Head|Game`, replaces
  `GazeRayFromHead`): 0.1 deg camera parented to the aim pose, reach = game reach (1 m) + 0.4 m. The game's X
  crosshair is moved (OpenRoomView postfix, in the pixel frame) to the screen projection of the laser hit; reset to
  centre when there is no hit. Ray targets are big boxes (doors ~1 x 2.3 m, radio 0.3 x 0.43 m), no magnetism needed.
  `[Input] PointerHand` picks the start hand; pulling the other trigger switches.
* **Radio knob**: gamepad `UI/RadioKnob` = right stick, and the knob turns with the *change of the stick's angle*
  (stick rotated in a circle), not its deflection. It never moved before because the radio keeps the cursor locked,
  which the old code treated as walking (right stick zeroed for snap turn). Now: Gamepad mode passes the right stick,
  and **trigger + twisting the controller** synthesises a circling stick (gain 3; clockwise twist = needle right,
  verified in sim). Grips = shoulders = AM/FM.
* **Peephole**: picture on a quad (layer 29) fixed in the world in front of the head yaw at the moment it opens, 2 m,
  `[Rendering] PeepholeDegrees` 60 (was head-locked 90 deg); one eye, other black. The pointer works on it
  (`FlatScreen.ScreenOverride`) for the visitor's dialog choices.
* Verified in sim (simulated controllers, see commands below): laser + X on doors / radio / phone, Bedroom opened with
  hand ray + A, room pointer (mouse follows), pause -> Settings click, B back, Menu, fridge hover + hold-to-drink,
  phone dialling (display "13_"), radio knob via circling stick and via twist, peephole world-locked one-eye, main
  menu "Continue" click. Phone / radio / Kitchen / Storage refuse to open in this save (day 0 night: the game shows
  the hint faded) - close-ups were opened with the dev command for these tests.
* New debug commands: `simhand l|r x y z yaw pitch [trigger]`, `aim l|r u v` (at the screen panel), `aimat l|r x y z`,
  `roll l|r deg`, `trigger l|r s`, `btn l|r <VRButton> s`, `pad ... for <s>` (timed; a press and release in the same
  0.25 s poll was lost), `game` (input/watcher state + ray hit), `targets`, `inter`, `use i`,
  `closeup radio|phone|fridge [off]` (dev only), `scheme pad|kbm`, `ray main|head|hand` (persists to the cfg!).
* Not verified: haptics, real Quest controller aim pose / laser feel, radio via the real flow, dialogs at the door.

### 2.12 Sixth pass (gamepad scheme everywhere) - 2026-10-02

User: exiting should not depend on the cursor position; no button to navigate menus; control scheme should match the
glyphs. Cause: pointer screens ran in the game's Keyboard & Mouse scheme (keyboard glyphs "Leave Q"; no stick
navigation), so the user pointed at on-screen labels to leave.
* `[Input] PointerScheme = Gamepad` (default; `Mouse` = the 2.11 behaviour): the game stays in its Gaypad scheme in
  every mode, so glyphs are A/B/X/Y and every screen keeps its native controller handling (B = Exit/Cancel anywhere,
  left stick = navigate, A = submit, X = skip line, Menu/Start = pause and close pause).
* The laser still drives the game's cursor: virtual mouse + OS cursor (legacy readers) **and
  `GamepadCursor._cursorPosition`** (rooms: `gpCursorState=1`, the game's own controller cursor, A clicks there).
* Gamepad menus act on the **EventSystem selection** (Watcher/AMarker keep it on the screen's markers and re-assert it
  every frame). `UiPointer.SelectUnderPointer`: `EventSystem.RaycastAll` at the laser point, first interactable
  `Selectable` in the parents becomes the selection, re-applied every frame while the laser rests on it; any stick
  deflection hands control back to stick navigation until the laser moves to another button.
* Pointing trigger = A (South), other trigger = X (West), grips = RT+RB / LT+LB (prompts for RT mean grip).
* Verified in sim: pause -> laser on Settings + trigger opens Settings, B back; fridge hover via laser, hold A drinks;
  phone keypad via laser + A; Bedroom: game cursor follows the laser, "(B)" prompt, B leaves the room.
  Not verified: B on close-ups opened the normal way (the dev `closeup` command skips the game's exit wiring, so B
  did nothing there), dialog choices, collection menus.

### 2.13 Dialog cursor flicker - 2026-10-02

User: choosing a dialog option at the door, the cursor jumped back and forth rapidly.
* Cause: in gamepad mode the game keeps the OS cursor **locked**; `MoveSystemCursor` warped it to the laser every
  frame and Windows put it back to the centre every frame (measured: a "foreign" cursor move on every frame, 1245 in
  1245 frames), so everything reading the mouse alternated between the laser point and the screen centre.
* Fix: no OS cursor warping at all when the patch below is active (and never while the cursor is locked).
  **Harmony postfix on `UnityEngine.Input.get_mousePosition`** (`UiCapture.MousePositionPostfix`) returns the laser
  point (`UiPointer.LegacyMouse`, kept between frames while in pointer mode) - the legacy readers (fridge hover) follow
  the laser and the real cursor is never touched. The patch applies fine on this IL2CPP build.
* In the gamepad scheme the **virtual mouse is no longer fed or even created**: a mouse hovering a button puts it in
  its "highlighted" look, which in this game hides the gamepad "selected" look (dialog options showed no highlight).
* Verified in sim: door dialog, laser on "Fine" then "Same as it ever was": highlight follows, 2 selection changes in
  2 s, 0 foreign cursor moves; A picks the option; fridge hover follows the laser with the cursor locked.

### 2.14 Polish pass / NIVR 0.2.0 - 2026-10-02

Implemented the Must items 1-7 and Should items 8-12 from `POLISH_PROMPT.md`. The UI-resolution item uses the
documented safe fallback (1920x1080 or higher, with a low-resolution log warning); independent UI supersampling
would violate the window-sized canvas/layout constraint described above. No Could items were started.
All source changes stayed in `plugin/Core`; original game install, `src`, and `scripts` were not edited.

**Changes**

* `ComfortOverlay`: eye-only procedural blackout and vignette on layer 29. Camera/view transitions hook
  `AActionableObjectView.StartLooking`, `PlayerService.TeleportTo`, and `ScenesChanger.OpenScene`; unexpected base
  position/rotation changes also trigger. Default fade is 0.22 s (35% black hold, 65% reveal), adjustable 0.15-0.3 s.
  Head collision uses a cached 64-collider `OverlapSphereNonAlloc`, Default layer only, no triggers/player colliders;
  default radius 8 cm. Deliberate stick turns and ordinary tracked head rotation do not trigger jump fades.
* `TurnMode = Snap|Smooth|Off`, default Snap 30 degrees, Smooth 60 degrees/s. Optional `MotionVignette` is off by
  default and activates during smooth turn or fast stick walking. Existing game camera/body steering is retained.
* `ControllerVisuals`: procedural teal cuboids at grip poses, rendered only in the eyes, hidden in the peephole.
  `VRController` now exposes `IsAimTracked` / `IsGripTracked` separately; laser uses aim validity and marker grip validity.
* `Haptics`: hover (throttled), submit, room/window/peephole state entry and radio twist detents (default 12 degrees).
  `VibrationStrength` scales `VRController.Haptic`; 0 disables. Simulator logs hand/reason/amplitude/duration.
* Runtime focus loss opens the game's own `PauseMenuView.Switch(true)` and releases virtual input; resume is manual.
  OpenXR instance/session loss and loss-pending results request main-thread flat fallback, resource cleanup, fresh
  input actions and a 5-second retry. Generation-tagged render events prevent stale callbacks using recreated sessions.
  Right Menu long hold recenters when the runtime exposes it; both stick clicks and F8 still work (Quest reserves Menu).
* Cleanup now releases devices, render textures, procedural materials/meshes, rig cameras and render callbacks.
  Scene-owned canvas/camera/settings caches prune destroyed entries. Pointer errors retry instead of disabling forever.
  Simulator also runs in the background so inspection of captures does not stall its command polling.
* `VrSettings`: runtime clones of the native Volume row, checkbox, header and Back button. Adds 20 VR controls to
  native `SettingsInstance`, once per instance, including scene reloads. Values apply live and save to the BepInEx cfg.
  Covers all required controls, plus vignette/lazy HUD/subtitle lowering/physical crouch. Explicit up/down navigation,
  `SettingsMarker`, custom visibility scrolling and laser-held slider adjustment support both input paths.
* First-run floating controls card uses the game's font at runtime and procedural UI; any controller button dismisses
  it and records `ControlsCardSeen`. Dismissal is consumed until the button is released, avoiding an accidental click.
* Optional lazy walking HUD holds its heading until the configured 20-degree threshold, then catches up. Projected
  door/crosshair pixels are reprojected through the lagging panel to retain world alignment. `SubtitleOffset` moves the
  full subtitle panel only during UI capture, restores it afterwards, and clamps lowering to a 12-pixel bottom margin.
* Optional physical crouch (off by default) calls the game's `ECM2.Character.Crouch/UnCrouch`; head drop threshold
  0.35 m with 8 cm hysteresis. It respects `canEverCrouch` and releases on disabling VR/focus/mode changes.
* Performance: room texture renders only while a room/window picture is active. Resource scans remain throttled;
  collider buffer and rendering resources are reused. `status` now reports unscaled smoothed fps (also valid while
  paused), eye-render CPU time, OpenXR render-submit CPU time, room-render and haptic counters, fade and collision state.
* Version 0.2.0, `plugin/Core/package.ps1` builds Release and creates `release/NIVR-0.2.0.zip` with a strict four-file
  allowlist: Core DLL, Khronos OpenXR loader, complete Apache-2.0 license and README. No assets, game files, interop,
  DevTools or BepInEx core. README links BepInEx be.788 and covers installation, controls/glyphs, config, uninstall,
  limitations, 1080p UI and RenderScale 0.75/1.0/1.25 presets. All debug commands require nonempty `[Debug] CommandDir`;
  the release default is empty, and no local configuration is shipped.

**Verification and evidence**

* Debug and final Release builds: 0 errors, 0 warnings. The swapchain-image stack allocation inside a loop was replaced
  with a pinned managed array during session creation, removing the former CA2014 warning. Package entry allowlist
  checked after creation; the deployed DLL is the Release build. Every launch used deployed DevTools and SaveSandbox=true.
* Pause -> Settings -> VR: laser + A changed SnapTurnDegrees 30 -> 65; stick right changed 65 -> 75; stick up + A
  toggled Smooth, laser + A toggled back to Snap. Cfg checked after each step; trigger adjustment also tested.
  `polish_settings_verified_ui.png` shows native styling and correct `75°` / `60°/s` labels, without overlapping rows.
* `polish_card3_L.png`: visible first-run card; button dismissal and remembered cfg verified. Hallway/room/window
  captures show both procedural markers and the laser. `polish_peephole_verified_L/R.png`: left black, right picture,
  no controller markers. Open the window separately from a peephole visitor dialog for a valid window test;
  forcibly stacking `act` states mid-dialog can produce black captures. `polish_window_release_L.png` is the valid test.
* `polish_fade_L.png`: complete eye blackout on explicit transition. `headat -5.06 1 -9.45` places the simulated head
  inside the pantry door: status `fade=1.00 headBlocked=True`, both eyes black (`polish_collision_verified_L/R.png`).
  Returning to `simhead 0 0 0` clears it. `polish_vignette_L.png` confirms peripheral masking during smooth stick turn;
  status heading changed from 94.3 to 226 degrees. These checks establish visuals/logic, not comfort in a headset.
* Haptic logs verified hover, submit, opened and radio-detent pulses. Sandboxed `closeup radio`, trigger hold and two
  25-degree rolls produced detent ticks; day-0 gameplay gating is preserved. `btn r menu 1.3` logged a recenter.
* `focus lost` logged native pause and `game` showed `top=Pause`, `inUi=1`. VR off/on, gameplay -> main menu reload,
  and clean quit completed without recurring VR exceptions. Settings injected once in each newly created instance.
  Later days/dream scenes and actual SteamVR runtime restart remain untested.
* Lazy HUD: head world yaw 109.3 (15-degree turn) / HUD yaw 94.3; after a 40-degree turn, head 134.3 / HUD 133.3.
  Use all three rotation arguments: `simhead 0 0 0 15 0 0` (a lone yaw argument is ignored by the older parser).
  `polish_lazy_verified_L.png` shows lagged placement. Marker alignment during fast motion still needs headset checking.
* `polish_subtitle_default_L.png` vs final `polish_subtitle_clamped_L.png`: complete caption and background visibly
  lowered without clipping. Physical-crouch head drop 0.45 m in the hallway correctly stayed uncrouched because
  `canEverCrouch=False`; the allowed crouch zone belongs to an inactive later location. Positive crouch/stand testing
  in that location still needs gameplay/headset verification.
* Representative Release hallway status at 1440x1600 per eye and 1920x1080 UI: 215 fps (~4.65 ms/frame), eye CPU
  1.27 ms, roomRenders=0. Window status: 233 fps, eye CPU 1.83 ms, room counter increasing. These are simulator CPU
  observations, not GPU timings or headset performance; renderSubmitMs=0 in Simulator. Large six-PNG capture batches
  can provoke Unity JobTempAlloc warnings. Existing startup interop and scene-unload DOTween warnings also occur;
  no recurring NIVR Update/render exceptions appeared in final checks.
* Saved runtime evidence: `logs/polish_release_validation.log`, `logs/polish_final_validation.log`,
  `logs/polish_subtitle_validation.log`; captures are under `logs/shots`. Extra gated debug probes added: `settings
  [scroll <normalized>]`, `option <configKey> <value>`, `fade`, `focus lost`, `headat x y z`, `geometry`, `subtitle [off]`.
  `geometry` skips nonconvex mesh ClosestPoint (Unity only supports that call for convex/primitives).

**Interop traps discovered**

* `RTLTMPro.RTLTextMeshPro.text` is a hidden (`new`) property, not an override of TMP_Text.text. Setting only the
  base property lets RTL Update restore its cloned OriginalText (the volume value `100`). Use the typed RTL setter
  when the native text is RTLTMPro. Localization components are disabled/removed and native event delegates cleared.
* Clear runtime and persistent UnityEvent calls before replacing slider/toggle/button events. A cloned VolumeRow
  still belongs to native sound settings and is disabled/destroyed; copied selection delegates and FirstMarker are removed.
* Native Settings.OnItemSelected scrolling assumes the original rows and moves clones incorrectly. Skip it for the
  VR section and scroll only when the selected row lies outside the viewport. Do not use stripped
  `RectTransformUtility.CalculateRelativeRectTransformBounds` (NotSupportedException); transform four corners instead.
* Do not call Canvas.ForceUpdateCanvases from willRenderCanvases: it can hang. Keep the controls canvas active for the
  normal layout pass while excluding layer 29 from desktop cameras. Subtitle lowering must move its background too,
  and be clamped: the original panel already sits close to the bottom of the window-sized texture.

**Left for the headset / final state**

Comfort/scale, real haptic feel, grip/aim alignment, lazy-marker alignment, positive physical crouch, runtime focus
and SteamVR restart recovery need hardware verification. Right Menu is runtime-dependent; use both stick clicks on Quest.
Final state: game stopped, Backend=Auto, Snap=30, test comfort/HUD/crouch options reset, ControlsCardSeen=false so the
user gets the first-run card. SaveSandbox remains true; existing local debug preferences are preserved. Release contains
no cfg and defaults to CommandDir empty / Verbose false.
