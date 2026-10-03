using UnityEngine;

namespace NIVR.Core;

internal static class Haptics
{
    private static readonly float[] s_nextHover = new float[2];
    public static int Pulses { get; private set; }
    public static void Tick(VRHand hand, string reason, float amplitude = 0.25f, float seconds = 0.03f)
    {
        if (!VRRig.IsActive || VRConfig.VibrationStrength.Value <= 0f) return;
        var c = VRRig.Controller(hand);
        if (!c.IsConnected) return;
        if (reason == "hover" && Time.unscaledTime < s_nextHover[(int)hand]) return;
        if (reason == "hover") s_nextHover[(int)hand] = Time.unscaledTime + 0.05f;
        c.Haptic(amplitude, seconds);
        Pulses++;
        if (VRRig.IsSimulated || VRConfig.Verbose.Value) CorePlugin.Log.LogInfo($"haptic: {hand} {reason} amplitude={amplitude * VRConfig.VibrationStrength.Value:F2} seconds={seconds:F3}");
    }
}
