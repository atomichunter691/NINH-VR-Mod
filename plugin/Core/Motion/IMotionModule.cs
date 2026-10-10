namespace NIVR.Core.Motion;

/// <summary>
/// A motion-control feature (reach targets, poke, close-up hands...). Modules are created once in
/// <see cref="MotionModules"/> and ticked in that fixed order from Core's Update, after tracking and the game state
/// are known and before any controller input is sent to the game, so a module can claim a button for this frame.
/// A module that throws is logged (throttled) and skipped for that frame; it never takes the frame loop down.
/// </summary>
internal interface IMotionModule
{
    /// <summary>Short name for the log and the status line.</summary>
    string Name { get; }

    /// <summary>Once per rendered frame while VR is active.</summary>
    void Tick(in MotionFrame frame);

    /// <summary>The input mode changed since the last tick (walking, gamepad view, pointer screen). Called before Tick.</summary>
    void OnModeChanged(VRInputMode from, VRInputMode to);

    /// <summary>One line for the "status" debug command.</summary>
    string Describe();
}

/// <summary>What a module needs to know about the frame. Poses and velocities are read from <see cref="VRRig"/>.</summary>
internal readonly struct MotionFrame
{
    public MotionFrame(int number, float deltaTime, VRInputMode mode, _Code.Player.EWatcherState? gameState, bool inputAllowed, InputArbiter input)
    {
        Number = number; DeltaTime = deltaTime; Mode = mode; GameState = gameState; InputAllowed = inputAllowed; Input = input;
    }

    /// <summary>Unity frame count.</summary>
    public int Number { get; }
    /// <summary>Unscaled seconds since the previous frame (the game pauses with timeScale 0; hands keep moving).</summary>
    public float DeltaTime { get; }
    public VRInputMode Mode { get; }
    /// <summary>Top of the game's watcher stack, null when the game has none (main menu).</summary>
    public _Code.Player.EWatcherState? GameState { get; }
    /// <summary>False while the headset session is unfocused or the first-run controls card is up: recognize nothing, act on nothing.</summary>
    public bool InputAllowed { get; }
    /// <summary>Who owns which button this frame.</summary>
    public InputArbiter Input { get; }
}
