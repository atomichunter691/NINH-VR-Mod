using System;
using System.Collections.Generic;
using System.IO;
using System.Collections;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace NIVR.DevTools;

/// <summary>
/// Hotkeys + a file-based command channel so the game can be driven and inspected from scripts:
/// drop a text file named *.cmd into LogDir\cmd, one command per line (see scripts\cmd.ps1).
///   shot [name]            screenshot to LogDir\shots
///   dump [name]            scene/hierarchy dump to LogDir\dumps
///   scenes                 log loaded scenes
///   loadscene &lt;index|name&gt; SceneManager.LoadScene
///   timescale &lt;f&gt;          Time.timeScale
///   click &lt;GameObject&gt;     invoke the UI Button on the (active) GameObject with that name
///   inspect &lt;GameObject&gt;   field dump of its game components -> dumps\inspect_&lt;name&gt;.txt
///   inspecttype &lt;Type&gt;     field dump of every loaded MonoBehaviour/ScriptableObject of that type
///   openscene &lt;index&gt;      the game's own ScenesChanger.OpenScene
///   quit                   Application.Quit
/// </summary>
public class DevToolsBehaviour : MonoBehaviour
{
    public DevToolsBehaviour(IntPtr ptr) : base(ptr) { }

    private float _nextCmdPoll;
    private string _sceneSignature = "";
    private float _autoDumpAt = -1f;
    private int _shotCounter;

    private void Update()
    {
        try
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f9Key.wasPressedThisFrame) Screenshot(null);
                if (kb.f10Key.wasPressedThisFrame) Dump(null);
            }
        }
        catch (Exception) { /* input system not ready yet */ }

        TrackSceneChanges();

        if (Time.unscaledTime >= _nextCmdPoll)
        {
            _nextCmdPoll = Time.unscaledTime + 0.25f;
            PollCommands();
        }
    }

    private void TrackSceneChanges()
    {
        var sig = "";
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            sig += $"{s.buildIndex}:{s.name}:{(s.isLoaded ? 1 : 0)};";
        }
        if (sig != _sceneSignature)
        {
            _sceneSignature = sig;
            DevToolsPlugin.Logger.LogInfo($"Scenes changed -> {sig} (active: {SceneManager.GetActiveScene().name})");
            if (DevToolsPlugin.AutoDumpOnSceneLoad.Value)
                _autoDumpAt = Time.unscaledTime + DevToolsPlugin.AutoDumpDelay.Value;
        }
        if (_autoDumpAt > 0 && Time.unscaledTime >= _autoDumpAt)
        {
            _autoDumpAt = -1f;
            var name = "auto_" + SceneManager.GetActiveScene().name;
            Dump(name);
            Screenshot(name);
        }
    }

    private void PollCommands()
    {
        string[] files;
        try { files = Directory.GetFiles(DevToolsPlugin.CmdDir, "*.cmd"); }
        catch { return; }
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var f in files)
        {
            string[] lines;
            try { lines = File.ReadAllLines(f); File.Delete(f); }
            catch { continue; } // still being written; retry next poll
            foreach (var line in lines)
            {
                var l = line.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                try { Run(l); }
                catch (Exception e) { DevToolsPlugin.Logger.LogError($"cmd '{l}' failed: {e}"); }
            }
        }
    }

    private void Run(string line)
    {
        var parts = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : null;
        DevToolsPlugin.Logger.LogInfo($"cmd> {line}");
        switch (cmd)
        {
            case "shot": Screenshot(arg); break;
            case "dump": Dump(arg); break;
            case "scenes":
                DevToolsPlugin.Logger.LogInfo($"scenes: {_sceneSignature} buildCount={SceneManager.sceneCountInBuildSettings}");
                break;
            case "loadscene":
                if (int.TryParse(arg, out var idx)) SceneManager.LoadScene(idx);
                else SceneManager.LoadScene(arg);
                break;
            case "timescale": Time.timeScale = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
            case "quit": Application.Quit(); break;
            default:
                if (!GameCommands.TryRun(cmd, arg))
                    DevToolsPlugin.Logger.LogWarning($"unknown cmd '{cmd}'");
                break;
        }
    }

    private void Screenshot(string name)
    {
        name = Sanitize(name) ?? $"shot_{DateTime.Now:yyyyMMdd_HHmmss}_{_shotCounter++}";
        var path = Path.Combine(DevToolsPlugin.ShotDir, name + ".png");
        // ScreenCapture.CaptureScreenshot is unusable here (Il2CppInterop unstripping gap:
        // MissingMethodException on ReadOnlySpan.GetPinnableReference), so read the backbuffer ourselves.
        StartCoroutine(CaptureAtEndOfFrame(path).WrapToIl2Cpp());
    }

    private static IEnumerator CaptureAtEndOfFrame(string path)
    {
        yield return new WaitForEndOfFrame();
        Texture2D tex = null;
        try
        {
            tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, UnityWorkarounds.EncodeToPNG(tex));
            DevToolsPlugin.Logger.LogInfo($"screenshot -> {path} ({tex.width}x{tex.height})");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Logger.LogError($"screenshot failed - use scripts\\shot.ps1 instead. {e}");
        }
        finally
        {
            if (tex != null) UnityEngine.Object.Destroy(tex);
        }
    }

    private void Dump(string name)
    {
        name = Sanitize(name) ?? $"dump_{DateTime.Now:yyyyMMdd_HHmmss}";
        var path = Path.Combine(DevToolsPlugin.DumpDir, name + ".txt");
        try
        {
            File.WriteAllText(path, SceneDumper.DumpAll(gameObject));
            DevToolsPlugin.Logger.LogInfo($"dump -> {path}");
        }
        catch (Exception e)
        {
            DevToolsPlugin.Logger.LogError($"dump failed: {e}");
        }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new string(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
    }
}
