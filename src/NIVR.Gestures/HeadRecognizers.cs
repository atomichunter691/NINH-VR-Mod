using System;
using System.Numerics;

namespace NIVR.Gestures;

public enum LeanEvent { None, Entered, Exited }

/// <summary>
/// Lean-in: the head comes close to a point (the peephole) while facing it, and stays for a moment. Leaving needs a
/// larger distance than entering, so the edge does not flicker.
/// </summary>
public sealed class LeanInRecognizer
{
    private float _dwell;

    public float EnterRadius { get; set; } = 0.18f;
    public float ExitRadius { get; set; } = 0.30f;
    public float DwellSeconds { get; set; } = 0.25f;
    /// <summary>Largest angle between the head's forward and the direction into the target, in radians.</summary>
    public float MaxAngle { get; set; } = MathF.PI / 3f;
    public bool IsIn { get; private set; }

    /// <param name="into">Direction the player has to look to see into the target (for the peephole: through the door).</param>
    public LeanEvent Update(float dt, Vector3 head, Vector3 headForward, Vector3 target, Vector3 into)
    {
        float distance = Vector3.Distance(head, target);
        if (IsIn)
        {
            if (distance <= ExitRadius) return LeanEvent.None;
            IsIn = false; _dwell = 0f;
            return LeanEvent.Exited;
        }

        var f = Zones.SafeNormalize(headForward); var d = Zones.SafeNormalize(into);
        bool facing = d == Vector3.Zero || Vector3.Dot(f, d) >= MathF.Cos(MaxAngle);
        if (distance > EnterRadius || !facing) { _dwell = 0f; return LeanEvent.None; }
        _dwell += dt;
        if (_dwell < DwellSeconds) return LeanEvent.None;
        IsIn = true;
        return LeanEvent.Entered;
    }

    public void Reset() { IsIn = false; _dwell = 0f; }
}

public enum HeadGesture { None, Nod, Shake }

/// <summary>
/// Nod (yes) and shake (no) from the head's forward direction. A gesture is a run of swings on one axis: the head
/// travels at least <see cref="MinSwing"/> one way, then reverses. <see cref="Swings"/> reversals inside
/// <see cref="Window"/> seconds make a gesture, provided the other axis moved less (looking around is neither).
/// After firing nothing is recognized for <see cref="Cooldown"/> seconds.
/// </summary>
public sealed class HeadGestureRecognizer
{
    private Axis _pitch, _yaw;
    private float _time, _quietUntil;
    private bool _hasLast; private float _lastPitch, _lastYaw;

    /// <summary>Smallest swing that counts, radians (default 8 degrees).</summary>
    public float MinSwing { get; set; } = 8f * MathF.PI / 180f;
    public int Swings { get; set; } = 3;
    public float Window { get; set; } = 1.5f;
    public float Cooldown { get; set; } = 1.0f;

    public HeadGesture Update(float dt, Vector3 headForward)
    {
        _time += dt;
        var f = Zones.SafeNormalize(headForward);
        if (f == Vector3.Zero) return HeadGesture.None;
        float pitch = MathF.Asin(Math.Clamp(f.Y, -1f, 1f));
        float yaw = MathF.Atan2(f.X, f.Z);
        if (!_hasLast) { _hasLast = true; _lastPitch = pitch; _lastYaw = yaw; return HeadGesture.None; }

        float dPitch = pitch - _lastPitch, dYaw = yaw - _lastYaw;
        if (dYaw > MathF.PI) dYaw -= 2f * MathF.PI; else if (dYaw < -MathF.PI) dYaw += 2f * MathF.PI;
        _lastPitch = pitch; _lastYaw = yaw;

        _pitch.Step(dPitch, _time, MinSwing, Window);
        _yaw.Step(dYaw, _time, MinSwing, Window);
        if (_time < _quietUntil) return HeadGesture.None;

        HeadGesture g = HeadGesture.None;
        if (_pitch.Count >= Swings && _pitch.Count > _yaw.Count) g = HeadGesture.Nod;
        else if (_yaw.Count >= Swings && _yaw.Count > _pitch.Count) g = HeadGesture.Shake;
        if (g == HeadGesture.None) return g;
        _pitch.Clear(); _yaw.Clear();
        _quietUntil = _time + Cooldown;
        return g;
    }

    public void Reset() { _pitch.Clear(); _yaw.Clear(); _hasLast = false; _quietUntil = 0f; }

    /// <summary>
    /// Counts the swings of one angle and forgets old ones. A swing ends when the angle has come back from its
    /// furthest point by half of the minimum swing, so tracker jitter in the middle of a movement is not a reversal.
    /// </summary>
    private struct Axis
    {
        private float _angle, _pivot, _extreme; // accumulated angle, last turning point, furthest point since then
        private int _direction;                 // of the current swing; 0 = not moving yet
        private float _first;                   // time of the oldest counted swing
        public int Count;

        public void Step(float delta, float now, float minSwing, float window)
        {
            if (Count > 0 && now - _first > window) Clear();
            _angle += delta;
            float turn = minSwing * 0.5f;
            if (_direction == 0)
            {
                if (MathF.Abs(_angle - _pivot) < turn) return;
                _direction = MathF.Sign(_angle - _pivot); _extreme = _angle;
                return;
            }
            if ((_angle - _extreme) * _direction > 0f) { _extreme = _angle; return; }
            if ((_extreme - _angle) * _direction < turn) return;
            if (MathF.Abs(_extreme - _pivot) >= minSwing)
            {
                if (Count == 0) _first = now;
                Count++;
            }
            _pivot = _extreme; _direction = -_direction; _extreme = _angle;
        }

        public void Clear() { Count = 0; _first = 0f; _pivot = _angle; _extreme = _angle; _direction = 0; }
    }
}
