using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace NIVR.DevTools;

/// <summary>
/// Text dump of everything loaded: render pipeline, cameras, canvases, volumes and the full
/// hierarchy with per-component details relevant to a VR port (what is 3D, what is a flat sprite).
/// </summary>
internal static class SceneDumper
{
    public static string DumpAll(GameObject ddolProbe)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# NIVR scene dump {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"unity={Application.unityVersion} screen={Screen.width}x{Screen.height} fullscreen={Screen.fullScreenMode} timeScale={Time.timeScale}");
        Safe(sb, "pipeline", () =>
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            sb.AppendLine($"renderPipeline={(rp == null ? "BUILT-IN" : rp.GetIl2CppType().FullName + " '" + rp.name + "'")} colorSpace={QualitySettings.activeColorSpace} quality={QualitySettings.GetQualityLevel()} aa={QualitySettings.antiAliasing} gfx={SystemInfo.graphicsDeviceType}");
            var urp = rp == null ? null : rp.TryCast<UniversalRenderPipelineAsset>();
            if (urp != null)
                sb.AppendLine($"urp: renderScale={urp.renderScale} msaa={urp.msaaSampleCount} hdr={urp.supportsHDR} depthTex={urp.supportsCameraDepthTexture} opaqueTex={urp.supportsCameraOpaqueTexture} upscaling={urp.upscalingFilter}");
        });
        sb.AppendLine($"cursor: visible={Cursor.visible} lock={Cursor.lockState}");

        sb.AppendLine();
        sb.AppendLine("## CAMERAS (all, incl. inactive)");
        Safe(sb, "cameras", () =>
        {
            foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (cam.gameObject.scene.name == null) continue; // prefab asset, not in a scene
                sb.AppendLine("- " + DescribeCamera(cam));
            }
        });

        sb.AppendLine();
        sb.AppendLine("## CANVASES (all, incl. inactive)");
        Safe(sb, "canvases", () =>
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (c.gameObject.scene.name == null) continue;
                sb.AppendLine("- " + PathOf(c.transform) + $" [scene={c.gameObject.scene.name} activeInHierarchy={c.gameObject.activeInHierarchy}] " + DescribeCanvas(c));
            }
        });

        sb.AppendLine();
        sb.AppendLine("## VOLUMES (URP post-processing)");
        Safe(sb, "volumes", () =>
        {
            foreach (var v in Resources.FindObjectsOfTypeAll<Volume>())
            {
                if (v.gameObject.scene.name == null) continue;
                sb.AppendLine("- " + PathOf(v.transform) + $" [active={v.gameObject.activeInHierarchy} enabled={v.enabled}] " + DescribeVolume(v));
            }
        });

        var seen = new HashSet<int>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            seen.Add(scene.handle);
            DumpScene(sb, scene);
        }
        if (ddolProbe != null && !seen.Contains(ddolProbe.scene.handle))
            DumpScene(sb, ddolProbe.scene);
        return sb.ToString();
    }

    private static void DumpScene(StringBuilder sb, Scene scene)
    {
        sb.AppendLine();
        sb.AppendLine($"## SCENE '{scene.name}' buildIndex={scene.buildIndex} loaded={scene.isLoaded} roots={scene.rootCount}");
        Safe(sb, "scene " + scene.name, () =>
        {
            foreach (var root in scene.GetRootGameObjects())
                DumpObject(sb, root.transform, 0);
        });
    }

    private static void DumpObject(StringBuilder sb, Transform t, int depth)
    {
        var go = t.gameObject;
        var indent = new string(' ', depth * 2);
        var rt = t.TryCast<RectTransform>();
        sb.Append(indent).Append(go.activeSelf ? "+ " : "- ").Append(go.name)
          .Append($" (layer={LayerMask.LayerToName(go.layer)}:{go.layer}");
        if (go.tag != "Untagged") sb.Append($" tag={go.tag}");
        if (rt != null)
            sb.Append($" rect={Fmt(rt.rect.size)} anchored={Fmt(rt.anchoredPosition)} scale={Fmt(t.localScale)}");
        else
            sb.Append($" pos={Fmt(t.position)} rot={Fmt(t.eulerAngles)} scale={Fmt(t.lossyScale)}");
        sb.AppendLine(")");

        Component[] comps;
        try { comps = go.GetComponents<Component>(); }
        catch (Exception e) { sb.Append(indent).AppendLine("    !! GetComponents failed: " + e.Message); comps = Array.Empty<Component>(); }
        foreach (var c in comps)
        {
            if (c == null) continue;
            string typeName;
            try { typeName = c.GetIl2CppType().FullName; } catch { typeName = "?"; }
            if (typeName == "UnityEngine.Transform" || typeName == "UnityEngine.RectTransform" || typeName == "UnityEngine.CanvasRenderer") continue;
            string detail = "";
            try { detail = Describe(c); } catch (Exception e) { detail = "!! " + e.Message; }
            sb.Append(indent).Append("    * ").Append(typeName);
            if (!string.IsNullOrEmpty(detail)) sb.Append("  ").Append(detail);
            sb.AppendLine();
        }

        for (int i = 0; i < t.childCount; i++)
            DumpObject(sb, t.GetChild(i), depth + 1);
    }

    private static string Describe(Component c)
    {
        var beh = c.TryCast<Behaviour>();
        var prefix = beh != null && !beh.enabled ? "[disabled] " : "";

        var cam = c.TryCast<Camera>();
        if (cam != null) return prefix + DescribeCamera(cam, false);
        var canvas = c.TryCast<Canvas>();
        if (canvas != null) return prefix + DescribeCanvas(canvas);
        var scaler = c.TryCast<CanvasScaler>();
        if (scaler != null) return prefix + $"mode={scaler.uiScaleMode} ref={Fmt(scaler.referenceResolution)} match={scaler.matchWidthOrHeight} screenMatch={scaler.screenMatchMode}";
        var cg = c.TryCast<CanvasGroup>();
        if (cg != null) return prefix + $"alpha={cg.alpha} interactable={cg.interactable} blocksRaycasts={cg.blocksRaycasts}";
        var img = c.TryCast<Image>();
        if (img != null) return prefix + $"sprite={DescribeSprite(img.sprite)} color={Fmt(img.color)} type={img.type} raycastTarget={img.raycastTarget} mat={MatName(img.material)}";
        var raw = c.TryCast<RawImage>();
        if (raw != null) return prefix + $"texture={DescribeTexture(raw.texture)} uvRect={raw.uvRect} color={Fmt(raw.color)} mat={MatName(raw.material)}";
        var sr = c.TryCast<SpriteRenderer>();
        if (sr != null) return (sr.enabled ? "" : "[disabled] ") + $"sprite={DescribeSprite(sr.sprite)} color={Fmt(sr.color)} flipX={sr.flipX} flipY={sr.flipY} sortLayer={sr.sortingLayerName} order={sr.sortingOrder} drawMode={sr.drawMode} mat={MatName(sr.sharedMaterial)}";
        var mr = c.TryCast<MeshRenderer>();
        if (mr != null) return (mr.enabled ? "" : "[disabled] ") + $"mats=[{MatNames(mr)}] shadows={mr.shadowCastingMode}";
        var smr = c.TryCast<SkinnedMeshRenderer>();
        if (smr != null) return (smr.enabled ? "" : "[disabled] ") + $"mesh={(smr.sharedMesh == null ? "null" : smr.sharedMesh.name + " v=" + smr.sharedMesh.vertexCount)} mats=[{MatNames(smr)}]";
        var mf = c.TryCast<MeshFilter>();
        if (mf != null) { var m = mf.sharedMesh; return m == null ? "mesh=null" : $"mesh='{m.name}' verts={m.vertexCount} bounds={Fmt(m.bounds.size)}"; }
        var vp = c.TryCast<VideoPlayer>();
        if (vp != null) return prefix + $"source={vp.source} clip={(vp.clip == null ? "null" : vp.clip.name + " " + vp.clip.width + "x" + vp.clip.height)} url='{vp.url}' renderMode={vp.renderMode} targetTexture={DescribeTexture(vp.targetTexture)} targetCamera={(vp.targetCamera == null ? "null" : vp.targetCamera.name)} playing={vp.isPlaying}";
        var light = c.TryCast<Light>();
        if (light != null) return prefix + $"type={light.type} intensity={light.intensity} range={light.range} color={Fmt(light.color)} shadows={light.shadows}";
        var vol = c.TryCast<Volume>();
        if (vol != null) return prefix + DescribeVolume(vol);
        var camData = c.TryCast<UniversalAdditionalCameraData>();
        if (camData != null) return DescribeCamData(camData);
        var gr = c.TryCast<GraphicRaycaster>();
        if (gr != null) return prefix + $"blockingObjects={gr.blockingObjects} ignoreReversed={gr.ignoreReversedGraphics}";
        var col = c.TryCast<Collider>();
        if (col != null) return (col.enabled ? "" : "[disabled] ") + $"trigger={col.isTrigger} bounds={Fmt(col.bounds.size)}";
        var anim = c.TryCast<Animator>();
        if (anim != null) return prefix + $"controller={(anim.runtimeAnimatorController == null ? "null" : anim.runtimeAnimatorController.name)}";
        var ps = c.TryCast<ParticleSystem>();
        if (ps != null) return $"playing={ps.isPlaying} count={ps.particleCount}";
        var tmp = c.TryCast<TMPro.TMP_Text>();
        if (tmp != null) { var s = tmp.text ?? ""; if (s.Length > 60) s = s.Substring(0, 60) + "..."; return prefix + $"text=\"{s.Replace("\n", "\\n")}\" font={(tmp.font == null ? "null" : tmp.font.name)} size={tmp.fontSize}"; }
        return prefix.Trim();
    }

    private static string DescribeCamera(Camera cam, bool withPath = true)
    {
        var s = withPath ? PathOf(cam.transform) + $" [scene={cam.gameObject.scene.name} active={cam.gameObject.activeInHierarchy} enabled={cam.enabled}] " : "";
        s += $"{(cam.orthographic ? "ORTHO size=" + cam.orthographicSize : "PERSP fov=" + cam.fieldOfView)} near={cam.nearClipPlane} far={cam.farClipPlane} depth={cam.depth} clear={cam.clearFlags} bg={Fmt(cam.backgroundColor)} cullingMask=0x{cam.cullingMask:X8}({MaskNames(cam.cullingMask)}) rect={cam.rect} targetTexture={DescribeTexture(cam.targetTexture)} hdr={cam.allowHDR} msaa={cam.allowMSAA} tag={cam.gameObject.tag}";
        if (withPath)
        {
            var data = cam.GetComponent<UniversalAdditionalCameraData>();
            if (data != null) s += " | URP: " + DescribeCamData(data);
            s += $" | pos={Fmt(cam.transform.position)} rot={Fmt(cam.transform.eulerAngles)}";
        }
        return s;
    }

    private static string DescribeCamData(UniversalAdditionalCameraData d)
    {
        var s = $"renderType={d.renderType} postProcessing={d.renderPostProcessing} aa={d.antialiasing} volumeMask=0x{d.volumeLayerMask.value:X} renderShadows={d.renderShadows}";
        try
        {
            if (d.renderType == CameraRenderType.Base)
            {
                var stack = d.cameraStack;
                if (stack != null && stack.Count > 0)
                {
                    var names = new List<string>();
                    for (int i = 0; i < stack.Count; i++) names.Add(stack[i] == null ? "null" : stack[i].name);
                    s += $" stack=[{string.Join(", ", names)}]";
                }
            }
        }
        catch (Exception e) { s += " stack=!" + e.Message; }
        try { s += $" renderer={d.scriptableRenderer.GetIl2CppType().Name}"; } catch { }
        return s;
    }

    private static string DescribeCanvas(Canvas c)
    {
        return $"renderMode={c.renderMode} isRoot={c.isRootCanvas} worldCamera={(c.worldCamera == null ? "null" : c.worldCamera.name)} planeDistance={c.planeDistance} sortingLayer={c.sortingLayerName} order={c.sortingOrder} overrideSorting={c.overrideSorting} scaleFactor={c.scaleFactor} pixelRect={c.pixelRect} enabled={c.enabled}";
    }

    private static string DescribeVolume(Volume v)
    {
        var s = $"global={v.isGlobal} priority={v.priority} weight={v.weight} layer={LayerMask.LayerToName(v.gameObject.layer)}";
        var profile = v.sharedProfile;
        if (profile == null) return s + " profile=null";
        var names = new List<string>();
        var comps = profile.components;
        for (int i = 0; i < comps.Count; i++)
        {
            var vc = comps[i];
            if (vc == null) continue;
            names.Add(vc.GetIl2CppType().Name + (vc.active ? "" : "(off)"));
        }
        return s + $" profile='{profile.name}' overrides=[{string.Join(", ", names)}]";
    }

    private static string DescribeSprite(Sprite s)
    {
        if (s == null) return "null";
        var r = "'" + s.name + "'";
        try
        {
            var tex = s.texture;
            r += $" tex='{(tex == null ? "null" : tex.name)}'";
            if (tex != null) r += $"({tex.width}x{tex.height})";
            r += $" rect={s.rect} pivot={Fmt(s.pivot)} ppu={s.pixelsPerUnit} packed={s.packed}";
            if (s.packed) r += $" packMode={s.packingMode} rot={s.packingRotation}";
            try { r += $" texRect={s.textureRect}"; } catch { r += " texRect=n/a(tight)"; }
        }
        catch (Exception e) { r += " !" + e.Message; }
        return r;
    }

    private static string DescribeTexture(Texture t)
    {
        if (t == null) return "null";
        return $"'{t.name}'({t.GetIl2CppType().Name} {t.width}x{t.height})";
    }

    private static string MatName(Material m)
    {
        if (m == null) return "null";
        return $"'{m.name}'/{(m.shader == null ? "null" : m.shader.name)}";
    }

    private static string MatNames(Renderer r)
    {
        var mats = r.sharedMaterials;
        var names = new List<string>();
        foreach (var m in mats) names.Add(MatName(m));
        return string.Join(", ", names);
    }

    private static string MaskNames(int mask)
    {
        if (mask == -1) return "Everything";
        var names = new List<string>();
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0)
            {
                var n = LayerMask.LayerToName(i);
                names.Add(string.IsNullOrEmpty(n) ? i.ToString() : n);
            }
        return string.Join("|", names);
    }

    internal static string PathOf(Transform t)
    {
        var path = t.name;
        var p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 64) { path = p.name + "/" + path; p = p.parent; }
        return path;
    }

    private static string Fmt(Vector2 v) => $"({v.x:0.###},{v.y:0.###})";
    private static string Fmt(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";
    private static string Fmt(Color c) => $"rgba({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})";

    private static void Safe(StringBuilder sb, string what, Action a)
    {
        try { a(); }
        catch (Exception e) { sb.AppendLine($"!! {what} failed: {e.GetType().Name}: {e.Message}"); }
    }
}
