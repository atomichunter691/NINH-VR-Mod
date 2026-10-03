using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace NIVR.DevTools;

/// <summary>Game-specific dev commands (extend as needed). Return false for unknown commands.</summary>
internal static class GameCommands
{
    public static bool TryRun(string cmd, string arg)
    {
        switch (cmd)
        {
            case "click": Click(arg); return true;
            case "inspect": Write("inspect_" + arg, Inspector.InspectByName(arg)); return true;
            case "inspecttype": Write("type_" + arg, Inspector.InspectByType(arg)); return true;
            case "openscene": _Code.Infrastructure.Scenes.ScenesChanger.OpenScene(int.Parse(arg)); return true;
            case "savesprite": SaveSprite(arg); return true;
            case "inputjson":
                foreach (var a in Resources.FindObjectsOfTypeAll<UnityEngine.InputSystem.InputActionAsset>())
                    Write("input_" + a.name, a.ToJson());
                return true;
            case "buildscenes":
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings; i++)
                    DevToolsPlugin.Logger.LogInfo($"build scene {i}: {UnityEngine.SceneManagement.SceneUtility.GetScenePathByBuildIndex(i)}");
                return true;
            default: return false;
        }
    }

    /// <summary>
    /// Dev-only preview of a loaded sprite (to check orientation etc.) -> LogDir\shots\sprites\.
    /// These previews are game art: never copy them into the mod or a release package.
    /// </summary>
    private static void SaveSprite(string spriteName)
    {
        foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (s.name != spriteName) continue;
            var tex = s.texture;
            var r = s.rect;
            int w = Mathf.Max(1, (int)r.width), h = Mathf.Max(1, (int)r.height);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt, new Vector2(r.width / tex.width, r.height / tex.height), new Vector2(r.x / tex.width, r.y / tex.height));
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var dir = Path.Combine(DevToolsPlugin.ShotDir, "sprites");
            Directory.CreateDirectory(dir);
            var name = spriteName;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var path = Path.Combine(dir, name + ".png");
            File.WriteAllBytes(path, UnityWorkarounds.EncodeToPNG(copy));
            Object.Destroy(copy);
            DevToolsPlugin.Logger.LogInfo($"savesprite -> {path} ({Inspector.SpriteInfo(s)})");
            return;
        }
        DevToolsPlugin.Logger.LogWarning($"savesprite: no loaded Sprite named '{spriteName}'");
    }

    private static void Write(string name, string text)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        var path = Path.Combine(DevToolsPlugin.DumpDir, name + ".txt");
        File.WriteAllText(path, text);
        DevToolsPlugin.Logger.LogInfo($"{name} -> {path} ({text.Length} chars)");
    }

    private static void Click(string goName)
    {
        foreach (var b in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (b.gameObject.name != goName || !b.gameObject.activeInHierarchy) continue;
            DevToolsPlugin.Logger.LogInfo($"click '{SceneDumper.PathOf(b.transform)}' interactable={b.interactable}");
            b.onClick.Invoke();
            return;
        }
        DevToolsPlugin.Logger.LogWarning($"click: no active Button on a GameObject named '{goName}'");
    }
}
