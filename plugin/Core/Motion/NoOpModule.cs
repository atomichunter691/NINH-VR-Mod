namespace NIVR.Core.Motion;

/// <summary>
/// Does nothing to the game. It exists to prove the module seam: it counts its ticks and mode changes, which the
/// "status" debug command prints, and it is the template for a real module.
/// </summary>
internal sealed class NoOpModule : IMotionModule
{
    private long _ticks, _allowedTicks;
    private int _modeChanges;
    private VRInputMode _mode;

    public string Name => "noop";

    public void Tick(in MotionFrame frame)
    {
        if (_ticks == 0) CorePlugin.Log.LogInfo($"Motion module '{Name}' is ticking (mode {frame.Mode}).");
        _ticks++;
        if (frame.InputAllowed) _allowedTicks++;
        _mode = frame.Mode;
    }

    public void OnModeChanged(VRInputMode from, VRInputMode to) => _modeChanges++;

    public string Describe() => $"ticks={_ticks} inputAllowed={_allowedTicks} modeChanges={_modeChanges} mode={_mode}";
}
