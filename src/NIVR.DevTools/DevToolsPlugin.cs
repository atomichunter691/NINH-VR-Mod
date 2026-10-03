using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace NIVR.DevTools;

[BepInPlugin(Guid, Name, Version)]
public class DevToolsPlugin : BasePlugin
{
    public const string Guid = "nivr.devtools";
    public const string Name = "NIVR DevTools";
    public const string Version = "0.1.0";

    internal static ManualLogSource Logger;
    internal static string LogDir;
    internal static string ShotDir;
    internal static string DumpDir;
    internal static string CmdDir;
    internal static ConfigEntry<bool> AutoDumpOnSceneLoad;
    internal static ConfigEntry<float> AutoDumpDelay;

    public override void Load()
    {
        Logger = Log;

        var logDirCfg = Config.Bind("General", "LogDir", @"C:\VRMod\logs",
            "Where the log mirror, screenshots, dumps and the command inbox live.");
        AutoDumpOnSceneLoad = Config.Bind("General", "AutoDumpOnSceneLoad", true,
            "Write a scene dump (and screenshot) a few seconds after every scene change.");
        AutoDumpDelay = Config.Bind("General", "AutoDumpDelay", 4f,
            "Seconds to wait after a scene change before the automatic dump.");

        LogDir = logDirCfg.Value;
        ShotDir = Path.Combine(LogDir, "shots");
        DumpDir = Path.Combine(LogDir, "dumps");
        CmdDir = Path.Combine(LogDir, "cmd");
        foreach (var d in new[] { LogDir, ShotDir, DumpDir, CmdDir })
            Directory.CreateDirectory(d);

        BepInEx.Logging.Logger.Listeners.Add(new FileMirrorLogListener(Path.Combine(LogDir, "nivr.log")));

        var sandbox = Config.Bind("General", "SaveSandbox", true,
            "Swallow all save writes (PlayerPrefs + Steam Cloud) so dev sessions never touch the real save. Loading still works.");
        if (sandbox.Value)
            SaveSandbox.Apply(new HarmonyLib.Harmony(Guid + ".savesandbox"));
        else
            Log.LogWarning("SaveSandbox is OFF - this session can overwrite the real (Steam Cloud) save!");

        Log.LogInfo($"DevTools loaded. LogDir={LogDir}  (F9 = screenshot, F10 = scene dump, or drop a *.cmd file in {CmdDir})");
        AddComponent<DevToolsBehaviour>();
    }
}

/// <summary>Mirrors every BepInEx log line (ours, other plugins', Unity's) into LogDir\nivr.log.</summary>
internal sealed class FileMirrorLogListener : ILogListener
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public FileMirrorLogListener(string path)
    {
        // Keep one previous run around for comparison.
        try { if (File.Exists(path)) File.Copy(path, path + ".prev", true); } catch { }
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        _writer.WriteLine($"==== NIVR log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====");
    }

    public LogLevel LogLevelFilter => LogLevel.All;

    public void LogEvent(object sender, LogEventArgs e)
    {
        lock (_lock)
            _writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{e.Level,-7}:{e.Source.SourceName,10}] {e.Data}");
    }

    public void Dispose() => _writer.Dispose();
}
