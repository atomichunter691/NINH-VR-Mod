using System;
using System.Text;
using UnityEngine;

namespace NIVR.Core.Motion;

/// <summary>Owns the motion modules and ticks them in a fixed order (the order of the array below).</summary>
internal sealed class MotionModules
{
    // Order matters: earlier modules get first claim on buttons. Add new modules here, nowhere else.
    private readonly IMotionModule[] _modules =
    {
        new NoOpModule(),
    };

    private readonly InputArbiter _input = new();
    private VRInputMode? _lastMode;

    /// <summary>This frame's button claims; valid after <see cref="Tick"/> until the next one.</summary>
    public InputArbiter Input => _input;

    public void Tick(VRInputMode mode, _Code.Player.EWatcherState? gameState, bool inputAllowed)
    {
        _input.BeginFrame();
        var frame = new MotionFrame(Time.frameCount, Time.unscaledDeltaTime, mode, gameState, inputAllowed, _input);
        bool modeChanged = _lastMode.HasValue && _lastMode.Value != mode;
        foreach (var m in _modules)
        {
            try
            {
                if (modeChanged) m.OnModeChanged(_lastMode.Value, mode);
                m.Tick(frame);
            }
            catch (Exception e)
            {
                CorePlugin.LogThrottled("module." + m.Name, $"Motion module '{m.Name}' failed: {e}");
            }
        }
        _lastMode = mode;
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        foreach (var m in _modules)
        {
            if (sb.Length > 0) sb.Append("; ");
            string d;
            try { d = m.Describe(); } catch (Exception e) { d = "describe failed: " + e.GetType().Name; }
            sb.Append(m.Name).Append(": ").Append(d);
        }
        return sb.Append($" (claims this frame: {_input.ClaimCount})").ToString();
    }
}
