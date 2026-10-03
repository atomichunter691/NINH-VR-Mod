using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace NIVR.Core;

/// <summary>
/// Presents the VR controllers to the game as an ordinary gamepad (an Input System "Gamepad" device fed with state
/// events), so the game's own controller scheme drives everything: left stick walks, A interacts, the right stick
/// moves the game's gamepad cursor in menus, and button prompts switch to controller glyphs.
///
///   left stick  -> left stick (move / navigate)        right stick -> right stick (UI cursor, scroll; look is disabled)
///   A, R trigger-> South (interact / select / submit)  B           -> East (back / cancel)
///   X, L trigger-> West (skip dialog line)             Y           -> North (tutorial / hint)
///   left Menu   -> Start (pause)                       R stick click -> crouch, L stick click -> left stick press
///   R grip      -> right trigger + RB (run, speed-up)  L grip      -> left trigger + LB
/// </summary>
internal sealed unsafe class VirtualGamepad
{
    // UnityEngine.InputSystem.LowLevel.StateEvent carrying a GamepadState ('GPAD'), laid out by hand because the
    // generic InputSystem.QueueStateEvent<T> has no AOT instance for a plugin to call.
    [StructLayout(LayoutKind.Explicit, Size = 52)]
    private struct GamepadStateEvent
    {
        [FieldOffset(0)] public int type;          // 'STAT'
        [FieldOffset(4)] public ushort sizeInBytes;
        [FieldOffset(6)] public ushort deviceId;
        [FieldOffset(8)] public double time;
        [FieldOffset(16)] public int eventId;
        [FieldOffset(20)] public int stateFormat;  // 'GPAD'
        [FieldOffset(24)] public uint buttons;
        [FieldOffset(28)] public Vector2 leftStick;
        [FieldOffset(36)] public Vector2 rightStick;
        [FieldOffset(44)] public float leftTrigger;
        [FieldOffset(48)] public float rightTrigger;
    }

    private const int Stat = ('S' << 24) | ('T' << 16) | ('A' << 8) | 'T';
    private const int Gpad = ('G' << 24) | ('P' << 16) | ('A' << 8) | 'D';
    private const uint North = 1u << 4, East = 1u << 5, South = 1u << 6, West = 1u << 7, LeftStick = 1u << 8, RightStick = 1u << 9,
        LeftShoulder = 1u << 10, RightShoulder = 1u << 11, Start = 1u << 12;

    private InputDevice _device;
    public InputDevice Device => _device;
    private bool _failed, _wasFeeding;
    private InputSettings.BackgroundBehavior _savedBackground;
    private bool _backgroundSaved;

    /// <summary>Debug override (sim): when set, this state is sent instead of the controllers'.</summary>
    public Vector2? DebugLeftStick, DebugRightStick;
    public uint DebugButtons;

    // Radio knob: the game turns it by the change of the right stick's angle (stick rotated in a circle). Holding the
    // trigger and twisting the controller produces the same: a full-deflection stick whose angle follows the twist.
    private const float TwistGain = 3f;
    private bool _twisting;
    private float _twistStickAngle;
    private Quaternion _twistLastRot;
    private float _twistDetent;
    private bool _submitHeld;
    public static bool DebugNavigating { get; private set; }

    /// <param name="zeroRightStick">the right stick is snap turn (hallway)</param>
    /// <param name="silent">the controllers drive the virtual mouse / keyboard instead (pointer mode)</param>
    /// <param name="knobTwist">radio: trigger + twist turns the knob</param>
    /// <param name="pointerHand">its trigger is A (interact); the other trigger is X (skip dialog line)</param>
    public void Tick(bool vrActive, bool zeroRightStick, bool silent = false, bool knobTwist = false, VRHand pointerHand = VRHand.Right)
    {
        if (_failed) return;
        var l = VRRig.LeftController; var r = VRRig.RightController;
        bool debug = DebugLeftStick.HasValue || DebugRightStick.HasValue || DebugButtons != 0;
        bool feed = vrActive && VRConfig.ControllerAsGamepad.Value && (l.IsConnected || r.IsConnected || debug) && !(silent && !debug);
        try
        {
            if (!feed)
            {
                if (_wasFeeding) { Send(default); _wasFeeding = false; } // release everything once
                _submitHeld = _twisting = false; DebugNavigating = false;
                return;
            }
            if (_device == null)
            {
                _device = InputSystem.AddDevice("Gamepad", "NIVR VR Controllers");
                CorePlugin.Log.LogInfo($"Virtual gamepad added (device id {_device.deviceId}).");
            }
            if (!_backgroundSaved)
            {
                // The game window rarely has focus while the headset is on; keep input flowing.
                _savedBackground = InputSystem.settings.backgroundBehavior; _backgroundSaved = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            }

            var e = new GamepadStateEvent { leftStick = DebugLeftStick ?? l.Stick, rightStick = DebugRightStick ?? r.Stick };
            DebugNavigating = debug && (e.leftStick.sqrMagnitude > 0.25f || e.rightStick.sqrMagnitude > 0.25f);
            // Walking the hallway: the right stick is snap turn only (the game's look is switched off anyway).
            if (zeroRightStick && !DebugRightStick.HasValue) e.rightStick = Vector2.zero;
            var p = VRRig.Controller(pointerHand); var o = VRRig.Controller(pointerHand == VRHand.Right ? VRHand.Left : VRHand.Right);
            bool twist = knobTwist && p.IsTracked && p.Aim != null && ButtonGate.Down(p, VRButton.Trigger) && e.rightStick.sqrMagnitude < 0.25f;
            if (twist)
            {
                var rot = p.Aim.rotation;
                if (!_twisting) { _twisting = true; _twistStickAngle = 90f; _twistDetent = 0f; }
                else
                {
                    // Roll of the controller since last frame (rotation about its pointing axis). Sign chosen (in the
                    // simulator) so a clockwise twist moves the tuning needle right, like a real radio dial.
                    (rot * Quaternion.Inverse(_twistLastRot)).ToAngleAxis(out float ang, out Vector3 axis);
                    if (ang > 180f) ang -= 360f;
                    float delta = ang * Vector3.Dot(axis, p.Aim.forward);
                    _twistStickAngle += TwistGain * delta;
                    _twistDetent += delta;
                    if (Mathf.Abs(_twistDetent) >= VRConfig.RadioDetentDegrees.Value)
                    { Haptics.Tick(p.Hand, "radio-detent", 0.18f, 0.02f); _twistDetent %= VRConfig.RadioDetentDegrees.Value; }
                }
                _twistLastRot = rot;
                float rad = _twistStickAngle * Mathf.Deg2Rad;
                e.rightStick = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 0.95f;
            }
            else _twisting = false;
            uint b = DebugButtons;
            if (ButtonGate.Down(r, VRButton.Primary) || (!twist && ButtonGate.Down(p, VRButton.Trigger))) b |= South;
            if (ButtonGate.Down(r, VRButton.Secondary)) b |= East;
            if (ButtonGate.Down(l, VRButton.Primary) || (!knobTwist && ButtonGate.Down(o, VRButton.Trigger))) b |= West;
            if (ButtonGate.Down(l, VRButton.Secondary)) b |= North;
            if (ButtonGate.Down(l, VRButton.Menu)) b |= Start;
            if (ButtonGate.Down(l, VRButton.Stick)) b |= LeftStick;
            if (ButtonGate.Down(r, VRButton.Stick)) b |= RightStick;
            if (ButtonGate.Down(l, VRButton.Grip)) b |= LeftShoulder;
            if (ButtonGate.Down(r, VRButton.Grip)) b |= RightShoulder;
            e.buttons = b;
            bool submit = (b & South) != 0;
            if (submit && !_submitHeld) Haptics.Tick(pointerHand, "submit", 0.4f, 0.04f);
            _submitHeld = submit;
            e.leftTrigger = ButtonGate.Analog(l, VRButton.Grip, l.GripValue); e.rightTrigger = ButtonGate.Analog(r, VRButton.Grip, r.GripValue);
            Send(e);
            _wasFeeding = true;
        }
        catch (Exception ex)
        {
            _failed = true;
            CorePlugin.Log.LogError("Virtual gamepad failed; controllers will not drive the game: " + ex);
        }
    }

    private void Send(GamepadStateEvent e)
    {
        if (_device == null) return;
        e.type = Stat; e.stateFormat = Gpad;
        e.sizeInBytes = 52; e.deviceId = (ushort)_device.deviceId;
        e.time = InputState.currentTime;
        InputSystem.QueueEvent(new InputEventPtr((InputEvent*)&e));
    }

    public void Restore()
    {
        try
        {
            if (_wasFeeding) { Send(default); _wasFeeding = false; }
            if (_backgroundSaved) { InputSystem.settings.backgroundBehavior = _savedBackground; _backgroundSaved = false; }
        }
        catch (Exception) { }
    }
    public void Dispose() { Restore(); if (_device != null) { InputSystem.RemoveDevice(_device); _device = null; } }
}
