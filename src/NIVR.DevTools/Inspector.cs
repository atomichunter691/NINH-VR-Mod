using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using BindingFlags = Il2CppSystem.Reflection.BindingFlags;

namespace NIVR.DevTools;

/// <summary>
/// Dumps the (serialized and private) fields of game objects through IL2CPP reflection, so
/// inspector values like sprite references can be read at runtime without extracting assets.
/// </summary>
internal static class Inspector
{
    private const int MaxDepth = 4;
    private const int MaxElements = 64;

    /// <summary>All GameObjects with this exact name (incl. inactive): dump every game-script component.</summary>
    public static string InspectByName(string goName)
    {
        var sb = new StringBuilder();
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.name != goName) continue;
            sb.AppendLine($"# GameObject '{SceneDumper.PathOf(go.transform)}' scene={go.scene.name} active={go.activeInHierarchy}");
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var tn = c.GetIl2CppType().FullName;
                sb.AppendLine($"## {tn}");
                if (tn.StartsWith("UnityEngine.") && !tn.StartsWith("UnityEngine.InputSystem")) continue;
                DumpFields(sb, c, "  ", 0, new HashSet<IntPtr>());
            }
        }
        return sb.Length == 0 ? $"no GameObject named '{goName}'" : sb.ToString();
    }

    /// <summary>Every loaded MonoBehaviour / ScriptableObject whose IL2CPP type name matches (short or full name).</summary>
    public static string InspectByType(string typeName)
    {
        var sb = new StringBuilder();
        var seen = new HashSet<IntPtr>();
        void Visit(UnityEngine.Object o, string where)
        {
            if (o == null || !seen.Add(o.Pointer)) return;
            var t = o.GetIl2CppType();
            if (!IsOrDerives(t, typeName)) return;
            sb.AppendLine($"# {t.FullName} '{o.name}' {where}");
            DumpFields(sb, o, "  ", 0, new HashSet<IntPtr>());
        }
        foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            Visit(mb, $"on '{SceneDumper.PathOf(mb.transform)}' scene={mb.gameObject.scene.name}");
        foreach (var so in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            Visit(so, "(ScriptableObject)");
        return sb.Length == 0 ? $"no loaded object of type '{typeName}'" : sb.ToString();
    }

    private static bool IsOrDerives(Il2CppSystem.Type t, string name)
    {
        for (int i = 0; t != null && i < 16; t = t.BaseType, i++)
            if (t.Name == name || t.FullName == name) return true;
        return false;
    }

    private static void DumpFields(StringBuilder sb, Il2CppSystem.Object obj, string indent, int depth, HashSet<IntPtr> visiting)
    {
        if (!visiting.Add(obj.Pointer)) { sb.Append(indent).AppendLine("(cycle)"); return; }
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var t = obj.GetIl2CppType();
        for (int guard = 0; t != null && guard < 16; t = t.BaseType, guard++)
        {
            var full = t.FullName ?? "";
            if (full == "System.Object" || full.StartsWith("UnityEngine.MonoBehaviour") || full.StartsWith("UnityEngine.ScriptableObject")
                || full.StartsWith("UnityEngine.Behaviour") || full.StartsWith("UnityEngine.Component") || full.StartsWith("UnityEngine.Object"))
                break;
            Il2CppSystem.Reflection.FieldInfo[] fields;
            try { fields = t.GetFields(flags); } catch (Exception e) { sb.Append(indent).AppendLine("!! GetFields: " + e.Message); continue; }
            foreach (var f in fields)
            {
                sb.Append(indent).Append(f.FieldType.Name).Append(' ').Append(f.Name).Append(" = ");
                try { DescribeValue(sb, f.GetValue(obj), indent, depth, visiting); }
                catch (Exception e) { sb.AppendLine("!! " + e.Message); }
            }
        }
        visiting.Remove(obj.Pointer);
    }

    private static void DescribeValue(StringBuilder sb, Il2CppSystem.Object v, string indent, int depth, HashSet<IntPtr> visiting)
    {
        if (v == null) { sb.AppendLine("null"); return; }
        var t = v.GetIl2CppType();
        var full = t.FullName ?? "";

        var uo = v.TryCast<UnityEngine.Object>();
        if (uo != null)
        {
            if (uo == null) { sb.AppendLine("null(destroyed)"); return; }
            var sprite = uo.TryCast<Sprite>();
            if (sprite != null) { sb.AppendLine("Sprite " + SpriteInfo(sprite)); return; }
            var tex = uo.TryCast<Texture>();
            if (tex != null) { sb.AppendLine($"{t.Name} '{tex.name}' {tex.width}x{tex.height}"); return; }
            var comp = uo.TryCast<Component>();
            if (comp != null) { sb.AppendLine($"{t.Name} @ '{SceneDumper.PathOf(comp.transform)}'"); return; }
            var go = uo.TryCast<GameObject>();
            if (go != null) { sb.AppendLine($"GameObject @ '{SceneDumper.PathOf(go.transform)}'"); return; }
            sb.AppendLine($"{t.Name} '{uo.name}'");
            // ScriptableObject data assets: expand one level so nested sprite references are visible
            if (depth < MaxDepth && uo.TryCast<ScriptableObject>() != null && !full.StartsWith("UnityEngine.") && !full.StartsWith("TMPro."))
                DumpFields(sb, v, indent + "    ", depth + 1, visiting);
            return;
        }

        switch (full)
        {
            case "System.Single": sb.AppendLine(v.Unbox<float>().ToString("0.####")); return;
            case "System.Double": sb.AppendLine(v.Unbox<double>().ToString("0.####")); return;
            case "System.Int32": sb.AppendLine(v.Unbox<int>().ToString()); return;
            case "System.UInt32": sb.AppendLine(v.Unbox<uint>().ToString()); return;
            case "System.Int64": sb.AppendLine(v.Unbox<long>().ToString()); return;
            case "System.Int16": sb.AppendLine(v.Unbox<short>().ToString()); return;
            case "System.Byte": sb.AppendLine(v.Unbox<byte>().ToString()); return;
            case "System.Boolean": sb.AppendLine(v.Unbox<bool>().ToString()); return;
            case "UnityEngine.Vector2": { var x = v.Unbox<Vector2>(); sb.AppendLine($"({x.x:0.###},{x.y:0.###})"); return; }
            case "UnityEngine.Vector3": { var x = v.Unbox<Vector3>(); sb.AppendLine($"({x.x:0.###},{x.y:0.###},{x.z:0.###})"); return; }
            case "UnityEngine.Color": { var x = v.Unbox<Color>(); sb.AppendLine($"rgba({x.r:0.##},{x.g:0.##},{x.b:0.##},{x.a:0.##})"); return; }
        }

        if (t.IsPrimitive || t.IsEnum || full == "System.String" || full.StartsWith("UnityEngine.Vector") || full == "UnityEngine.Color"
            || full == "UnityEngine.Quaternion" || full == "UnityEngine.Rect" || full == "UnityEngine.LayerMask")
        {
            sb.AppendLine(full == "UnityEngine.LayerMask" ? "LayerMask " + v.Unbox<LayerMask>().value : v.ToString());
            return;
        }

        var arr = v.TryCast<Il2CppSystem.Array>();
        if (arr != null)
        {
            sb.AppendLine($"{t.Name} len={arr.Length}");
            if (depth >= MaxDepth) return;
            for (int i = 0; i < arr.Length && i < MaxElements; i++)
            {
                sb.Append(indent).Append("    [").Append(i).Append("] ");
                try { DescribeValue(sb, arr.GetValue(i), indent + "    ", depth + 1, visiting); }
                catch (Exception e) { sb.AppendLine("!! " + e.Message); }
            }
            return;
        }

        var list = v.TryCast<Il2CppSystem.Collections.IList>();
        if (list != null && full.StartsWith("System.Collections.Generic.List"))
        {
            int count = v.Cast<Il2CppSystem.Collections.ICollection>().Count;
            sb.AppendLine($"{t.Name} count={count}");
            if (depth >= MaxDepth) return;
            for (int i = 0; i < count && i < MaxElements; i++)
            {
                sb.Append(indent).Append("    [").Append(i).Append("] ");
                try { DescribeValue(sb, list[i], indent + "    ", depth + 1, visiting); }
                catch (Exception e) { sb.AppendLine("!! " + e.Message); }
            }
            return;
        }

        // Plain serializable game classes/structs: recurse. Everything else: type + ToString.
        bool gameType = !full.StartsWith("System.") && !full.StartsWith("UnityEngine.") && !full.StartsWith("Cysharp.")
                        && !full.StartsWith("Zenject.") && !full.StartsWith("DG.") && !full.StartsWith("TMPro.") && !full.StartsWith("Unity.");
        // Injected services/presenters drag in the whole object graph - name them, don't expand.
        if (gameType && System.Text.RegularExpressions.Regex.IsMatch(t.Name, "(Controller|Manager|Service|Presenter|Provider|Handler|Handling|Eventus|Reader|Storage|Instance)$"))
            gameType = false;
        if (gameType && depth < MaxDepth)
        {
            sb.AppendLine(t.Name);
            DumpFields(sb, v, indent + "    ", depth + 1, visiting);
            return;
        }
        string s;
        try { s = v.ToString(); } catch { s = "?"; }
        sb.AppendLine(s == full ? t.Name : $"{t.Name} {s}");
    }

    internal static string SpriteInfo(Sprite s)
    {
        var r = $"'{s.name}'";
        try
        {
            var tex = s.texture;
            r += $" tex='{(tex == null ? "null" : tex.name)}'";
            if (tex != null) r += $"({tex.width}x{tex.height})";
            r += $" rect={s.rect} pivot=({s.pivot.x:0.#},{s.pivot.y:0.#}) ppu={s.pixelsPerUnit} packed={s.packed}";
            if (s.packed) r += $" packMode={s.packingMode} rot={s.packingRotation}";
            try { r += $" texRect={s.textureRect}"; } catch { r += " texRect=n/a(tight)"; }
        }
        catch (Exception e) { r += " !" + e.Message; }
        return r;
    }
}
