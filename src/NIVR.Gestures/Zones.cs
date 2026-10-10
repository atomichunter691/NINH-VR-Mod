using System;
using System.Numerics;

namespace NIVR.Gestures;

/// <summary>
/// Conventions for the whole library: metres, seconds, radians per second for angular velocity, Y is up. Nothing
/// depends on handedness. Recognizers are plain objects fed once per frame; they hold no clock of their own.
/// </summary>
public static class Zones
{
    /// <summary>Distance from a point to the surface of a sphere; negative inside.</summary>
    public static float SphereDistance(Vector3 point, Vector3 centre, float radius) => Vector3.Distance(point, centre) - radius;

    /// <summary>True when the point is inside an axis-aligned box grown by <paramref name="padding"/> on every side.</summary>
    public static bool InBox(Vector3 point, Vector3 centre, Vector3 size, float padding = 0f)
    {
        var d = Vector3.Abs(point - centre);
        var h = size * 0.5f + new Vector3(padding);
        return d.X <= h.X && d.Y <= h.Y && d.Z <= h.Z;
    }

    /// <summary>Distance from a point to an axis-aligned box; 0 inside.</summary>
    public static float BoxDistance(Vector3 point, Vector3 centre, Vector3 size)
    {
        var d = Vector3.Abs(point - centre) - size * 0.5f;
        return Vector3.Max(d, Vector3.Zero).Length();
    }

    /// <summary>Same for a box with a rotation (the game's ray targets on angled doors).</summary>
    public static float BoxDistance(Vector3 point, Vector3 centre, Quaternion rotation, Vector3 size)
        => BoxDistance(Vector3.Transform(point - centre, Quaternion.Conjugate(rotation)), Vector3.Zero, size);

    /// <summary>Signed distance of a point in front of (+) or behind (-) a plane through <paramref name="origin"/>.</summary>
    public static float PlaneDistance(Vector3 point, Vector3 origin, Vector3 normal) => Vector3.Dot(point - origin, SafeNormalize(normal));

    internal static Vector3 SafeNormalize(Vector3 v)
    {
        float l = v.Length();
        return l > 1e-6f ? v / l : Vector3.Zero;
    }

    /// <summary>Analog value to pressed / released with hysteresis (same thresholds as the mod's VRController).</summary>
    internal static bool Hysteresis(bool wasPressed, float value, float press, float release) => value > (wasPressed ? release : press);
}

/// <summary>
/// Velocity of a tracked point from its positions, low-pass filtered. For runtimes that report no velocity and for
/// the head (which the mod does not ask the runtime about).
/// </summary>
public sealed class VelocityFilter
{
    private Vector3 _last; private bool _hasLast;

    /// <summary>Time constant of the smoothing in seconds; 0 = none.</summary>
    public float Smoothing { get; set; } = 0.04f;
    public Vector3 Velocity { get; private set; }
    public float Speed => Velocity.Length();

    public Vector3 Update(float dt, Vector3 position)
    {
        if (_hasLast && dt > 1e-5f)
        {
            var raw = (position - _last) / dt;
            float k = Smoothing <= 0f ? 1f : 1f - MathF.Exp(-dt / Smoothing);
            Velocity = Vector3.Lerp(Velocity, raw, k);
        }
        _last = position; _hasLast = true;
        return Velocity;
    }

    /// <summary>Call when tracking is lost, so the jump on return is not read as speed.</summary>
    public void Reset() { _hasLast = false; Velocity = Vector3.Zero; }
}
