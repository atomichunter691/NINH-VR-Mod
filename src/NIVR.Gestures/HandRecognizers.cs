using System;
using System.Collections.Generic;
using System.Numerics;

namespace NIVR.Gestures;

public enum PushPull { None, Push, Pull }

/// <summary>
/// A hand moved a set distance along an axis after touching something: push a door (against the surface normal) or
/// pull it (along the normal). Fires once per engagement; the hand has to leave and come back to fire again.
/// A brush sideways across the surface does not fire: sideways travel beyond <see cref="MaxDrift"/> cancels.
/// </summary>
public sealed class PushPullRecognizer
{
    private bool _engaged, _done;
    private Vector3 _start;

    /// <summary>Travel along the axis that counts as intent.</summary>
    public float Distance { get; set; } = 0.10f;
    /// <summary>Travel across the axis that cancels the attempt.</summary>
    public float MaxDrift { get; set; } = 0.15f;
    /// <summary>Signed travel along the normal since the hand engaged (+ = pull, towards the player).</summary>
    public float Travel { get; private set; }

    /// <param name="engaged">The hand is in the target's zone (and, where the design asks for it, gripping).</param>
    /// <param name="surfaceNormal">Points from the surface towards the player.</param>
    public PushPull Update(bool engaged, Vector3 hand, Vector3 surfaceNormal)
    {
        if (!engaged) { _engaged = _done = false; Travel = 0f; return PushPull.None; }
        if (!_engaged) { _engaged = true; _done = false; _start = hand; Travel = 0f; return PushPull.None; }
        if (_done) return PushPull.None;

        var n = Zones.SafeNormalize(surfaceNormal);
        var move = hand - _start;
        Travel = Vector3.Dot(move, n);
        if ((move - n * Travel).Length() > MaxDrift) { _start = hand; Travel = 0f; return PushPull.None; } // start over from here
        if (Travel <= -Distance) { _done = true; return PushPull.Push; }
        if (Travel >= Distance) { _done = true; return PushPull.Pull; }
        return PushPull.None;
    }

    public void Reset() { _engaged = _done = false; Travel = 0f; }
}

public enum GrabEvent { None, Grabbed, HoldCompleted, Released }

/// <summary>
/// Grip closed while the hand is in a zone, then kept closed. "Grabbed" on closing, "HoldCompleted" once after
/// <see cref="HoldSeconds"/>, "Released" on opening. A grip that was already closed when the hand arrived does not
/// grab (walking through a doorway with a fist is not a grab), and the grab survives the hand leaving the zone, so a
/// pulled blind stays held.
/// </summary>
public sealed class GrabHoldRecognizer
{
    private bool _pressed, _armed, _completed;

    public float PressThreshold { get; set; } = 0.6f;
    public float ReleaseThreshold { get; set; } = 0.4f;
    public float HoldSeconds { get; set; } = 0.5f;
    public bool IsGrabbing { get; private set; }
    public float HeldSeconds { get; private set; }

    public GrabEvent Update(float dt, bool inZone, float grip)
    {
        bool pressed = Zones.Hysteresis(_pressed, grip, PressThreshold, ReleaseThreshold);
        bool down = pressed && !_pressed, up = !pressed && _pressed;
        _pressed = pressed;

        if (!IsGrabbing)
        {
            // Armed = the grip was seen open while in the zone.
            if (!inZone) { _armed = false; return GrabEvent.None; }
            if (!pressed) { _armed = true; return GrabEvent.None; }
            if (!down || !_armed) return GrabEvent.None;
            IsGrabbing = true; _completed = false; HeldSeconds = 0f;
            return GrabEvent.Grabbed;
        }

        if (up) { IsGrabbing = false; _armed = inZone; HeldSeconds = 0f; return GrabEvent.Released; }
        HeldSeconds += dt;
        if (!_completed && HeldSeconds >= HoldSeconds) { _completed = true; return GrabEvent.HoldCompleted; }
        return GrabEvent.None;
    }

    public void Reset() { _pressed = _armed = _completed = IsGrabbing = false; HeldSeconds = 0f; }
}

/// <summary>
/// Stroking: the hand stays near a surface and slides along it, back and forth allowed, at a gentle speed. Fires each
/// time the slid distance reaches <see cref="Length"/>. A fast hand is a hit, not a stroke, and resets the count, as
/// does leaving the zone for longer than <see cref="Grace"/>.
/// </summary>
public sealed class StrokeRecognizer
{
    private float _outside;

    public float Length { get; set; } = 0.25f;
    public float MinSpeed { get; set; } = 0.05f;
    public float MaxSpeed { get; set; } = 1.2f;
    public float Grace { get; set; } = 0.3f;
    /// <summary>Distance slid along the surface so far.</summary>
    public float Progress { get; private set; }

    /// <param name="surfaceNormal">Any length; zero means "no surface, count all movement".</param>
    public bool Update(float dt, bool inZone, Vector3 velocity, Vector3 surfaceNormal)
    {
        if (!inZone)
        {
            _outside += dt;
            if (_outside > Grace) Progress = 0f;
            return false;
        }
        _outside = 0f;
        if (velocity.Length() > MaxSpeed) { Progress = 0f; return false; }
        var n = Zones.SafeNormalize(surfaceNormal);
        float along = (velocity - n * Vector3.Dot(velocity, n)).Length();
        if (along < MinSpeed) return false;
        Progress += along * dt;
        if (Progress < Length) return false;
        Progress = 0f;
        return true;
    }

    public void Reset() { Progress = 0f; _outside = 0f; }
}

/// <summary>
/// Throw: the grip opens while the hand is moving fast. The release velocity is the average over the last
/// <see cref="Window"/> seconds before the release, because the single frame of the release is usually already slowing.
/// </summary>
public sealed class ThrowRecognizer
{
    private readonly Queue<(float age, Vector3 v)> _recent = new();
    private bool _pressed;

    public float MinSpeed { get; set; } = 1.5f;
    public float Window { get; set; } = 0.1f;
    public float PressThreshold { get; set; } = 0.6f;
    public float ReleaseThreshold { get; set; } = 0.4f;
    public bool IsHolding => _pressed;

    public bool Update(float dt, float grip, Vector3 velocity, out Vector3 releaseVelocity)
    {
        releaseVelocity = Vector3.Zero;
        bool pressed = Zones.Hysteresis(_pressed, grip, PressThreshold, ReleaseThreshold);
        bool released = !pressed && _pressed;
        _pressed = pressed;

        if (pressed)
        {
            // Ages are measured back from "now"; drop what fell out of the window.
            int n = _recent.Count;
            for (int i = 0; i < n; i++)
            {
                var (age, v) = _recent.Dequeue();
                if (age + dt <= Window) _recent.Enqueue((age + dt, v));
            }
            _recent.Enqueue((0f, velocity));
            return false;
        }
        if (!released) { _recent.Clear(); return false; }

        var sum = velocity; int count = 1;
        foreach (var (_, v) in _recent) { sum += v; count++; }
        _recent.Clear();
        releaseVelocity = sum / count;
        if (releaseVelocity.Length() >= MinSpeed) return true;
        releaseVelocity = Vector3.Zero;
        return false;
    }

    public void Reset() { _recent.Clear(); _pressed = false; }
}

public enum RaiseHandsEvent { None, Raised, Lowered }

/// <summary>
/// Both hands held up in front of the body and kept still: at or above chest height, in front of the head, apart but
/// not spread wide. "Raised" fires once after <see cref="HoldSeconds"/>; "Lowered" when the pose is left.
/// </summary>
public sealed class RaiseHandsRecognizer
{
    private float _held;

    /// <summary>How far below the head a hand may be (chest height is about 0.35 m below the eyes).</summary>
    public float MaxBelowHead { get; set; } = 0.35f;
    /// <summary>Least distance in front of the head, measured along the head's horizontal forward.</summary>
    public float MinForward { get; set; } = 0.15f;
    public float MinSeparation { get; set; } = 0.15f;
    public float MaxSeparation { get; set; } = 0.9f;
    /// <summary>A waving hand is not a shown hand.</summary>
    public float MaxSpeed { get; set; } = 0.6f;
    public float HoldSeconds { get; set; } = 0.4f;
    public bool IsRaised { get; private set; }

    public RaiseHandsEvent Update(float dt, Vector3 head, Vector3 headForward, bool leftTracked, Vector3 left, Vector3 leftVelocity,
        bool rightTracked, Vector3 right, Vector3 rightVelocity)
    {
        var forward = Zones.SafeNormalize(new Vector3(headForward.X, 0f, headForward.Z));
        bool pose = leftTracked && rightTracked && forward != Vector3.Zero
            && Up(head, forward, left) && Up(head, forward, right)
            && leftVelocity.Length() <= MaxSpeed && rightVelocity.Length() <= MaxSpeed;
        if (pose)
        {
            float apart = Vector3.Distance(left, right);
            pose = apart >= MinSeparation && apart <= MaxSeparation;
        }

        if (!pose)
        {
            _held = 0f;
            if (!IsRaised) return RaiseHandsEvent.None;
            IsRaised = false;
            return RaiseHandsEvent.Lowered;
        }
        if (IsRaised) return RaiseHandsEvent.None;
        _held += dt;
        if (_held < HoldSeconds) return RaiseHandsEvent.None;
        IsRaised = true;
        return RaiseHandsEvent.Raised;
    }

    private bool Up(Vector3 head, Vector3 forward, Vector3 hand)
        => hand.Y >= head.Y - MaxBelowHead && Vector3.Dot(hand - head, forward) >= MinForward;

    public void Reset() { _held = 0f; IsRaised = false; }
}
