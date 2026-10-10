using System;
using System.Numerics;
using Xunit;

namespace NIVR.Gestures.Tests;

public class LeanInTests
{
    private const float Dt = 1f / 90f;
    // The peephole of the game's front door: the hole, and "into" = through the door.
    private static readonly Vector3 Hole = new(-4.12f, 1.47f, -4.23f);
    private static readonly Vector3 Into = Vector3.UnitZ;

    [Fact]
    public void Dwelling_at_the_hole_enters_and_backing_off_exits()
    {
        var r = new LeanInRecognizer { DwellSeconds = 0.2f };
        var near = Hole - Into * 0.10f;
        int entered = 0;
        for (int i = 0; i < 45; i++) if (r.Update(Dt, near, Into, Hole, Into) == LeanEvent.Entered) entered++;
        Assert.Equal(1, entered);
        Assert.True(r.IsIn);
        // Inside the exit radius nothing changes; beyond it the lean ends.
        Assert.Equal(LeanEvent.None, r.Update(Dt, Hole - Into * 0.25f, Into, Hole, Into));
        Assert.Equal(LeanEvent.Exited, r.Update(Dt, Hole - Into * 0.40f, Into, Hole, Into));
        Assert.False(r.IsIn);
    }

    [Fact]
    public void Passing_by_quickly_does_not_enter()
    {
        var r = new LeanInRecognizer { DwellSeconds = 0.25f };
        for (int i = 0; i < 10; i++) Assert.Equal(LeanEvent.None, r.Update(Dt, Hole - Into * 0.10f, Into, Hole, Into));
        for (int i = 0; i < 90; i++) Assert.Equal(LeanEvent.None, r.Update(Dt, Hole - Into * 0.60f, Into, Hole, Into));
    }

    [Fact]
    public void Standing_close_but_looking_away_does_not_enter()
    {
        var r = new LeanInRecognizer();
        for (int i = 0; i < 90; i++) Assert.Equal(LeanEvent.None, r.Update(Dt, Hole - Into * 0.10f, Vector3.UnitX, Hole, Into));
    }

    [Fact]
    public void Standing_at_the_usual_distance_does_not_enter()
    {
        var r = new LeanInRecognizer();
        // The game's standing point is 0.39 m in front of the hole.
        for (int i = 0; i < 180; i++) Assert.Equal(LeanEvent.None, r.Update(Dt, Hole - Into * 0.39f, Into, Hole, Into));
    }
}

public class HeadGestureTests
{
    private const float Dt = 1f / 90f;

    private static Vector3 Forward(float yawDegrees, float pitchDegrees)
    {
        float yaw = yawDegrees * MathF.PI / 180f, pitch = pitchDegrees * MathF.PI / 180f;
        return new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));
    }

    /// <summary>Feeds a sine on one axis; returns what was recognized (at most one gesture expected).</summary>
    private static HeadGesture Run(HeadGestureRecognizer r, float seconds, Func<float, (float yaw, float pitch)> motion)
    {
        var result = HeadGesture.None;
        for (float t = 0f; t < seconds; t += Dt)
        {
            var (yaw, pitch) = motion(t);
            var g = r.Update(Dt, Forward(yaw, pitch));
            if (g != HeadGesture.None) { Assert.Equal(HeadGesture.None, result); result = g; }
        }
        return result;
    }

    [Fact]
    public void Nodding_is_a_nod()
    {
        var r = new HeadGestureRecognizer();
        Assert.Equal(HeadGesture.Nod, Run(r, 1.2f, t => (0f, -12f * MathF.Sin(2f * MathF.PI * 2.5f * t))));
    }

    [Fact]
    public void Shaking_is_a_shake_also_across_the_yaw_wrap()
    {
        var r = new HeadGestureRecognizer();
        Assert.Equal(HeadGesture.Shake, Run(r, 1.2f, t => (15f * MathF.Sin(2f * MathF.PI * 2.5f * t), 0f)));
        var back = new HeadGestureRecognizer();
        Assert.Equal(HeadGesture.Shake, Run(back, 1.2f, t => (180f + 15f * MathF.Sin(2f * MathF.PI * 2.5f * t), 0f)));
    }

    [Fact]
    public void A_single_look_down_and_up_is_not_a_nod()
    {
        var r = new HeadGestureRecognizer();
        Assert.Equal(HeadGesture.None, Run(r, 2f, t => (0f, t < 0.5f ? -30f * t / 0.5f : t < 1f ? -30f + 30f * (t - 0.5f) / 0.5f : 0f)));
    }

    [Fact]
    public void Small_tracker_jitter_is_nothing()
    {
        var r = new HeadGestureRecognizer();
        Assert.Equal(HeadGesture.None, Run(r, 3f, t => (1.5f * MathF.Sin(40f * t), 1.5f * MathF.Cos(33f * t))));
    }

    [Fact]
    public void Jitter_on_top_of_a_nod_still_gives_one_nod()
    {
        var r = new HeadGestureRecognizer();
        Assert.Equal(HeadGesture.Nod, Run(r, 1.2f, t => (0.8f * MathF.Sin(70f * t), -12f * MathF.Sin(2f * MathF.PI * 2.5f * t) + 0.8f * MathF.Sin(90f * t))));
    }

    [Fact]
    public void Slow_swings_spread_over_a_long_time_are_not_a_gesture()
    {
        var r = new HeadGestureRecognizer { Window = 1.5f };
        // Looking left and right at things: one swing every 1.5 seconds.
        Assert.Equal(HeadGesture.None, Run(r, 8f, t => (40f * MathF.Sin(2f * MathF.PI * t / 3f), 0f)));
    }

    [Fact]
    public void Nothing_fires_during_the_cooldown()
    {
        var r = new HeadGestureRecognizer { Cooldown = 5f };
        int nods = 0;
        for (float t = 0f; t < 4f; t += Dt)
            if (r.Update(Dt, Forward(0f, -12f * MathF.Sin(2f * MathF.PI * 2.5f * t))) == HeadGesture.Nod) nods++;
        Assert.Equal(1, nods);
    }
}
