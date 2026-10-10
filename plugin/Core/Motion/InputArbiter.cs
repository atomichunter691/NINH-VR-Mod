using System;

namespace NIVR.Core.Motion;

/// <summary>
/// Per-frame ownership of controller buttons. A module that turns a button into something else (grip = grab) claims
/// it; whoever would otherwise send that button to the game asks <see cref="IsClaimed"/> first. Claims last one frame
/// and are first come, first served in module order, so a held button has to be claimed again every frame.
/// This is for new code. The older per-feature flags (UiPointer.SwallowTrigger, PauseGuard.SwallowEast, ButtonGate)
/// still work as before and nothing existing reads the arbiter yet.
/// </summary>
internal sealed class InputArbiter
{
    private static readonly int Buttons = Enum.GetValues<VRButton>().Length;
    private readonly string[] _owner = new string[2 * Buttons];

    /// <summary>Claims made so far this frame.</summary>
    public int ClaimCount { get; private set; }

    internal void BeginFrame()
    {
        if (ClaimCount == 0) return;
        Array.Clear(_owner, 0, _owner.Length);
        ClaimCount = 0;
    }

    /// <summary>True when the button is now owned by <paramref name="owner"/> (also when it already was).</summary>
    public bool Claim(VRHand hand, VRButton button, string owner)
    {
        int i = (int)hand * Buttons + (int)button;
        if (_owner[i] != null) return _owner[i] == owner;
        _owner[i] = owner;
        ClaimCount++;
        return true;
    }

    public bool IsClaimed(VRHand hand, VRButton button) => _owner[(int)hand * Buttons + (int)button] != null;

    /// <summary>Name of the module that owns the button this frame, or null.</summary>
    public string Owner(VRHand hand, VRButton button) => _owner[(int)hand * Buttons + (int)button];
}
