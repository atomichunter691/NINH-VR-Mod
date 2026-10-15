using System.Numerics;
using Xunit;

namespace NIVR.Gestures.Tests;

public class ZonesTests
{
    [Fact]
    public void Box_contains_point_only_with_padding()
    {
        // A door leaf: 1 m wide, 2.3 m high, 6 cm thick.
        var centre = new Vector3(0f, 1f, 0f); var size = new Vector3(1.0f, 2.3f, 0.06f);
        var hand = new Vector3(0.2f, 1.2f, 0.10f);
        Assert.False(Zones.InBox(hand, centre, size));
        Assert.True(Zones.InBox(hand, centre, size, 0.12f));
        Assert.Equal(0.07f, Zones.BoxDistance(hand, centre, size), 3);
    }

    [Fact]
    public void Rotated_box_distance_matches_the_unrotated_case()
    {
        var size = new Vector3(1f, 2f, 0.1f);
        var quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, System.MathF.PI / 2f);
        // After a quarter turn about Y the thin axis points along X.
        Assert.Equal(0.25f, Zones.BoxDistance(new Vector3(0.3f, 0f, 0f), Vector3.Zero, quarterTurn, size), 3);
        Assert.Equal(0f, Zones.BoxDistance(new Vector3(0f, 0f, 0.4f), Vector3.Zero, quarterTurn, size), 3);
    }

    [Fact]
    public void Plane_distance_is_signed()
    {
        Assert.Equal(0.3f, Zones.PlaneDistance(new Vector3(0f, 5f, 0.3f), Vector3.Zero, new Vector3(0f, 0f, 2f)), 4);
        Assert.Equal(-0.3f, Zones.PlaneDistance(new Vector3(0f, 5f, -0.3f), Vector3.Zero, Vector3.UnitZ), 4);
    }

    [Fact]
    public void Velocity_filter_converges_and_resets()
    {
        var f = new VelocityFilter();
        var p = Vector3.Zero;
        for (int i = 0; i < 60; i++) { p += new Vector3(1f, 0f, 0f) * (1f / 90f); f.Update(1f / 90f, p); }
        Assert.Equal(1f, f.Speed, 2);
        f.Reset();
        f.Update(1f / 90f, new Vector3(50f, 0f, 0f)); // a tracking jump after a reset is not speed
        Assert.Equal(0f, f.Speed);
    }
}

public class PushPullTests
{
    private static readonly Vector3 Normal = Vector3.UnitZ; // surface faces +Z, the player stands at +Z

    [Fact]
    public void Push_fires_once_past_the_distance()
    {
        var r = new PushPullRecognizer();
        Assert.Equal(PushPull.None, r.Update(true, new Vector3(0f, 1f, 0.05f), Normal));
        Assert.Equal(PushPull.None, r.Update(true, new Vector3(0f, 1f, -0.02f), Normal));
        Assert.Equal(PushPull.Push, r.Update(true, new Vector3(0f, 1f, -0.06f), Normal));
        Assert.Equal(PushPull.None, r.Update(true, new Vector3(0f, 1f, -0.20f), Normal));
    }

    [Fact]
    public void Pull_fires_towards_the_player()
    {
        var r = new PushPullRecognizer { Distance = 0.08f };
        r.Update(true, new Vector3(0f, 1f, 0f), Normal);
        Assert.Equal(PushPull.None, r.Update(true, new Vector3(0f, 1f, 0.05f), Normal));
        Assert.Equal(PushPull.Pull, r.Update(true, new Vector3(0f, 1f, 0.09f), Normal));
        Assert.Equal(0.09f, r.Travel, 3);
    }

    [Fact]
    public void A_sideways_brush_does_not_fire()
    {
        var r = new PushPullRecognizer();
        r.Update(true, new Vector3(0f, 1f, 0f), Normal);
        for (float x = 0f; x < 0.6f; x += 0.05f)
            Assert.Equal(PushPull.None, r.Update(true, new Vector3(x, 1f, -0.03f), Normal));
    }

    [Fact]
    public void Leaving_the_zone_rearms()
    {
        var r = new PushPullRecognizer();
        r.Update(true, Vector3.Zero, Normal);
        Assert.Equal(PushPull.Push, r.Update(true, new Vector3(0f, 0f, -0.12f), Normal));
        Assert.Equal(PushPull.None, r.Update(false, new Vector3(0f, 0f, 0.3f), Normal));
        r.Update(true, Vector3.Zero, Normal);
        Assert.Equal(PushPull.Push, r.Update(true, new Vector3(0f, 0f, -0.12f), Normal));
    }
}

public class GrabHoldTests
{
    private const float Dt = 1f / 90f;

    [Fact]
    public void Grab_hold_release_sequence()
    {
        var r = new GrabHoldRecognizer { HoldSeconds = 0.2f };
        Assert.Equal(GrabEvent.None, r.Update(Dt, true, 0f));
        Assert.Equal(GrabEvent.Grabbed, r.Update(Dt, true, 0.9f));
        int completed = 0;
        for (int i = 0; i < 40; i++) if (r.Update(Dt, true, 0.9f) == GrabEvent.HoldCompleted) completed++;
        Assert.Equal(1, completed);
        Assert.True(r.IsGrabbing);
        Assert.Equal(GrabEvent.Released, r.Update(Dt, true, 0.1f));
        Assert.False(r.IsGrabbing);
    }

    [Fact]
    public void A_fist_carried_into_the_zone_does_not_grab()
    {
        var r = new GrabHoldRecognizer();
        r.Update(Dt, false, 0.9f);
        for (int i = 0; i < 20; i++) Assert.Equal(GrabEvent.None, r.Update(Dt, true, 0.9f));
        r.Update(Dt, true, 0.1f); // open inside, then close: now it is a grab
        Assert.Equal(GrabEvent.Grabbed, r.Update(Dt, true, 0.9f));
    }

    [Fact]
    public void The_grab_survives_leaving_the_zone_and_grip_noise()
    {
        var r = new GrabHoldRecognizer();
        r.Update(Dt, true, 0f);
        r.Update(Dt, true, 0.8f);
        Assert.Equal(GrabEvent.None, r.Update(Dt, false, 0.5f)); // between the thresholds: still held
        Assert.True(r.IsGrabbing);
        Assert.Equal(GrabEvent.Released, r.Update(Dt, false, 0.3f));
    }

    [Fact]
    public void Closing_outside_the_zone_is_ignored()
    {
        var r = new GrabHoldRecognizer();
        r.Update(Dt, false, 0f);
        Assert.Equal(GrabEvent.None, r.Update(Dt, false, 0.9f));
        Assert.False(r.IsGrabbing);
    }
}

public class StrokeTests
{
    private const float Dt = 1f / 90f;

    [Fact]
    public void Sliding_back_and_forth_along_the_surface_fires()
    {
        var r = new StrokeRecognizer { Length = 0.25f };
        int fired = 0;
        // 0.4 m/s along X, direction reversed every 0.25 s, for one second: 0.4 m of sliding.
        for (int i = 0; i < 90; i++)
        {
            float sign = (i / 22) % 2 == 0 ? 1f : -1f;
            if (r.Update(Dt, true, new Vector3(0.4f * sign, 0f, 0f), Vector3.UnitY)) fired++;
        }
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Movement_into_the_surface_does_not_count()
    {
        var r = new StrokeRecognizer();
        for (int i = 0; i < 180; i++) Assert.False(r.Update(Dt, true, new Vector3(0f, -0.4f, 0f), Vector3.UnitY));
        Assert.Equal(0f, r.Progress);
    }

    [Fact]
    public void A_fast_hand_resets_the_stroke()
    {
        var r = new StrokeRecognizer();
        for (int i = 0; i < 30; i++) r.Update(Dt, true, new Vector3(0.4f, 0f, 0f), Vector3.UnitY);
        Assert.True(r.Progress > 0.1f);
        Assert.False(r.Update(Dt, true, new Vector3(3f, 0f, 0f), Vector3.UnitY));
        Assert.Equal(0f, r.Progress);
    }

    [Fact]
    public void A_short_gap_outside_the_zone_keeps_the_progress_a_long_one_does_not()
    {
        var r = new StrokeRecognizer { Grace = 0.3f };
        for (int i = 0; i < 30; i++) r.Update(Dt, true, new Vector3(0.4f, 0f, 0f), Vector3.UnitY);
        float before = r.Progress;
        for (int i = 0; i < 9; i++) r.Update(Dt, false, Vector3.Zero, Vector3.UnitY);   // 0.1 s
        Assert.Equal(before, r.Progress);
        for (int i = 0; i < 45; i++) r.Update(Dt, false, Vector3.Zero, Vector3.UnitY);  // 0.5 s more
        Assert.Equal(0f, r.Progress);
    }
}

public class ThrowTests
{
    private const float Dt = 1f / 90f;

    [Fact]
    public void Release_at_speed_is_a_throw_with_the_recent_velocity()
    {
        var r = new ThrowRecognizer();
        var v = new Vector3(0f, 1f, 3f);
        for (int i = 0; i < 20; i++) Assert.False(r.Update(Dt, 1f, v, out _));
        Assert.True(r.Update(Dt, 0f, v * 0.2f, out var release)); // the release frame itself is already slowing
        Assert.True(release.Length() > 2.5f);
        Assert.True(release.Z > 0f);
    }

    [Fact]
    public void Opening_a_resting_hand_is_not_a_throw()
    {
        var r = new ThrowRecognizer();
        for (int i = 0; i < 20; i++) r.Update(Dt, 1f, new Vector3(0.1f, 0f, 0f), out _);
        Assert.False(r.Update(Dt, 0f, new Vector3(0.1f, 0f, 0f), out var release));
        Assert.Equal(Vector3.Zero, release);
    }

    [Fact]
    public void Speed_long_before_the_release_does_not_count()
    {
        var r = new ThrowRecognizer { Window = 0.1f };
        for (int i = 0; i < 20; i++) r.Update(Dt, 1f, new Vector3(0f, 0f, 4f), out _);
        for (int i = 0; i < 30; i++) r.Update(Dt, 1f, Vector3.Zero, out _); // a third of a second at rest
        Assert.False(r.Update(Dt, 0f, Vector3.Zero, out _));
    }

    [Fact]
    public void A_fast_open_hand_is_not_a_throw()
    {
        var r = new ThrowRecognizer();
        for (int i = 0; i < 20; i++) Assert.False(r.Update(Dt, 0f, new Vector3(0f, 0f, 4f), out _));
    }
}

public class RaiseHandsTests
{
    private const float Dt = 1f / 90f;
    private static readonly Vector3 Head = new(0f, 1.65f, 0f);
    private static readonly Vector3 Forward = Vector3.UnitZ;
    private static readonly Vector3 Left = new(-0.2f, 1.45f, 0.35f), Right = new(0.2f, 1.45f, 0.35f);

    private static RaiseHandsEvent Step(RaiseHandsRecognizer r, Vector3 left, Vector3 right, bool tracked = true, Vector3 velocity = default)
        => r.Update(Dt, Head, Forward, tracked, left, velocity, tracked, right, velocity);

    [Fact]
    public void Held_up_in_front_fires_once_then_lowers()
    {
        var r = new RaiseHandsRecognizer { HoldSeconds = 0.3f };
        int raised = 0;
        for (int i = 0; i < 90; i++) if (Step(r, Left, Right) == RaiseHandsEvent.Raised) raised++;
        Assert.Equal(1, raised);
        Assert.True(r.IsRaised);
        Assert.Equal(RaiseHandsEvent.Lowered, Step(r, Left - new Vector3(0f, 0.6f, 0f), Right - new Vector3(0f, 0.6f, 0f)));
        Assert.False(r.IsRaised);
    }

    [Fact]
    public void Hands_at_the_hips_do_not_fire()
    {
        var r = new RaiseHandsRecognizer();
        for (int i = 0; i < 180; i++)
            Assert.Equal(RaiseHandsEvent.None, Step(r, new Vector3(-0.2f, 0.9f, 0.2f), new Vector3(0.2f, 0.9f, 0.2f)));
    }

    [Fact]
    public void One_hand_or_hands_behind_the_head_do_not_fire()
    {
        var r = new RaiseHandsRecognizer();
        for (int i = 0; i < 90; i++) Assert.Equal(RaiseHandsEvent.None, Step(r, Left, new Vector3(0.2f, 0.9f, 0.2f)));
        for (int i = 0; i < 90; i++)
            Assert.Equal(RaiseHandsEvent.None, Step(r, new Vector3(-0.2f, 1.8f, -0.3f), new Vector3(0.2f, 1.8f, -0.3f)));
        for (int i = 0; i < 90; i++) Assert.Equal(RaiseHandsEvent.None, Step(r, Left, Right, tracked: false));
    }

    [Fact]
    public void Waving_or_clapped_hands_do_not_fire()
    {
        var r = new RaiseHandsRecognizer();
        for (int i = 0; i < 90; i++) Assert.Equal(RaiseHandsEvent.None, Step(r, Left, Right, velocity: new Vector3(1.5f, 0f, 0f)));
        var together = new Vector3(0f, 1.45f, 0.35f);
        for (int i = 0; i < 90; i++) Assert.Equal(RaiseHandsEvent.None, Step(r, together, together + new Vector3(0.05f, 0f, 0f)));
    }

    [Fact]
    public void Looking_down_does_not_break_the_forward_test()
    {
        var r = new RaiseHandsRecognizer { HoldSeconds = 0.1f };
        var down = Vector3.Normalize(new Vector3(0f, -0.9f, 0.4f));
        RaiseHandsEvent last = RaiseHandsEvent.None; bool raised = false;
        for (int i = 0; i < 30; i++) { last = r.Update(Dt, Head, down, true, Left, Vector3.Zero, true, Right, Vector3.Zero); raised |= last == RaiseHandsEvent.Raised; }
        Assert.True(raised);
    }
}
