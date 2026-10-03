using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NIVR.DevTools;

/// <summary>
/// The working copy shares the real game's save: PlayerPrefs (HKCU\Software\Trioskaz\NoImNotAHuman,
/// keys GameSaveData / MetaPrefsData) plus Steam Cloud (same app id). While the sandbox is on,
/// nothing the dev session does is persisted: PlayerPrefs string writes and Steam Cloud file
/// writes are swallowed. Loading still works, so "Continue" loads the real save read-only.
/// </summary>
internal static class SaveSandbox
{
    public static int BlockedPrefs, BlockedCloud;

    public static void Apply(Harmony harmony)
    {
        TryPatch(harmony, typeof(PlayerPrefs).GetMethod("SetString", new[] { typeof(string), typeof(string) }), nameof(BlockPrefs));
        var srs = typeof(Steamworks.SteamRemoteStorage);
        foreach (var m in srs.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name == "FileWrite") TryPatch(harmony, m, nameof(BlockCloudWrite));
            else if (m.Name == "FileWriteAsync") TryPatch(harmony, m, nameof(BlockCloudWriteAsync));
            else if (m.Name == "FileDelete") TryPatch(harmony, m, nameof(BlockCloudWrite));
        }
    }

    private static void TryPatch(Harmony harmony, MethodBase target, string prefix)
    {
        if (target == null) { DevToolsPlugin.Logger.LogWarning($"SaveSandbox: target for {prefix} not found"); return; }
        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SaveSandbox).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)));
            DevToolsPlugin.Logger.LogInfo($"SaveSandbox: guarding {target.DeclaringType.Name}.{target.Name}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Logger.LogError($"SaveSandbox: FAILED to guard {target.DeclaringType.Name}.{target.Name}: {e.Message}");
        }
    }

    private static bool BlockPrefs(string __0)
    {
        BlockedPrefs++;
        DevToolsPlugin.Logger.LogInfo($"SaveSandbox: swallowed PlayerPrefs.SetString('{__0}')");
        return false;
    }

    private static bool BlockCloudWrite(string __0, ref bool __result)
    {
        BlockedCloud++;
        DevToolsPlugin.Logger.LogInfo($"SaveSandbox: swallowed Steam Cloud write/delete '{__0}'");
        __result = true;
        return false;
    }

    private static bool BlockCloudWriteAsync(string __0)
    {
        BlockedCloud++;
        DevToolsPlugin.Logger.LogWarning($"SaveSandbox: swallowed Steam Cloud FileWriteAsync '{__0}' (caller may wait forever)");
        return false;
    }
}
