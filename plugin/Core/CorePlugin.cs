using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace NIVR.Core;

[BepInPlugin(Guid, Name, Version)]
public class CorePlugin : BasePlugin
{
    public const string Guid = "nivr.core";
    public const string Name = "NIVR Core";
    public const string Version = "0.2.1";

    internal static new ManualLogSource Log;
    private static readonly Dictionary<string, DateTime> s_throttle = new();

    public override void Load()
    {
        Log = base.Log;
        VRConfig.Bind(Config);
        if (!VRConfig.Enabled.Value)
        {
            Log.LogInfo("NIVR Core is disabled in the config - the game runs flat.");
            return;
        }
        var harmony = new HarmonyLib.Harmony(Guid);
        Comfort.ApplyPatches(harmony);
        UiCapture.ApplyPatches(harmony);
        ComfortOverlay.ApplyPatches(harmony);
        VrSettings.ApplyPatches(harmony);
        AddComponent<VRCoreBehaviour>();
        Log.LogInfo($"NIVR Core {Version} loaded (backend {VRConfig.Backend.Value}, tracking {VRConfig.TrackingMode.Value}).");
    }

    /// <summary>Logs a warning at most once every 5 seconds per key (for per-frame failure paths).</summary>
    internal static void LogThrottled(string key, string message)
    {
        var now = DateTime.UtcNow;
        if (s_throttle.TryGetValue(key, out var last) && (now - last).TotalSeconds < 5) return;
        s_throttle[key] = now;
        Log.LogWarning(message);
    }
}
