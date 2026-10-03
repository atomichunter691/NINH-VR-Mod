using System;
using UnityEngine;

namespace NIVR.Core;

public enum VRHand { Left = 0, Right = 1 }

/// <summary>
/// Trigger/Grip are "pressed" above 0.6 and released below 0.4. Primary = A (right) / X (left), Secondary = B / Y.
/// StickLeft..StickDown are the thumbstick pushed past 0.6 in that direction (released below 0.4).
/// </summary>
public enum VRButton { Trigger, Grip, Primary, Secondary, Menu, Stick, StickLeft, StickRight, StickUp, StickDown }

/// <summary>The tracked head. <see cref="Transform"/> is in world space and valid whenever <see cref="VRRig.IsActive"/>.</summary>
public sealed class VRHead
{
    /// <summary>World-space head (centre between the eyes). Child of <see cref="VRRig.Origin"/>; read-only for other modules.</summary>
    public Transform Transform { get; internal set; }
    public bool IsTracked { get; internal set; }
    /// <summary>Head pose in tracking space (metres, before recenter and world scale).</summary>
    public Vector3 LocalPosition { get; internal set; }
    public Quaternion LocalRotation { get; internal set; } = Quaternion.identity;
}

public sealed class VRController
{
    private const int ButtonCount = 10;
    private readonly bool[] _now = new bool[ButtonCount], _prev = new bool[ButtonCount];

    internal VRController(VRHand hand) { Hand = hand; }

    public VRHand Hand { get; }
    /// <summary>A controller is connected and reporting input.</summary>
    public bool IsConnected { get; internal set; }
    /// <summary>The pose transforms below are being updated from tracking this frame.</summary>
    public bool IsTracked { get; internal set; }
    /// <summary>World-space pointing pose (ray along +Z / forward). Child of <see cref="VRRig.Origin"/>.</summary>
    public bool IsAimTracked { get; internal set; }
    public bool IsGripTracked { get; internal set; }
    public Transform Aim { get; internal set; }
    /// <summary>World-space grip pose (where the hand holds the controller). Child of <see cref="VRRig.Origin"/>.</summary>
    public Transform Grip { get; internal set; }
    /// <summary>Index trigger, 0..1.</summary>
    public float Trigger { get; internal set; }
    /// <summary>Grip / squeeze, 0..1.</summary>
    public float GripValue { get; internal set; }
    public Vector2 Stick { get; internal set; }

    public bool GetButton(VRButton b) => _now[(int)b];
    public bool GetButtonDown(VRButton b) => _now[(int)b] && !_prev[(int)b];
    public bool GetButtonUp(VRButton b) => !_now[(int)b] && _prev[(int)b];

    /// <summary>Vibrate. amplitude 0..1; seconds &lt;= 0 gives the shortest pulse; frequency 0 = runtime default.</summary>
    public void Haptic(float amplitude, float seconds = 0.05f, float frequency = 0f)
        => VRRig.HapticImpl?.Invoke(Hand, Mathf.Clamp01(amplitude * VRConfig.VibrationStrength.Value), seconds, frequency);

    internal void BeginFrame() => Array.Copy(_now, _prev, ButtonCount);
    internal void Set(VRButton b, bool value) => _now[(int)b] = value;
    /// <summary>Analog value to button with hysteresis.</summary>
    internal void SetAnalog(VRButton b, float value) => _now[(int)b] = value > (_prev[(int)b] ? 0.4f : 0.6f);
}

/// <summary>
/// Public API of the VR core for other NIVR modules. Everything is updated in Core's Update (poses are refreshed again
/// just before rendering). Read these from Update/LateUpdate; do not move or reparent the rig transforms.
/// </summary>
public static class VRRig
{
    /// <summary>True while the game is being rendered in stereo (headset session running, or the simulator).</summary>
    public static bool IsActive { get; internal set; }
    /// <summary>True when running without a headset (Backend = Simulator).</summary>
    public static bool IsSimulated { get; internal set; }

    public static VRHead Head { get; } = new();
    public static VRController LeftController { get; } = new(VRHand.Left);
    public static VRController RightController { get; } = new(VRHand.Right);
    public static VRController Controller(VRHand hand) => hand == VRHand.Left ? LeftController : RightController;

    /// <summary>World-space origin of the tracking space (parent of head and controllers). Scale = WorldScale.</summary>
    public static Transform Origin { get; internal set; }
    /// <summary>The game camera currently being rendered to the headset (Camera.main), or null.</summary>
    public static Camera GameCamera { get; internal set; }
    /// <summary>Where the game wants the camera this frame (before head tracking is applied on top).</summary>
    public static Vector3 GameCameraPosition { get; internal set; }
    public static Quaternion GameCameraRotation { get; internal set; } = Quaternion.identity;

    /// <summary>Set to override the config's flat-screen mode (e.g. Off once a UI module shows the canvases itself); null = use the config.</summary>
    public static VRFlatScreenMode? FlatScreenMode { get; set; }

    /// <summary>Raised from Core's Update, after poses were refreshed.</summary>
    public static event Action<VRHand, VRButton> ButtonPressed;
    public static event Action<VRHand, VRButton> ButtonReleased;
    /// <summary>Raised after the view was recentered.</summary>
    public static event Action Recentered;
    /// <summary>Raised when <see cref="IsActive"/> changes (argument = new value).</summary>
    public static event Action<bool> ActiveChanged;

    /// <summary>Make the current head position/heading the game's "looking straight ahead from the eye point".</summary>
    public static void Recenter() => RecenterImpl?.Invoke();

    /// <summary>Last rendered eye image (0 = left, 1 = right), upright, sRGB. Null when not active.</summary>
    public static RenderTexture GetEyeTexture(int eye) => EyeTextureImpl?.Invoke(eye);

    internal static Action RecenterImpl;
    internal static Func<int, RenderTexture> EyeTextureImpl;
    internal static Action<VRHand, float, float, float> HapticImpl;

    internal static void RaiseButtons()
    {
        for (int h = 0; h < 2; h++)
        {
            var c = Controller((VRHand)h);
            for (int b = 0; b < 10; b++)
            {
                try
                {
                    if (c.GetButtonDown((VRButton)b)) ButtonPressed?.Invoke(c.Hand, (VRButton)b);
                    else if (c.GetButtonUp((VRButton)b)) ButtonReleased?.Invoke(c.Hand, (VRButton)b);
                }
                catch (Exception e) { CorePlugin.Log.LogError($"VRRig button handler threw: {e}"); }
            }
        }
    }

    internal static void RaiseRecentered()
    {
        try { Recentered?.Invoke(); } catch (Exception e) { CorePlugin.Log.LogError($"VRRig.Recentered handler threw: {e}"); }
    }

    internal static void RaiseActiveChanged(bool active)
    {
        try { ActiveChanged?.Invoke(active); } catch (Exception e) { CorePlugin.Log.LogError($"VRRig.ActiveChanged handler threw: {e}"); }
    }
}
