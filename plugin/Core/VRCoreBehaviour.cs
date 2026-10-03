using System;
using System.Collections;
using System.Globalization;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime;
using NIVR.Core.OpenXR;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NIVR.Core;

/// <summary>
/// Frame loop of the VR core.
///   Update:          OpenXR events, xrWaitFrame, controller input, rig transforms, VRRig events.
///   onBeforeRender:  (after every LateUpdate, so after Cinemachine placed the game camera) take the game camera pose as
///                    the rig base, put the head on top of it, render the game camera once per eye into the eye textures
///                    and hand the frame to the render thread.
///   end of frame:    desktop mirror, debug captures.
/// The game camera itself is reused for the eye renders, so camera stacks, post-processing and camera-space canvases
/// all come along; no second set of cameras exists.
/// </summary>
public unsafe class VRCoreBehaviour : MonoBehaviour
{
    public VRCoreBehaviour(IntPtr ptr) : base(ptr) { }

    private readonly XrSession _xr = new();
    private XrInput _input = new();
    private readonly HandState[] _hands = new HandState[2];
    private bool _sim, _unsupported, _loggedNoHeadset;
    private int _systemTries;
    private float _nextTry;

    // eye textures: _eyeRT is what the camera renders into (MSAA, upright in Unity terms),
    // _submitRT is the resolved, vertically flipped copy whose native texture is copied into the swapchain.
    private readonly RenderTexture[] _eyeRT = new RenderTexture[2];
    private readonly RenderTexture[] _submitRT = new RenderTexture[2];
    private readonly IntPtr[] _submitPtr = new IntPtr[2];
    private int _eyeW, _eyeH;

    // frame state
    private bool _frameWaited;
    private XrFrameState _frameState;
    private ulong _trackingSpace;
    private readonly XrView[] _views = new XrView[2];
    private readonly Vector3[] _eyePos = new Vector3[2];
    private readonly Quaternion[] _eyeRot = { Quaternion.identity, Quaternion.identity };
    private bool _inBeforeRender;
    private long _beforeRenderCalls, _framesSubmitted, _framesRendered;

    // rig
    private Transform _origin, _headT;
    private readonly Transform[] _aimT = new Transform[2], _gripT = new Transform[2];
    private Vector3 _basePos; private Quaternion _baseRot = Quaternion.identity; private bool _haveBase;
    private Vector3 _recenterPos; private float _recenterYaw; private bool _recentered;
    private int _wroteCamId; private Vector3 _wrotePos; private Quaternion _wroteRot;
    private int _brainCamId; private bool _camHasBrain;
    private float _bothSticksHeld;
    private Vector3 _simPos; private Quaternion _simRot = Quaternion.identity;

    // settings we override while active
    private int _savedVSync = -1, _savedTargetFps; private bool _savedRunInBackground;

    // end-of-frame work
    private Camera _eyeCam;
    private UnityEngine.Rendering.Universal.UniversalAdditionalCameraData _eyeData;
    private int _eyeRendererIndex;
    private readonly FlatScreen _flatScreen = new();
    private bool _debugOff;
    private readonly UiCapture _uiCapture = new();
    private readonly VirtualGamepad _gamepad = new();
    private readonly UiPointer _pointer = new();
    private readonly ComfortOverlay _comfortOverlay = new();
    private readonly ControllerVisuals _controllerVisuals = new();
    private readonly ControlsCard _controlsCard = new();
    private bool _focused;
    private _Code.Player.EWatcherState? _previousState;
    private float _rightMenuHeld, _recentTurnUntil, _recenterHeight;
    private bool _physicalCrouched;
    private ECM2.Character _crouchOwner;
    private double _renderMs;
    private float _frameSeconds;
    private string _captureName;
    private float _nextCmdPoll;

    private void Awake()
    {
        var root = new GameObject("NIVR Rig");
        DontDestroyOnLoad(root);
        _origin = root.transform;
        _headT = Child("Head");
        for (int h = 0; h < 2; h++)
        {
            _aimT[h] = Child(h == 0 ? "LeftAim" : "RightAim");
            _gripT[h] = Child(h == 0 ? "LeftGrip" : "RightGrip");
        }
        VRRig.Origin = _origin;
        VRRig.Head.Transform = _headT;
        VRRig.LeftController.Aim = _aimT[0]; VRRig.LeftController.Grip = _gripT[0];
        VRRig.RightController.Aim = _aimT[1]; VRRig.RightController.Grip = _gripT[1];
        VRRig.RecenterImpl = Recenter;
        VRRig.EyeTextureImpl = eye => VRRig.IsActive && eye >= 0 && eye < 2 ? _eyeRT[eye] : null;
        VRRig.HapticImpl = (hand, amp, sec, freq) => { if (!_sim && _xr.Running) _input.Haptic(_xr.Session, (int)hand, amp, sec, freq); };

        HookBeforeRender();
        StartCoroutine(EndOfFrameLoop().WrapToIl2Cpp());
    }

    private Transform Child(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_origin, false);
        return go.transform;
    }

    // ------------------------------------------------------------------ Update

    private static bool s_loggedEsFocus;
    // The EventSystem only processes submit / navigation while the game window is focused, and with a headset on it
    // rarely is. Tell it it has focus.
    private static void KeepEventSystemFocused()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null || es.isFocused) return;
        if (!s_loggedEsFocus) { s_loggedEsFocus = true; CorePlugin.Log.LogInfo("EventSystem reports no window focus; forcing it so menus keep working in the headset."); }
        es.OnApplicationFocus(true);
    }

    internal static bool TraceSubmit;
    private void TraceSubmitState()
    {
        var gp = _gamepad.Device != null ? _gamepad.Device.TryCast<Gamepad>() : null;
        var ih = GameState.Input;
        bool south = gp != null && gp.buttonSouth.isPressed;
        bool clicked = ih != null && (ih.UISubmitClicked || ih.UISubmitDown);
        if (!south && !clicked) return;
        var es = UnityEngine.EventSystems.EventSystem.current;
        CorePlugin.Log.LogInfo($"TRACE f={Time.frameCount} south={south} uiSubmit={clicked} sel={(es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "none")} scheme={(ih != null && ih._playerInput != null ? ih._playerInput.currentActionMap?.name : "?")} focus={Application.isFocused}/{(es != null ? es.isFocused : false)}");
    }

    private void Update()
    {
        try
        {
            _frameSeconds = _frameSeconds == 0f ? Time.unscaledDeltaTime : Mathf.Lerp(_frameSeconds, Time.unscaledDeltaTime, 0.05f);
            while (XrSession.RenderThreadLog.TryDequeue(out var msg)) CorePlugin.Log.LogInfo("[render] " + msg);
            PollDebugCommands();
            ReleaseTimedDebugInput();

            if (_xr.Instance != 0) _xr.PollEvents();
            bool focused = _sim || (_xr.Running && _xr.State == XrSessionState.Focused && !_xr.InstanceLost);
            if (_focused && !focused) SessionFocus.Lost();
            if (_focused != focused) { _gamepad.Restore(); _pointer.Restore(); }
            _focused = focused;
            EnsureBackend();
            SetActive(!_debugOff && (_sim || (_xr.Session != 0 && _xr.Running)));
            if (!VRRig.IsActive) return;

            if (!_sim)
            {
                // A waited frame that never reached onBeforeRender must still be begun/ended, or xrWaitFrame blocks forever.
                if (_frameWaited) { SubmitFrame(false); GL.Flush(); }

                _trackingSpace = VRConfig.TrackingMode.Value == VRTrackingMode.Standing && _xr.StageSpace != 0 ? _xr.StageSpace : _xr.LocalSpace;
                if (_xr.WaitFrame(out _frameState)) _frameWaited = true;
                else return;

                long t = _frameState.predictedDisplayTime;
                if (_xr.LocateSpace(_xr.ViewSpace, _trackingSpace, t, out var headPose, out var flags))
                {
                    VRRig.Head.LocalPosition = Pos(headPose.position);
                    VRRig.Head.LocalRotation = Rot(headPose.orientation);
                    VRRig.Head.IsTracked = (flags & Xr.LocPositionTracked) != 0;
                    if (!_recentered) Recenter();
                }
                else VRRig.Head.IsTracked = false;

                _input.Read(_xr, _trackingSpace, t, _hands);
            }
            else
            {
                VRRig.Head.LocalPosition = _simPos;
                VRRig.Head.LocalRotation = _simRot;
                VRRig.Head.IsTracked = true;
                if (!_recentered) Recenter();
            }

            UpdateControllers();
            UpdateRigTransforms();
            FollowHead();
            HandleRecenterInput();
            var mode = CurrentInputMode();
            ButtonGate.Update(mode != InputMode);
            var state = GameState.Top;
            if (state != _previousState && (state == _Code.Player.EWatcherState.Room || state == _Code.Player.EWatcherState.Window || state == _Code.Player.EWatcherState.Peephole))
                Haptics.Tick(_pointer.HandSide, "opened", 0.5f, 0.06f);
            _previousState = state;
            InputMode = mode;
            bool controlsCard = _controlsCard.Tick();
            VrSettings.Tick();
            KeepEventSystemFocused();
            PauseGuard.Tick(_focused && !controlsCard);
            _pointer.PrePress(_focused && !controlsCard ? InputMode : VRInputMode.Walk);
            _gamepad.Tick(_focused && !controlsCard, InputMode == VRInputMode.Walk, InputMode == VRInputMode.Pointer && UiPointer.ControllersPresent && UiPointer.MouseScheme,
                GameState.Top == _Code.Player.EWatcherState.Radio, _pointer.HandSide);
            if (_focused && !controlsCard) _pointer.Tick(InputMode, _flatScreen, _gamepad, _headT);
            else _pointer.Restore();
            if (_focused && !controlsCard && InputMode == VRInputMode.Walk) HandleSnapTurn(); // elsewhere the right stick is the radio knob / scroll
            HandlePhysicalCrouch(_focused && !controlsCard && InputMode == VRInputMode.Walk);
            Comfort.Tick();
            VRRig.RaiseButtons();
            if (TraceSubmit) TraceSubmitState();
        }
        catch (Exception e)
        {
            CorePlugin.LogThrottled("update", "VR core Update failed: " + e);
        }
    }

    private void EnsureBackend()
    {
        if (_unsupported || _sim) return;
        if (VRConfig.Backend.Value == VRBackend.Simulator)
        {
            CreateEyeTextures(1440, 1600, false);
            _sim = true;
            VRRig.IsSimulated = true;
            CorePlugin.Log.LogInfo("Simulator backend: no headset, rendering two fake eyes (1440x1600, 100 degree fov).");
            return;
        }
        if (_xr.InstanceLost || _xr.RecoveryRequested)
        {
            SetActive(false);
            _frameWaited = false;
            GL.Flush(); // drain old texture-copy callbacks before releasing/recreating resources
            _xr.DestroyInstance();
            _input = new XrInput(); _xr.RecoveryRequested = false;
            _nextTry = Time.unscaledTime + 5f;
            CorePlugin.Log.LogInfo("OpenXR connection lost: flat fallback, recreating runtime/session in 5 seconds.");
            return;
        }
        if (_xr.Session != 0 || Time.unscaledTime < _nextTry) return;

        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
        {
            _unsupported = true;
            CorePlugin.Log.LogError($"VR needs Direct3D 11 but the game runs on {SystemInfo.graphicsDeviceType}. Start the game with -force-d3d11.");
            return;
        }

        if (_xr.Instance == 0)
        {
            try
            {
                if (!_xr.CreateInstance(out int r))
                {
                    if (!_loggedNoHeadset) CorePlugin.Log.LogWarning($"No OpenXR runtime ({Xr.ResultName(0, r)}); loader: {Xr.LoaderPath}. The game stays flat; retrying every 10 s.");
                    _loggedNoHeadset = true;
                    _nextTry = Time.unscaledTime + 10f;
                    return;
                }
            }
            catch (DllNotFoundException e)
            {
                _unsupported = true;
                CorePlugin.Log.LogError("openxr_loader.dll not found next to NIVR.Core.dll: " + e.Message);
                return;
            }
            if (!_loggedNoHeadset) CorePlugin.Log.LogInfo($"OpenXR instance created. Runtime: {_xr.RuntimeName}; loader: {Xr.LoaderPath}");
        }

        if (!_xr.TryGetSystem(out int sr))
        {
            if (!_loggedNoHeadset) CorePlugin.Log.LogInfo($"No headset yet ({_xr.Err(sr)}). The game stays flat; checking again every 10 s. Start SteamVR with the headset connected.");
            _loggedNoHeadset = true;
            // SteamVR only reports a headset to instances created while it is running, so start over every third try.
            if (++_systemTries % 3 == 0) _xr.DestroyInstance();
            _nextTry = Time.unscaledTime + 10f;
            return;
        }
        _loggedNoHeadset = false;

        // Actions belong to the instance and need a live runtime, so they are created only once a headset exists.
        _input = new XrInput();
        try { _input.Create(_xr.Instance); }
        catch (Exception e) { CorePlugin.Log.LogError("Controller actions could not be created (head tracking still works): " + e.Message); }

        float scale = Mathf.Clamp(VRConfig.RenderScale.Value, 0.3f, 2.5f);
        int w = ((int)(_xr.EyeWidth * scale) + 1) & ~1, h = ((int)(_xr.EyeHeight * scale) + 1) & ~1;
        CorePlugin.Log.LogInfo($"Headset: {_xr.SystemName}, recommended eye {_xr.EyeWidth}x{_xr.EyeHeight}, rendering {w}x{h}");
        if (!_xr.CreateSession(Texture2D.whiteTexture.GetNativeTexturePtr(), w, h, out var error))
        {
            CorePlugin.Log.LogError("OpenXR session could not be created: " + error + ". Retrying in 10 s.");
            _xr.DestroyInstance();
            _nextTry = Time.unscaledTime + 10f;
            return;
        }
        CreateEyeTextures(w, h, _xr.SwapchainFormat == D3D11.FormatB8G8R8A8UNormSrgb);
        _input.Attach(_xr.Session);
        _frameWaited = false;
        CorePlugin.Log.LogInfo($"OpenXR session created (swapchain format {_xr.SwapchainFormat}, stage space {(_xr.StageSpace != 0 ? "yes" : "no")}).");
    }

    private void CreateEyeTextures(int w, int h, bool bgra)
    {
        for (int i = 0; i < 2; i++)
        {
            if (_eyeRT[i] != null) { _eyeRT[i].Release(); Destroy(_eyeRT[i]); }
            if (_submitRT[i] != null) { _submitRT[i].Release(); Destroy(_submitRT[i]); }
            var fmt = bgra ? RenderTextureFormat.BGRA32 : RenderTextureFormat.ARGB32;
            int msaa = VRConfig.Msaa.Value;
            if (msaa != 2 && msaa != 4 && msaa != 8) msaa = 1;
            _eyeRT[i] = new RenderTexture(w, h, 24, fmt, RenderTextureReadWrite.sRGB) { name = "NIVR Eye " + i, antiAliasing = msaa, hideFlags = HideFlags.HideAndDontSave };
            _eyeRT[i].Create();
            _submitRT[i] = new RenderTexture(w, h, 0, fmt, RenderTextureReadWrite.sRGB) { name = "NIVR Submit " + i, antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
            _submitRT[i].Create();
            _submitPtr[i] = _submitRT[i].GetNativeTexturePtr();
        }
        _eyeW = w; _eyeH = h;
    }

    private void SetActive(bool active)
    {
        if (active == VRRig.IsActive) return;
        VRRig.IsActive = active;
        CorePlugin.Log.LogInfo(active ? "VR active: stereo rendering + head tracking on." : "VR inactive: game is flat again.");
        if (active)
        {
            _recentered = false; _haveBase = false; _wroteCamId = 0;
            // Simulator commands must keep running when inspecting captures outside the game window too.
            _savedVSync = QualitySettings.vSyncCount; _savedTargetFps = Application.targetFrameRate; _savedRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
        }
        else
        {
            _controllerVisuals.EndEyes(); _comfortOverlay.Reset(); _controlsCard.Hide();
            HandlePhysicalCrouch(false);
            if (VRRig.GameCamera != null && VRRig.GameCamera.GetInstanceID() == _wroteCamId)
                VRRig.GameCamera.transform.SetPositionAndRotation(_basePos, _baseRot);
            _follow = false; _haveBase = false; _wroteCamId = 0;
            Comfort.Restore();
            _uiCapture.Restore();
            _gamepad.Restore();
            _pointer.Restore();
            if (_peepCam != null && _peepCam.targetTexture == _peepRT) _peepCam.targetTexture = null;
            Comfort.KeepPostEffects = false;
            if (_savedVSync >= 0)
            {
                QualitySettings.vSyncCount = _savedVSync; Application.targetFrameRate = _savedTargetFps; Application.runInBackground = _savedRunInBackground;
                _savedVSync = -1;
            }
            VRRig.GameCamera = null;
            VRRig.LeftController.IsTracked = VRRig.RightController.IsTracked = false;
            VRRig.LeftController.IsAimTracked = VRRig.RightController.IsAimTracked = false;
            VRRig.LeftController.IsGripTracked = VRRig.RightController.IsGripTracked = false;
            VRRig.LeftController.IsConnected = VRRig.RightController.IsConnected = false;
        }
        VRRig.RaiseActiveChanged(active);
    }

    private void UpdateControllers()
    {
        for (int h = 0; h < 2; h++)
        {
            var c = VRRig.Controller((VRHand)h);
            ref HandState s = ref _hands[h];
            if (_sim) s = default;
            c.BeginFrame();
            c.IsConnected = s.active;
            c.IsTracked = s.aimValid || s.gripValid;
            c.IsAimTracked = s.aimValid; c.IsGripTracked = s.gripValid;
            c.Trigger = s.trigger; c.GripValue = s.grip; c.Stick = new Vector2(s.stickX, s.stickY);
            c.SetAnalog(VRButton.Trigger, s.trigger);
            c.SetAnalog(VRButton.Grip, s.grip);
            c.Set(VRButton.Primary, s.primary);
            c.Set(VRButton.Secondary, s.secondary);
            c.Set(VRButton.Menu, s.menu);
            c.Set(VRButton.Stick, s.stickClick);
            c.SetAnalog(VRButton.StickLeft, -s.stickX);
            c.SetAnalog(VRButton.StickRight, s.stickX);
            c.SetAnalog(VRButton.StickUp, s.stickY);
            c.SetAnalog(VRButton.StickDown, -s.stickY);
            if (s.aimValid) { _aimT[h].localPosition = Pos(s.aimPose.position); _aimT[h].localRotation = Rot(s.aimPose.orientation); }
            if (s.gripValid) { _gripT[h].localPosition = Pos(s.gripPose.position); _gripT[h].localRotation = Rot(s.gripPose.orientation); }
            if (_sim && _simHand[h].HasValue)
            {
                // simhand: a tracked controller at a fixed pose relative to the head, with an optional trigger value.
                var (pos, rot, trig) = _simHand[h].Value;
                c.IsConnected = c.IsTracked = true;
                c.IsAimTracked = c.IsGripTracked = true;
                c.Trigger = trig; c.SetAnalog(VRButton.Trigger, trig);
                _aimT[h].localPosition = _simPos + _simRot * pos;
                _aimT[h].localRotation = _simRot * rot;
                _gripT[h].localPosition = _aimT[h].localPosition; _gripT[h].localRotation = _aimT[h].localRotation;
            }
            if (_sim)
                for (int b = 0; b < _simButtons[h].Length; b++)
                    if (_simButtons[h][b] > 0f)
                    {
                        if (Time.unscaledTime < _simButtons[h][b] || Time.frameCount < _simButtonFrame) { c.IsConnected = true; c.Set((VRButton)b, true); }
                        else _simButtons[h][b] = 0f;
                    }
        }
    }

    private readonly (Vector3 pos, Quaternion rot, float trigger)?[] _simHand = new (Vector3, Quaternion, float)?[2];
    private float _padOffAt; private int _padOffFrame;
    private readonly float[][] _simButtons = { new float[16], new float[16] }; private int _simButtonFrame;
    private readonly float[] _triggerOffAt = new float[2]; private readonly int[] _triggerOffFrame = new int[2];

    // Timed debug inputs ("pad ... for s", "trigger r s") are released here, never before 3 frames have seen them.
    private void ReleaseTimedDebugInput()
    {
        if (_padOffAt > 0f && Time.unscaledTime >= _padOffAt && Time.frameCount >= _padOffFrame)
        {
            _gamepad.DebugLeftStick = _gamepad.DebugRightStick = null; _gamepad.DebugButtons = 0; _padOffAt = 0f;
        }
        for (int h = 0; h < 2; h++)
            if (_triggerOffAt[h] > 0f && Time.unscaledTime >= _triggerOffAt[h] && Time.frameCount >= _triggerOffFrame[h] && _simHand[h].HasValue)
            {
                var v = _simHand[h].Value; _simHand[h] = (v.pos, v.rot, 0f); _triggerOffAt[h] = 0f;
            }
    }

    private void HandleRecenterInput()
    {
        bool key = false;
        try { var kb = Keyboard.current; key = kb != null && kb[VRConfig.RecenterKey.Value].wasPressedThisFrame; } catch (Exception) { }
        if (VRConfig.RecenterWithThumbsticks.Value && VRRig.LeftController.GetButton(VRButton.Stick) && VRRig.RightController.GetButton(VRButton.Stick))
        {
            float before = _bothSticksHeld;
            _bothSticksHeld += Time.unscaledDeltaTime;
            if (before < 0.5f && _bothSticksHeld >= 0.5f) key = true;
        }
        else _bothSticksHeld = 0f;
        if (VRConfig.RecenterWithRightMenu.Value && VRRig.RightController.GetButton(VRButton.Menu))
        {
            float before = _rightMenuHeld; _rightMenuHeld += Time.unscaledDeltaTime;
            if (before < VRConfig.RightMenuHoldSeconds.Value && _rightMenuHeld >= VRConfig.RightMenuHoldSeconds.Value) key = true;
        }
        else _rightMenuHeld = 0f;
        if (key) Recenter();
    }

    internal static VRInputMode InputMode { get; private set; }

    private static VRInputMode CurrentInputMode()
    {
        // The game's own screen state decides; the cursor lock is the fallback (main menu, unknown states).
        switch (GameState.Top)
        {
            case _Code.Player.EWatcherState.World3d:
                return Cursor.lockState == CursorLockMode.Locked ? VRInputMode.Walk : VRInputMode.Pointer;
            case _Code.Player.EWatcherState.Radio:     // knob = right stick, bands = grips (the mouse knob is a fiddly drag)
            case _Code.Player.EWatcherState.Peephole:  // looking only; a visitor's dialog on top is pointer again
            case _Code.Player.EWatcherState.Window:
            case _Code.Player.EWatcherState.Movie:
            case _Code.Player.EWatcherState.Dream:
                return VRInputMode.Gamepad;
            case null:
                // No watcher: the main menu (a gamepad keeps the cursor locked there) counts as pointer while its UI is up.
                var ui = GameState.Input;
                if (ui != null && ui._inUiCounter > 0) return VRInputMode.Pointer;
                return Cursor.lockState == CursorLockMode.Locked ? VRInputMode.Walk : VRInputMode.Pointer;
            default:                                   // rooms, dialogs, phone, fridge, menus
                return VRInputMode.Pointer;
        }
    }

    private void HandleSnapTurn()
    {
        if (VRConfig.TurnMode.Value == VRTurnMode.Off) return;
        if (VRConfig.TurnMode.Value == VRTurnMode.Smooth)
        {
            float x = _gamepad.DebugRightStick?.x ?? VRRig.RightController.Stick.x;
            if (Mathf.Abs(x) > 0.2f) Turn(Mathf.Sign(x) * (Mathf.Abs(x) - 0.2f) / 0.8f * VRConfig.SmoothTurnSpeed.Value * Mathf.Min(Time.unscaledDeltaTime, 0.05f));
            return;
        }
        float deg = VRConfig.SnapTurnDegrees.Value;
        if (deg <= 0f) return;
        var r = VRRig.RightController;
        if (r.GetButtonDown(VRButton.StickRight)) Turn(deg);
        else if (r.GetButtonDown(VRButton.StickLeft)) Turn(-deg);
    }

    private void Turn(float deg)
    {
        _recentTurnUntil = Time.unscaledTime + 0.15f;
        if (_follow) _originYaw += deg; // the body is steered to the head, so turning the tracking space turns both
        else Comfort.Turn(deg);
    }

    // ------------------------------------------------------------------ rig maths

    private static Vector3 Pos(XrVector3f v) => new(v.x, v.y, -v.z);
    private void HandlePhysicalCrouch(bool walking)
    {
        var pc = Comfort.Player;
        var character = pc != null ? pc._character : null;
        if (_crouchOwner != character) { _physicalCrouched = false; _crouchOwner = character; }
        if (character == null) return;
        float drop = _recenterHeight - VRRig.Head.LocalPosition.y;
        if (!walking || !VRConfig.PhysicalCrouch.Value)
        { if (_physicalCrouched) character.UnCrouch(); _physicalCrouched = false; return; }
        if (!_physicalCrouched && drop >= VRConfig.CrouchDrop.Value && character.canEverCrouch && !character.IsCrouched())
        { character.Crouch(); _physicalCrouched = true; if (VRConfig.Verbose.Value) CorePlugin.Log.LogInfo("Physical crouch: crouching in an allowed game zone."); }
        else if (_physicalCrouched && (drop < VRConfig.CrouchDrop.Value - 0.08f || !character.canEverCrouch))
        { character.UnCrouch(); _physicalCrouched = false; if (VRConfig.Verbose.Value) CorePlugin.Log.LogInfo("Physical crouch: standing."); }
    }

    private void OnDestroy()
    {
        SetActive(false);
        if (_willRenderDelegate != null) Canvas.remove_willRenderCanvases(_willRenderDelegate);
        GL.Flush(); _xr.DestroyInstance();
        _comfortOverlay.Dispose(); _controllerVisuals.Dispose(); _controlsCard.Dispose();
        _uiCapture.Dispose(); _flatScreen.Dispose(); _pointer.Dispose(); _gamepad.Dispose();
        for (int i = 0; i < 2; i++)
        {
            if (_eyeRT[i] != null) { _eyeRT[i].Release(); Destroy(_eyeRT[i]); }
            if (_submitRT[i] != null) { _submitRT[i].Release(); Destroy(_submitRT[i]); }
        }
        if (_peepRT != null) { _peepRT.Release(); Destroy(_peepRT); }
        if (_peepQuad != null) Destroy(_peepQuad);
        if (_peepMat != null) Destroy(_peepMat);
        if (_eyeCam != null) Destroy(_eyeCam.gameObject);
        if (_origin != null) Destroy(_origin.gameObject);
        VRRig.RecenterImpl = null; VRRig.EyeTextureImpl = null; VRRig.HapticImpl = null;
        VRRig.Head.IsTracked = false; VRRig.GameCamera = null;
    }
    private static Quaternion Rot(XrQuaternionf q) => new(-q.x, -q.y, q.z, q.w);

    private bool Standing => VRConfig.TrackingMode.Value == VRTrackingMode.Standing;

    private void Recenter()
    {
        var p = VRRig.Head.LocalPosition;
        if (Standing) p.y = 0f; // keep the real floor
        _recenterPos = p;
        _recenterYaw = VRRig.Head.LocalRotation.eulerAngles.y;
        _recentered = true;
        _recenterHeight = VRRig.Head.LocalPosition.y;
        CorePlugin.Log.LogInfo($"Recentered (head at {p.x:F2},{p.y:F2},{p.z:F2}, yaw {_recenterYaw:F0}).");
        _flatScreen.Replace();
        VRRig.RaiseRecentered();
    }

    /// <summary>Places the tracking-space origin so that the recentered head sits on the game's camera pose.</summary>
    private void UpdateRigTransforms()
    {
        float scale = 1f / Mathf.Max(0.01f, VRConfig.WorldScale.Value);
        float baseYaw = _baseRot.eulerAngles.y;
        // Walking: the tracking space keeps its own heading and the game's body + camera are steered to the head
        // (see FollowHead), so nothing the game does to its camera can turn the world under the player.
        // Everything else (scripted looks, rooms, menus): the tracking space hangs on the game camera's heading.
        bool follow = FollowAllowed();
        if (follow && !_follow) _originYaw = baseYaw - _recenterYaw;
        if (!follow && _follow) _recenterYaw = Mathf.DeltaAngle(_originYaw, baseYaw);
        _follow = follow;
        Quaternion baseRot = VRConfig.RigRotation.Value == VRRigRotation.YawOnly ? Quaternion.Euler(0f, baseYaw, 0f) : _baseRot;
        Quaternion originRot = follow ? Quaternion.Euler(0f, _originYaw, 0f) : baseRot * Quaternion.Euler(0f, -_recenterYaw, 0f);
        Vector3 anchor = _basePos;
        if (Standing) anchor.y -= VRConfig.StandingEyeHeight.Value;
        else anchor.y += VRConfig.SeatedHeightOffset.Value;
        Vector3 originPos = anchor - originRot * (_recenterPos * scale);

        _origin.SetPositionAndRotation(originPos, originRot);
        _origin.localScale = new Vector3(scale, scale, scale);
        _headT.localPosition = VRRig.Head.LocalPosition;
        _headT.localRotation = VRRig.Head.LocalRotation;
    }

    private bool _follow;
    private float _originYaw;

    private bool FollowAllowed()
    {
        if (!VRConfig.BodyFollowsHead.Value || VRConfig.RigRotation.Value != VRRigRotation.YawOnly) return false;
        if (!FlatScreen.WorldMode || !_camHasBrain || !_haveBase) return false;
        var pc = Comfort.Player;
        return pc != null && pc.isActiveAndEnabled && !pc._isLookingAt;
    }

    /// <summary>
    /// Points the game's own player at what the head looks at: body yaw = head yaw, camera pitch = head pitch. The
    /// game's camera, its "is the player facing this" checks, movement direction and screen-projected HUD markers then
    /// all agree with the headset. (The interaction ray comes from the pointing controller, see UiPointer.)
    /// </summary>
    private void FollowHead()
    {
        var pc = Comfort.Player;
        if (!_follow || pc == null) return;

        Vector3 e = VRRig.Head.LocalRotation.eulerAngles;
        pc._character.SetYaw(_originYaw + e.y);
        float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -80f, 80f);
        if (pc.cameraTarget != null) pc.cameraTarget.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private static Matrix4x4 Projection(XrFovf fov, float near, float far)
    {
        float l = Mathf.Tan(fov.angleLeft), r = Mathf.Tan(fov.angleRight), u = Mathf.Tan(fov.angleUp), d = Mathf.Tan(fov.angleDown);
        var m = new Matrix4x4();
        m.m00 = 2f / (r - l); m.m02 = (r + l) / (r - l);
        m.m11 = 2f / (u - d); m.m12 = (u + d) / (u - d);
        m.m22 = -(far + near) / (far - near); m.m23 = -2f * far * near / (far - near);
        m.m32 = -1f;
        return m;
    }

    private static Camera FindGameCamera()
    {
        var cam = Camera.main;
        return cam != null && cam.isActiveAndEnabled ? cam : null;
    }

    // ------------------------------------------------------------------ rendering

    // "After every LateUpdate, before the frame is rendered" hook. Application.onBeforeRender is stripped from this
    // build, and RenderPipelineManager.DoRenderLoop_Internal is already inside the render loop (Camera.Render there is
    // rejected as recursive rendering). Canvas.willRenderCanvases runs in PostLateUpdate.PlayerUpdateCanvases: after
    // Cinemachine's LateUpdate, outside the render loop, once per frame (plus once per manual Camera.Render, filtered).
    private int _lastRenderFrame = -1;
    private Canvas.WillRenderCanvases _willRenderDelegate;

    private void HookBeforeRender()
    {
        _willRenderDelegate = (Canvas.WillRenderCanvases)new Action(PreRenderTick);
        Canvas.add_willRenderCanvases(_willRenderDelegate);
    }

    private void PreRenderTick()
    {
        if (_inBeforeRender) return;
        int frame = Time.frameCount;
        if (frame == _lastRenderFrame) return;
        _lastRenderFrame = frame;
        OnBeforeRender();
    }

    private void OnBeforeRender()
    {
        _beforeRenderCalls++;
        if (_inBeforeRender || !VRRig.IsActive) return;
        if (!_sim && !_frameWaited) return;
        _inBeforeRender = true;
        try
        {
            bool rendered = false;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try { rendered = RenderEyes(); _renderMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
            catch (Exception e) { CorePlugin.LogThrottled("render", "VR eye rendering failed: " + e); }
            if (!_sim) SubmitFrame(rendered);
        }
        finally { _inBeforeRender = false; }
    }

    private bool LocateEyes()
    {
        if (_sim)
        {
            const float halfIpd = 0.032f, half = 50f * Mathf.Deg2Rad;
            for (int i = 0; i < 2; i++)
            {
                _eyePos[i] = _simPos + _simRot * new Vector3(i == 0 ? -halfIpd : halfIpd, 0f, 0f);
                _eyeRot[i] = _simRot;
                _views[i].fov = new XrFovf { angleLeft = -half, angleRight = half, angleUp = half, angleDown = -half };
            }
        }
        else
        {
            if (_frameState.shouldRender == 0) return false;
            fixed (XrView* v = _views)
                if (!_xr.LocateViews(_frameState.predictedDisplayTime, _trackingSpace, v, out _)) return false;
            for (int i = 0; i < 2; i++) { _eyePos[i] = Pos(_views[i].pose.position); _eyeRot[i] = Rot(_views[i].pose.orientation); }
        }

        // Head = midpoint of the eyes (refreshes the Update-time pose with the latest prediction).
        Vector3 centre = (_eyePos[0] + _eyePos[1]) * 0.5f;
        VRRig.Head.LocalPosition = centre;
        VRRig.Head.LocalRotation = Quaternion.Slerp(_eyeRot[0], _eyeRot[1], 0.5f);

        float ipd = VRConfig.IpdOverrideMm.Value * 0.001f, actual = (_eyePos[1] - _eyePos[0]).magnitude;
        if (ipd > 0f && actual > 1e-4f)
            for (int i = 0; i < 2; i++) _eyePos[i] = centre + (_eyePos[i] - centre) * (ipd / actual);
        return true;
    }

    private bool RenderEyes()
    {
        var cam = FindGameCamera();
        VRRig.GameCamera = cam;
        if (cam == null || _eyeRT[0] == null || _submitRT[0] == null) return false;

        // Rig base = where the game put its camera this frame. If the transform still holds what we wrote last frame,
        // the game did not move it (paused brain etc.), so the previous base stays.
        var t = cam.transform;
        int camId = cam.GetInstanceID();
        Vector3 curPos = t.position; Quaternion curRot = t.rotation;
        bool untouched = camId == _wroteCamId && (curPos - _wrotePos).sqrMagnitude < 1e-10f && Quaternion.Angle(curRot, _wroteRot) < 0.01f;
        if (!untouched || !_haveBase)
        {
            bool jump = !_haveBase || camId != _brainCamId || (curPos - _basePos).magnitude > VRConfig.JumpDistance.Value / Mathf.Max(0.01f, VRConfig.WorldScale.Value)
                || (!_follow && Time.unscaledTime > _recentTurnUntil && Quaternion.Angle(curRot, _baseRot) > VRConfig.JumpAngle.Value);
            if (jump) _comfortOverlay.Jump();
            _basePos = curPos; _baseRot = curRot; _haveBase = true;
        }
        VRRig.GameCameraPosition = _basePos; VRRig.GameCameraRotation = _baseRot;

        if (!LocateEyes()) return false;
        UpdateRigTransforms();
        var move = _gamepad.DebugLeftStick ?? VRRig.LeftController.Stick;
        var turnStick = _gamepad.DebugRightStick ?? VRRig.RightController.Stick;
        float motion = Mathf.Max(Mathf.InverseLerp(0.65f, 1f, move.magnitude), VRConfig.TurnMode.Value == VRTurnMode.Smooth ? Mathf.InverseLerp(0.2f, 1f, Mathf.Abs(turnStick.x)) : 0f);
        _comfortOverlay.Update(_headT, InputMode == VRInputMode.Walk, motion);

        if (camId != _brainCamId)
        {
            _brainCamId = camId;
            var brain = cam.GetComponent<CinemachineBrain>();
            _camHasBrain = brain != null && brain.enabled;
        }

        if (UpdatePeephole()) { RenderPeephole(); _framesRendered++; _wroteCamId = 0; return true; }

        bool flat = (VRRig.FlatScreenMode ?? VRConfig.FlatScreen.Value) != VRFlatScreenMode.Off;
        if (flat) _uiCapture.Render(cam);

        EnsureEyeCamera();
        // The eye camera is a per-frame copy of the game camera (culling mask, clear flags, URP settings), so the game
        // camera keeps rendering the desktop view with its own canvases and layout untouched.
        _eyeCam.CopyFrom(cam);
        _eyeCam.enabled = false;
        _eyeCam.cullingMask |= 1 << UiCapture.VrOnlyLayer; // the controller laser
        var src = cam.GetUniversalAdditionalCameraData();
        if (src != null)
        {
            _eyeData.renderPostProcessing = src.renderPostProcessing;
            _eyeData.volumeLayerMask = src.volumeLayerMask;
            _eyeData.volumeTrigger = src.volumeTrigger;
            _eyeData.renderShadows = src.renderShadows;
            _eyeData.antialiasing = src.antialiasing;
            _eyeData.antialiasingQuality = src.antialiasingQuality;
            _eyeData.stopNaN = src.stopNaN;
            _eyeData.dithering = src.dithering;
            _eyeData.requiresDepthOption = src.requiresDepthOption;
            _eyeData.requiresColorOption = src.requiresColorOption;
            if (_eyeRendererIndex != src.m_RendererIndex) { _eyeRendererIndex = src.m_RendererIndex; _eyeData.SetRenderer(_eyeRendererIndex); }
        }

        float near = Mathf.Max(0.01f, VRConfig.NearClip.Value);
        float far = VRConfig.FarClip.Value > near ? VRConfig.FarClip.Value : cam.farClipPlane;
        Vector3 originPos = _origin.position; Quaternion originRot = _origin.rotation; float scale = _origin.localScale.x;
        bool flip = VRConfig.FlipY.Value;
        var et = _eyeCam.transform;
        _eyeCam.nearClipPlane = near; _eyeCam.farClipPlane = far;

        if (flat)
        {
            // The room canvas carries the room illustrations (behind a door) and, as nested canvases, the window views
            // (the landscape outside the blinds / curtains).
            float pictureDistance = 0f; bool isRoom = false;
            var rc = _uiCapture.RoomCanvas;
            if (rc != null)
            {
                // Windows first: the curtains window by the entrance also opens the room displayer.
                var rt = rc.transform;
                for (int i = 0; i < rt.childCount; i++)
                {
                    var child = rt.GetChild(i).gameObject;
                    if (child.activeSelf && child.name.EndsWith("WindowView")) { pictureDistance = VRConfig.WindowDistance.Value; break; }
                }
                var rd = rc.GetComponent<_Code.Infrastructure.Rooms.RoomDisplayer>();
                if (pictureDistance <= 0f && rd != null && rd._isOpened) { pictureDistance = VRConfig.RoomDistance.Value; isRoom = true; }
            }
            // Walking HUD: centred on the real head (position and direction), which is also where the interaction ray
            // goes; the game camera only matches it while the head sits exactly on the recenter point. In scripted
            // looks (window, dialog) it stays on the game camera so screen-projected markers match its view.
            Vector3 hudPos = _basePos; Quaternion hudRot = _baseRot;
            Vector3 fwd = _headT.forward;
            if (_follow && Mathf.Abs(fwd.y) < 0.999f) { hudPos = _headT.position; hudRot = Quaternion.LookRotation(fwd, Vector3.up); }
            _flatScreen.BeginEyeRender(_uiCapture.UiTexture, _uiCapture.RoomTexture, _basePos, _baseRot.eulerAngles.y, hudPos, hudRot,
                cam.fieldOfView, scale, pictureDistance, isRoom && VRConfig.RoomEnclosure.Value);
        }
        try
        {
            for (int eye = 0; eye < 2; eye++)
            {
                et.SetPositionAndRotation(originPos + originRot * (_eyePos[eye] * scale), originRot * _eyeRot[eye]);
                _eyeCam.targetTexture = _eyeRT[eye];
                _eyeCam.projectionMatrix = Projection(_views[eye].fov, near, far);
                _controllerVisuals.BeginEyes(false);
                _controlsCard.BeginEye(_headT, scale);
                _comfortOverlay.BeginEye(_eyeCam);
                _eyeCam.Render();
                _comfortOverlay.EndEye(); _controlsCard.EndEye();
                // Resolves MSAA and (for D3D) turns Unity's bottom-up render texture into the top-down image OpenXR expects.
                if (flip) Graphics.Blit(_eyeRT[eye], _submitRT[eye], new Vector2(1f, -1f), new Vector2(0f, 1f));
                else Graphics.Blit(_eyeRT[eye], _submitRT[eye]);
            }
        }
        finally
        {
            _flatScreen.EndEyeRender();
            _controllerVisuals.EndEyes(); _comfortOverlay.EndEye(); _controlsCard.EndEye();
            _eyeCam.targetTexture = null;
        }
        _framesRendered++;

        if (_camHasBrain && FlatScreen.WorldMode)
        {
            // Cinemachine rewrites the game camera every frame, so it can sit on the head between frames: the desktop
            // view, the audio listener and the game's centre-of-view interaction ray then all follow the head.
            t.SetPositionAndRotation(_headT.position, _headT.rotation);
            _wroteCamId = camId; _wrotePos = t.position; _wroteRot = t.rotation;
        }
        // Otherwise never touch it: self-driven cameras (menu, CameraRotate) read their own transform back, and in the
        // flat views (cursor free) the screen canvases were already laid out for the game's own camera pose.
        else _wroteCamId = 0;
        return true;
    }

    // ------------------------------------------------------------------ peephole
    // The game shows the peephole with a second camera (PeepholeCam: the street outside, the visitor sprite, the dialog
    // and a fisheye lens effect) drawn over the main one. In VR that picture is what one eye sees through the lens:
    // it is rendered into a texture and shown to one eye on a still screen in front of the player, the other eye is black.

    private Camera _peepCam;
    private RenderTexture _peepRT;
    private float _nextPeepScan;

    public bool PeepholeActive => _peepCam != null && _peepCam.isActiveAndEnabled;

    private bool UpdatePeephole()
    {
        if (_peepCam == null && Time.unscaledTime >= _nextPeepScan)
        {
            _nextPeepScan = Time.unscaledTime + 1f;
            foreach (var t in Resources.FindObjectsOfTypeAll<_Code.ActionObjects.LookAtObject.PeepholeTrigger>())
            {
                if (t == null || !t.gameObject.scene.IsValid() || t._peepholeCam == null) continue;
                _peepCam = t._peepholeCam.GetComponent<Camera>();
                break;
            }
        }
        bool active = PeepholeActive;
        Comfort.KeepPostEffects = active; // the fisheye and vignette are part of the picture
        if (!active) { _peepAnchored = false; _flatScreen.ScreenOverride = null; return false; }

        int w = Screen.width, h = Screen.height;
        if (_peepRT == null || _peepRT.width != w || _peepRT.height != h)
        {
            if (_peepRT != null) { _peepRT.Release(); Destroy(_peepRT); }
            _peepRT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "NIVR Peephole", hideFlags = HideFlags.HideAndDontSave };
            _peepRT.Create();
        }
        // Same size as the window, so the canvases attached to this camera keep their layout. The camera keeps
        // rendering on its own; the eye shows the previous frame's picture.
        if (_peepCam.targetTexture != _peepRT) _peepCam.targetTexture = _peepRT;
        return true;
    }

    // The picture hangs still in front of where the player faced when the peephole opened (a head-locked picture
    // was disorienting and its edges were out of view); it is drawn by the eye camera alone on its own layer.
    private const int PeepLayer = UiCapture.VrOnlyLayer;
    private GameObject _peepQuad;
    private Material _peepMat;
    private bool _peepAnchored;
    private Vector3 _peepAnchorPos; private Quaternion _peepAnchorRot;

    private void RenderPeephole()
    {
        int eye = VRConfig.PeepholeEye.Value == VRHand.Left ? 0 : 1;
        EnsureEyeCamera();
        if (_peepQuad == null)
        {
            _peepQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _peepQuad.name = "NIVR Peephole Picture";
            var col = _peepQuad.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _peepQuad.layer = PeepLayer;
            DontDestroyOnLoad(_peepQuad);
            _peepMat = new Material(Shader.Find("UI/Default")) { renderQueue = 3000 };
            _peepMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            var mr = _peepQuad.GetComponent<Renderer>();
            mr.sharedMaterial = _peepMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.enabled = false;
        }
        if (!_peepAnchored)
        {
            _peepAnchorPos = _headT.position;
            _peepAnchorRot = Quaternion.Euler(0f, _headT.eulerAngles.y, 0f);
            _peepAnchored = true;
        }
        float scale = _origin.localScale.x, dist = 2f * scale;
        float width = 2f * dist * Mathf.Tan(Mathf.Clamp(VRConfig.PeepholeFov.Value, 20f, 120f) * 0.5f * Mathf.Deg2Rad);
        _peepQuad.transform.SetPositionAndRotation(_peepAnchorPos + _peepAnchorRot * new Vector3(0f, 0f, dist), _peepAnchorRot);
        _peepQuad.transform.localScale = new Vector3(width, width * _peepRT.height / _peepRT.width, 1f);
        _peepMat.mainTexture = _peepRT;
        _flatScreen.ScreenOverride = _peepQuad.transform;

        Vector3 originPos = _origin.position; Quaternion originRot = _origin.rotation;
        var renderer = _peepQuad.GetComponent<Renderer>();
        _eyeCam.clearFlags = CameraClearFlags.SolidColor;
        _eyeCam.backgroundColor = Color.black;
        _eyeCam.cullingMask = 1 << PeepLayer; // the picture and the laser
        _eyeCam.allowHDR = false;
        if (_eyeData != null) _eyeData.renderPostProcessing = false;
        float near = Mathf.Max(0.01f, VRConfig.NearClip.Value), far = 100f;
        _eyeCam.nearClipPlane = near; _eyeCam.farClipPlane = far;
        _eyeCam.transform.SetPositionAndRotation(originPos + originRot * (_eyePos[eye] * scale), originRot * _eyeRot[eye]);
        _eyeCam.projectionMatrix = Projection(_views[eye].fov, near, far);
        _eyeCam.targetTexture = _eyeRT[eye];
        renderer.enabled = true;
        _controllerVisuals.EndEyes();
        _comfortOverlay.BeginEye(_eyeCam);
        try { _eyeCam.Render(); }
        finally { renderer.enabled = false; _eyeCam.targetTexture = null; _comfortOverlay.EndEye(); }
        Graphics.Blit(Texture2D.blackTexture, _eyeRT[1 - eye]);

        bool flip = VRConfig.FlipY.Value;
        for (int i = 0; i < 2; i++)
        {
            if (flip) Graphics.Blit(_eyeRT[i], _submitRT[i], new Vector2(1f, -1f), new Vector2(0f, 1f));
            else Graphics.Blit(_eyeRT[i], _submitRT[i]);
        }
    }

    private void EnsureEyeCamera()
    {
        if (_eyeCam != null) return;
        var go = new GameObject("NIVR Eye Camera");
        DontDestroyOnLoad(go);
        _eyeCam = go.AddComponent<Camera>();
        _eyeCam.enabled = false; // only ever rendered through Render()
        _eyeData = _eyeCam.GetUniversalAdditionalCameraData();
        _eyeRendererIndex = int.MinValue;
    }

    private void SubmitFrame(bool hasLayer)
    {
        var f = new FrameSubmit
        {
            displayTime = _frameState.predictedDisplayTime,
            hasLayer = hasLayer,
            space = _trackingSpace,
            pose0 = _views[0].pose, pose1 = _views[1].pose,
            fov0 = _views[0].fov, fov1 = _views[1].fov,
            src0 = _submitPtr[0], src1 = _submitPtr[1],
        };
        GL.IssuePluginEvent(XrSession.RenderCallback, _xr.QueueFrame(f));
        _frameWaited = false;
        _framesSubmitted++;

        if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
        if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
        if (!Application.runInBackground) Application.runInBackground = true;
    }

    // ------------------------------------------------------------------ end of frame

    private IEnumerator EndOfFrameLoop()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;
            try { EndOfFrame(); }
            catch (Exception e) { CorePlugin.LogThrottled("eof", "VR core end-of-frame failed: " + e); }
        }
    }

    private void EndOfFrame()
    {
        if (!VRRig.IsActive || _eyeRT[0] == null) return;

        if (PeepholeActive && _peepRT != null)
        {
            Graphics.Blit(_peepRT, (RenderTexture)null); // the desktop shows the peephole as the flat game does
        }
        else if (VRConfig.Mirror.Value == VRMirrorMode.LeftEye && _framesRendered > 0)
        {
            // Centre-crop the eye image to the window's aspect.
            float screen = (float)Screen.width / Screen.height, eye = (float)_eyeW / _eyeH;
            Vector2 s = screen > eye ? new Vector2(1f, eye / screen) : new Vector2(screen / eye, 1f);
            Graphics.Blit(_eyeRT[0], (RenderTexture)null, s, new Vector2((1f - s.x) * 0.5f, (1f - s.y) * 0.5f));
        }

        if (_captureName != null)
        {
            var name = _captureName; _captureName = null;
            var dir = Path.Combine(VRConfig.DebugCommandDir.Value, "..", "shots");
            Directory.CreateDirectory(dir);
            for (int i = 0; i < 2; i++) SavePng(_eyeRT[i], Path.Combine(dir, $"{name}_{(i == 0 ? "L" : "R")}.png"));
            SavePng(_submitRT[0], Path.Combine(dir, $"{name}_submitL.png"));
            SavePng(_submitRT[1], Path.Combine(dir, $"{name}_submitR.png"));
            if (_uiCapture.UiTexture != null) SavePng(_uiCapture.UiTexture, Path.Combine(dir, $"{name}_ui.png"));
            if (_uiCapture.RoomTexture != null) SavePng(_uiCapture.RoomTexture, Path.Combine(dir, $"{name}_room.png"));
        }
    }

    // ------------------------------------------------------------------ debug

    private void PollDebugCommands()
    {
        var dir = VRConfig.DebugCommandDir.Value;
        if (string.IsNullOrEmpty(dir) || Time.unscaledTime < _nextCmdPoll) return;
        _nextCmdPoll = Time.unscaledTime + 0.25f;
        string[] files;
        try { files = Directory.GetFiles(dir, "*.vr"); } catch { return; }
        foreach (var file in files)
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); File.Delete(file); } catch { continue; }
            foreach (var line in lines)
            {
                var p = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                CorePlugin.Log.LogInfo("vr> " + line.Trim());
                try { RunDebugCommand(p); } catch (Exception e) { CorePlugin.Log.LogError($"vr command failed: {e}"); }
            }
        }
    }

    private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private void RunDebugCommand(string[] p)
    {
        if (string.IsNullOrWhiteSpace(VRConfig.DebugCommandDir.Value)) return;
        switch (p[0].ToLowerInvariant())
        {
            case "settings": VrSettings.DebugCommand(p); break;
            case "focus": if (p.Length > 1 && p[1] == "lost") SessionFocus.Lost(); break;
            case "fade": _comfortOverlay.Jump(); break;
            case "subtitle":
                foreach (var subtitle in Resources.FindObjectsOfTypeAll<_Code.DialogSystem.SubtitlesView>())
                {
                    if (subtitle == null || !subtitle.gameObject.scene.IsValid()) continue;
                    if (p.Length > 1 && p[1] == "off") subtitle.Hide();
                    else subtitle.ShowDialogForTime("VR subtitle position check", Il2CppSystem.TimeSpan.FromSeconds(60));
                }
                break;
            case "headat": _simPos = _origin.InverseTransformPoint(new Vector3(F(p[1]), F(p[2]), F(p[3]))); break;
            case "geometry":
                var nearby = new System.Collections.Generic.List<(float distance, Vector3 point, Collider col)>();
                foreach (var collider in Resources.FindObjectsOfTypeAll<Collider>())
                {
                    if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger || collider.gameObject.layer != 0) continue;
                    var meshCollider = collider.TryCast<MeshCollider>();
                    if (meshCollider != null && !meshCollider.convex) continue;
                    if (Comfort.Player != null && collider.transform.IsChildOf(Comfort.Player.transform)) continue;
                    var closest = collider.ClosestPoint(_headT.position);
                    nearby.Add(((closest - _headT.position).magnitude, closest, collider));
                }
                nearby.Sort((a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < nearby.Count && i < 5; i++)
                    CorePlugin.Log.LogInfo($"geometry: {nearby[i].col.name} distance={nearby[i].distance:F2} point={nearby[i].point.ToString("F3")}");
                break;
            case "option": // development only, config key and value; exercises the live settings path without a headset
                if (VRConfig.File.TryGetEntry<string>(new BepInEx.Configuration.ConfigDefinition("Debug", "CommandDir"), out var debugEntry))
                {
                    foreach (var entry in VRConfig.File)
                        if (entry.Key.Key.Equals(p[1], StringComparison.OrdinalIgnoreCase) && entry.Key.Key != "CommandDir")
                        { VRConfig.File[entry.Key].SetSerializedValue(p[2]); break; }
                }
                break;
            case "eyes": _captureName = p.Length > 1 ? p[1] : "eyes"; break;
            case "recenter": Recenter(); break;
            case "simhead": // simhead x y z [yaw pitch roll]
                _simPos = new Vector3(F(p[1]), F(p[2]), F(p[3]));
                if (p.Length >= 7) _simRot = Quaternion.Euler(F(p[5]), F(p[4]), F(p[6]));
                break;
            case "turn":
                var pl = Comfort.Player;
                CorePlugin.Log.LogInfo($"turn {p[1]}: player yaw before {(pl != null ? pl.transform.eulerAngles.y : float.NaN):F1}, follow={_follow}");
                Turn(F(p[1]));
                break;
            case "mirror": VRConfig.Mirror.Value = Enum.Parse<VRMirrorMode>(p[1], true); break;
            case "mouselook": VRConfig.DisableMouseLook.Value = p[1] != "on"; break; // "mouselook on" = let the game rotate again
            case "flat": VRRig.FlatScreenMode = Enum.Parse<VRFlatScreenMode>(p[1], true); break;
            case "pad": // pad lx ly [rx ry [buttonBits]] [for <seconds>] | pad off   (drives the virtual gamepad without controllers)
                if (p[1] == "off") { _gamepad.DebugLeftStick = _gamepad.DebugRightStick = null; _gamepad.DebugButtons = 0; _padOffAt = 0f; break; }
                int forAt = Array.IndexOf(p, "for");
                int n = forAt > 0 ? forAt : p.Length;
                _gamepad.DebugLeftStick = new Vector2(F(p[1]), F(p[2]));
                if (n >= 5) _gamepad.DebugRightStick = new Vector2(F(p[3]), F(p[4]));
                _gamepad.DebugButtons = n >= 6 ? uint.Parse(p[5]) : 0;
                if (forAt > 0) { _padOffAt = Time.unscaledTime + F(p[forAt + 1]); _padOffFrame = Time.frameCount + 3; }
                break;
            case "aimat": // aimat r|l x y z: turn a simulated controller towards a world point
            case "aim":   // aim r|l u v: turn a simulated controller towards a point of the screen panel (u, v 0..1)
                int ah = p[1] == "l" ? 0 : 1;
                var aimAt = p[0] == "aimat" ? new Vector3(F(p[2]), F(p[3]), F(p[4])) : _flatScreen.PanelWorld(new Vector2(F(p[2]), F(p[3])));
                if (_simHand[ah].HasValue && aimAt.HasValue)
                {
                    var hv = _simHand[ah].Value;
                    Vector3 handWorld = _origin.TransformPoint(_simPos + _simRot * hv.pos);
                    Quaternion worldRot = Quaternion.LookRotation(aimAt.Value - handWorld, Vector3.up);
                    // aim pose (tracking space) = simRot * rot  ->  rot = simRot^-1 * origin^-1 * world
                    _simHand[ah] = (hv.pos, Quaternion.Inverse(_simRot) * Quaternion.Inverse(_origin.rotation) * worldRot, hv.trigger);
                }
                break;
            case "btn": // btn r|l <VRButton> <seconds>: hold a button of a simulated controller
                int bh = p[1] == "l" ? 0 : 1;
                _simButtons[bh][(int)Enum.Parse<VRButton>(p[2], true)] = Time.unscaledTime + F(p[3]);
                _simButtonFrame = Time.frameCount + 3;
                break;
            case "roll": // roll r|l <degrees>: twist a simulated controller about its pointing axis (clockwise positive)
                int rh = p[1] == "l" ? 0 : 1;
                if (_simHand[rh].HasValue) { var rv = _simHand[rh].Value; _simHand[rh] = (rv.pos, rv.rot * Quaternion.AngleAxis(-F(p[2]), Vector3.forward), rv.trigger); }
                break;
            case "trigger": // trigger r|l <seconds>: pull a simulated controller's trigger
                int th = p[1] == "l" ? 0 : 1;
                if (_simHand[th].HasValue) { var sv = _simHand[th].Value; _simHand[th] = (sv.pos, sv.rot, 1f); _triggerOffAt[th] = Time.unscaledTime + F(p[2]); _triggerOffFrame[th] = Time.frameCount + 3; }
                break;
            case "simhand": // simhand l|r x y z yaw pitch [trigger] (relative to the sim head) | simhand l|r off
                int sh = p[1] == "l" ? 0 : 1;
                if (p[2] == "off") { _simHand[sh] = null; break; }
                _simHand[sh] = (new Vector3(F(p[2]), F(p[3]), F(p[4])), Quaternion.Euler(F(p[6]), F(p[5]), 0f), p.Length > 7 ? F(p[7]) : 0f);
                break;
            case "trap": PauseGuard.DebugTrap(); break;
            case "trace": TraceSubmit = p.Length < 2 || p[1] != "off"; break;
            case "game":
                CorePlugin.Log.LogInfo("game: " + GameState.Describe() + $" mode={InputMode}");
                UiPointer.DebugHover = true;
                CorePlugin.Log.LogInfo("hover: " + UiPointer.HoverInfo);
                var rsrc = Comfort.RaySource;
                if (rsrc != null)
                {
                    var rc = rsrc._camera;
                    string hitInfo = "-";
                    if (rc != null && Physics.Raycast(new Ray(rc.transform.position, rc.transform.forward), out var dbgHit, 5f, rsrc._layerMask))
                        hitInfo = $"{dbgHit.collider.name} at {dbgHit.distance:F2} m";
                    CorePlugin.Log.LogInfo($"ray: cam={(rc != null ? rc.name : "null")} from={(rc != null ? rc.transform.position.ToString("F2") : "-")} dir={(rc != null ? rc.transform.forward.ToString("F2") : "-")} " +
                                           $"reach={rsrc._maxRayDistance:F2} mask={rsrc._layerMask} target={(rsrc._currentTarget != null ? rsrc._currentTarget.name : "none")} firstHit={hitInfo}");
                }
                break;
            case "scheme": // scheme pad|kbm: force the game's control scheme (development)
                var pi = GameState.Input != null ? GameState.Input._playerInput : null;
                if (pi == null) break;
                if (p[1] == "pad" && _gamepad.Device != null)
                    pi.SwitchCurrentControlScheme("Gaypad", new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<InputDevice>(new InputDevice[] { _gamepad.Device }));
                else
                    pi.SwitchCurrentControlScheme("Keyboard And Mouse", new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<InputDevice>(new InputDevice[] { Keyboard.current, Mouse.current }));
                CorePlugin.Log.LogInfo($"scheme now {pi.currentControlScheme}, neverAutoSwitch={pi.neverAutoSwitchControlSchemes}");
                break;
            case "follow": VRConfig.BodyFollowsHead.Value = p[1] == "on"; break;
            case "ray": // ray main|head|hand: where the interaction ray comes from
                VRConfig.InteractionRay.Value = p[1] == "main" ? VRInteractionRay.Game : p[1] == "head" ? VRInteractionRay.Head : VRInteractionRay.Controller;
                break;
            case "vr": _debugOff = p[1] == "off"; break; // "vr off": behave as if no headset (baseline comparisons)
            case "tp": // tp x y z yaw: move the player (development)
                var tpc = Comfort.Player;
                if (tpc == null) break;
                tpc._character.TeleportPosition(new Vector3(F(p[1]), F(p[2]), F(p[3])), false, true);
                tpc._character.TeleportRotation(Quaternion.Euler(0f, F(p[4]), 0f), false);
                if (_follow) _originYaw = F(p[4]) - VRRig.Head.LocalRotation.eulerAngles.y; // head ends up facing that way
                break;
            case "objects": // list the look-at objects (doors, windows, peephole)
            case "act":     // act <index>: use one of them
                var views = Resources.FindObjectsOfTypeAll<_Code.Infrastructure.ActionableObjects.AActionableObjectView>();
                for (int i = 0; i < views.Length; i++)
                {
                    var v = views[i];
                    if (v == null || !v.gameObject.scene.IsValid()) continue;
                    if (p[0] == "objects")
                        CorePlugin.Log.LogInfo($"  [{i}] {v.GetIl2CppType().Name} '{v.name}' parent='{(v.transform.parent != null ? v.transform.parent.name : "")}' active={v.gameObject.activeInHierarchy} pos={v.transform.position.ToString("F2")} " +
                                               $"stand={(v._standingPos != null ? v._standingPos.position.ToString("F2") : "-")} lookAt={(v._lookAtPos != null ? v._lookAtPos.position.ToString("F2") : "-")} fov={v._fov}");
                    else if (i == int.Parse(p[1])) { v.Act(); CorePlugin.Log.LogInfo($"Act() on {v.name}"); }
                }
                break;
            case "targets": // list the interaction ray targets and their collider boxes
                foreach (var tg in Resources.FindObjectsOfTypeAll<_Scripts.Raycast.ARaycastTarget>())
                {
                    if (tg == null || !tg.gameObject.scene.IsValid() || !tg.gameObject.activeInHierarchy) continue;
                    var tc = tg.GetComponent<Collider>();
                    CorePlugin.Log.LogInfo($"  target '{tg.name}' parent='{(tg.transform.parent != null ? tg.transform.parent.name : "")}' layer={tg.gameObject.layer} " +
                                           (tc != null ? $"center={tc.bounds.center.ToString("F2")} size={tc.bounds.size.ToString("F2")}" : "no collider"));
                }
                break;
            case "closeup": // closeup radio|phone|fridge [off]: open a close-up directly (development, sandboxed save only)
                _Code.Infrastructure.CloseUps.ACloseUpView view = null;
                _Code.Player.EWatcherState st = _Code.Player.EWatcherState.Radio;
                if (p[1] == "radio") { view = Resources.FindObjectsOfTypeAll<_Code.Infrastructure.CloseUps.Views.Radio.RadioCloseUpView>()[0]; st = _Code.Player.EWatcherState.Radio; }
                else if (p[1] == "phone") { view = Resources.FindObjectsOfTypeAll<_Code.Infrastructure.CloseUps.Views.Phone.PhoneCloseUpView>()[0]; st = _Code.Player.EWatcherState.Phone; }
                else if (p[1] == "fridge") { view = Resources.FindObjectsOfTypeAll<_Code.Infrastructure.CloseUps.Views.FridgeCloseUpView>()[0]; st = _Code.Player.EWatcherState.ConsumableController; }
                if (view == null) break;
                bool off = p.Length > 2 && p[2] == "off";
                if (off) { view.Hide(); GameState.Input?.SetIsInUIState(false); GameState.Watcher?.LeaveState(st); if (view.CursorController != null) view.CursorController.Lock(); }
                else { view.gameObject.SetActive(true); GameState.Input?.SetIsInUIState(true); GameState.Watcher?.EnterState(st); if (view.CursorController != null) view.CursorController.Unlock(); view.Show(); }
                CorePlugin.Log.LogInfo($"closeup {p[1]} {(off ? "hidden" : "shown")}");
                break;
            case "inter": // list the press-to-use objects (phone, radio, fridge...)
            case "use":   // use <index>: Interact() on one of them
                var items = Resources.FindObjectsOfTypeAll<_Code.Infrastructure.AInteractableObject>();
                for (int i = 0; i < items.Length; i++)
                {
                    var it = items[i];
                    if (it == null || !it.gameObject.scene.IsValid()) continue;
                    if (p[0] == "inter")
                        CorePlugin.Log.LogInfo($"  [{i}] {it.GetIl2CppType().Name} '{it.name}' parent='{(it.transform.parent != null ? it.transform.parent.name : "")}' active={it.gameObject.activeInHierarchy} pos={it.transform.position.ToString("F2")}");
                    else if (i == int.Parse(p[1])) { it.Interact(); CorePlugin.Log.LogInfo($"Interact() on {it.name}"); }
                }
                break;
            case "status":
                var cams = "";
                foreach (var c in Camera.allCameras) cams += $"{c.name}[{c.tag},depth {c.depth},fov {c.fieldOfView:F0}] ";
                CorePlugin.Log.LogInfo("cameras: " + cams);
                var cam = VRRig.GameCamera;
                var pc = Comfort.Player;
                CorePlugin.Log.LogInfo(
                    $"status: active={VRRig.IsActive} sim={_sim} xrState={_xr.State} running={_xr.Running} eye={_eyeW}x{_eyeH} " +
                    $"beforeRender={_beforeRenderCalls} rendered={_framesRendered} submitted={_framesSubmitted} fps={1f / Mathf.Max(1e-4f, _frameSeconds):F0} eyeCpuMs={_renderMs:F2} renderSubmitMs={_xr.RenderMilliseconds:F2} roomRenders={_uiCapture.RoomRenders} haptics={Haptics.Pulses} fade={_comfortOverlay.Alpha:F2} headBlocked={_comfortOverlay.Blocked}\n" +
                    $"  cam={(cam != null ? cam.name : "none")} brain={_camHasBrain} camPos={(cam != null ? cam.transform.position.ToString("F2") : "-")} camEuler={(cam != null ? cam.transform.eulerAngles.ToString("F1") : "-")}\n" +
                    $"  base={_basePos.ToString("F2")} baseEuler={_baseRot.eulerAngles.ToString("F1")} origin={_origin.position.ToString("F2")} originYaw={_origin.eulerAngles.y:F1}\n" +
                    $"  headLocal={VRRig.Head.LocalPosition.ToString("F2")} headWorld={_headT.position.ToString("F2")} headEuler={_headT.eulerAngles.ToString("F1")} tracked={VRRig.Head.IsTracked}\n" +
                    $"  follow={_follow} originYawState={_originYaw:F1} recenterYaw={_recenterYaw:F1}\n" +
                    $"  cursor={Cursor.lockState} overlayCanvasesConverted={_uiCapture.ConvertedCount} roomCanvas={(_uiCapture.RoomCanvas != null)}\n" +
                    $"  playerYaw={(pc != null ? pc.transform.eulerAngles.y : float.NaN):F1} comfort: {Comfort.Describe()} vsync={QualitySettings.vSyncCount}\n" +
                    $"  hudYaw={FlatScreen.HudYaw:F1} crouchAllowed={(pc != null && pc._character != null && pc._character.canEverCrouch)} crouched={(pc != null && pc._character != null && pc._character.IsCrouched())}\n" +
                    $"  game: {GameState.Describe()}");
                break;
            default: CorePlugin.Log.LogWarning("unknown vr command " + p[0]); break;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BlittableArrayWrapperNative { public IntPtr data; public int size; public int updateFlags; }

    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private delegate void EncodeToPngDelegate(IntPtr tex, out BlittableArrayWrapperNative ret);
    private static EncodeToPngDelegate s_encodeToPng;

    // ImageConversion.EncodeToPNG is unusable through the interop assemblies (docs/DEV_NOTES.md 1.4 issue 2); call the icall directly.
    private static void SavePng(RenderTexture rt, string path)
    {
        var prev = RenderTexture.active;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            s_encodeToPng ??= IL2CPP.ResolveICall<EncodeToPngDelegate>("UnityEngine.ImageConversion::EncodeToPNG_Injected");
            s_encodeToPng(tex.m_CachedPtr, out var ret); // the native object, not the il2cpp wrapper
            if (ret.data == IntPtr.Zero || ret.size <= 0) throw new InvalidOperationException("EncodeToPNG returned nothing");
            var src = ret.updateFlags == 3 ? System.Runtime.InteropServices.Marshal.ReadIntPtr(ret.data) : ret.data;
            var bytes = new byte[ret.size];
            System.Runtime.InteropServices.Marshal.Copy(src, bytes, 0, ret.size);
            File.WriteAllBytes(path, bytes);
            CorePlugin.Log.LogInfo($"eye capture -> {path} ({rt.width}x{rt.height})");
        }
        finally
        {
            RenderTexture.active = prev;
            Destroy(tex);
        }
    }
}
