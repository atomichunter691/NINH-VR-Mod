using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace NIVR.Core;

/// <summary>
/// Gets the game's screen UI into textures the VR side can place in the world, without changing how the game lays it
/// out or clicks it: every screen canvas stays a screen canvas of the game camera, and the game camera is rendered one
/// extra time per frame with only the UI layers, transparent background, into a texture of exactly the window size
/// (same size = no canvas re-layout). Overlay canvases cannot be rendered by a camera, so they are switched to
/// ScreenSpaceCamera (same layout, same mouse behaviour, sort order kept above the camera canvases).
/// The room illustrations (RoomDisplayer) go to a separate texture so they can be shown behind the door.
/// </summary>
internal sealed class UiCapture
{
    public const int UiLayer = 5, RoomLayer = 31, WorldUiLayer = 30;
    /// <summary>Drawn by the eye camera only (laser, peephole picture); the game cameras never render it.</summary>
    public const int VrOnlyLayer = 29;
    private const int OverlayOrderBoost = 10000;
    private const int ExtraLayers = (1 << RoomLayer) | (1 << WorldUiLayer);

    private readonly Dictionary<int, (Canvas canvas, int layer)> _worldCanvases = new();

    private readonly Dictionary<int, (Canvas canvas, int order, int layer)> _converted = new();
    private RenderTexture _ui, _room;
    private Canvas _roomCanvas; private int _roomCanvasOriginalLayer;
    private int _uiMask;
    private float _nextScan;
    private int _camId;
    private readonly List<(Transform text, Vector3 pos)> _subtitles = new();
    private readonly List<int> _dead = new();
    private readonly Dictionary<int, (Camera cam, int mask)> _cameraMasks = new();
    public int RoomRenders { get; private set; }
    private int _warnW, _warnH;
    public bool PictureActive
    {
        get
        {
            if (_roomCanvas == null) return false;
            var rd = _roomCanvas.GetComponent<_Code.Infrastructure.Rooms.RoomDisplayer>();
            if (rd != null && rd._isOpened) return true;
            var t = _roomCanvas.transform;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeInHierarchy && t.GetChild(i).name.EndsWith("WindowView")) return true;
            return false;
        }
    }

    // Scripts written for Overlay canvases place elements by assigning screen pixels to Transform.position (on an
    // Overlay canvas world space is pixel space). On the converted (camera) canvas that throws them far off screen and
    // turns them edge-on: the door marker (OpenRoomView: "X" crosshair + room name box) was invisible / a sliver.
    // While such a script updates, its canvas root is put back on the overlay pixel frame, then restored; the local
    // positions and rotations it wrote are then right in either frame.
    /// <summary>True once Input.mousePosition reports the laser position (UiPointer.LegacyMouse) instead of the OS cursor.</summary>
    public static bool LegacyMousePatched { get; private set; }

    private static void MousePositionPostfix(ref Vector3 __result)
    {
        var p = UiPointer.LegacyMouse;
        if (p.HasValue) __result = new Vector3(p.Value.x, p.Value.y, 0f);
    }

    public static void ApplyPatches(HarmonyLib.Harmony harmony)
    {
        try
        {
            // Screens that read the legacy mouse (fridge hover) follow the laser without moving the real cursor
            // (which the game keeps locked in gamepad mode: moving it made the cursor flip every frame).
            var getter = HarmonyLib.AccessTools.PropertyGetter(typeof(UnityEngine.Input), nameof(UnityEngine.Input.mousePosition));
            harmony.Patch(getter, postfix: new HarmonyLib.HarmonyMethod(typeof(UiCapture), nameof(MousePositionPostfix)));
            LegacyMousePatched = true;
        }
        catch (Exception e)
        {
            CorePlugin.Log.LogWarning($"Could not patch Input.mousePosition (falls back to moving the OS cursor): {e.Message}");
        }
        try
        {
            var target = HarmonyLib.AccessTools.Method(typeof(_Code.Menues.OpenRoomView), nameof(_Code.Menues.OpenRoomView.Update));
            harmony.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(UiCapture), nameof(PixelFramePrefix)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(UiCapture), nameof(PixelFramePostfix)));
        }
        catch (Exception e)
        {
            CorePlugin.Log.LogError($"Could not patch OpenRoomView.Update (door marker will be misplaced in VR): {e}");
        }
    }

    private static Transform s_pixelRoot;
    private static bool s_movedCrosshair;
    private static Vector3 s_savedPos, s_savedScale;
    private static Quaternion s_savedRot;

    private static void PixelFramePrefix(_Code.Menues.OpenRoomView __instance)
    {
        s_pixelRoot = null;
        if (!VRRig.IsActive) return;
        var canvas = __instance.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceCamera) return;
        var t = canvas.transform;
        s_pixelRoot = t;
        s_savedPos = t.position; s_savedRot = t.rotation; s_savedScale = t.localScale;
        float f = canvas.scaleFactor;
        t.SetPositionAndRotation(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f), Quaternion.identity);
        t.localScale = new Vector3(f, f, f);
    }

    private static void PixelFramePostfix(_Code.Menues.OpenRoomView __instance)
    {
        var t = s_pixelRoot;
        s_pixelRoot = null;
        if (t == null) return;
        // The crosshair marks the spot the controller laser hits instead of the screen centre.
        try
        {
            var target = UiPointer.CrosshairTarget;
            var cam = __instance._camera;
            if (target.HasValue && cam != null && __instance._crosshair != null)
            {
                var sp = cam.WorldToScreenPoint(target.Value);
                if (sp.z > 0f) { __instance._crosshair.position = new Vector3(sp.x, sp.y, 0f); s_movedCrosshair = true; }
            }
            else if (s_movedCrosshair && __instance._crosshair != null)
            {
                __instance._crosshair.localPosition = Vector3.zero; // back to the game's place (screen centre)
                s_movedCrosshair = false;
            }
        }
        catch (Exception) { }
        if (__instance._crosshair != null) __instance._crosshair.position = FlatScreen.ReprojectHudPixel(__instance._crosshair.position, __instance._camera);
        if (__instance._box != null) __instance._box.position = FlatScreen.ReprojectHudPixel(__instance._box.position, __instance._camera);
        t.SetPositionAndRotation(s_savedPos, s_savedRot);
        t.localScale = s_savedScale;
    }

    public RenderTexture UiTexture => _ui;
    public RenderTexture RoomTexture => _room;
    public Canvas RoomCanvas => _roomCanvas != null ? _roomCanvas : null;
    public int ConvertedCount => _converted.Count;

    private void Scan(Camera cam)
    {
        _dead.Clear(); foreach (var kv in _converted) if (kv.Value.canvas == null) _dead.Add(kv.Key);
        foreach (int id in _dead) _converted.Remove(id);
        _dead.Clear(); foreach (var kv in _worldCanvases) if (kv.Value.canvas == null) _dead.Add(kv.Key);
        foreach (int id in _dead) _worldCanvases.Remove(id);
        _dead.Clear(); foreach (var kv in _cameraMasks) if (kv.Value.cam == null) _dead.Add(kv.Key);
        foreach (int id in _dead) _cameraMasks.Remove(id);
        _subtitles.Clear();
        foreach (var subtitle in Resources.FindObjectsOfTypeAll<_Code.DialogSystem.SubtitlesView>())
            // Move the whole subtitle panel, including its background and clipping area.
            if (subtitle != null && subtitle.gameObject.scene.IsValid() && subtitle._text != null) _subtitles.Add((subtitle.transform, Vector3.zero));
        int mask = 0;
        foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
        {
            if (c == null || !c.gameObject.scene.IsValid()) continue;
            var go = c.gameObject;
            // Nested canvases draw with their own object's layer. The window views sit (inactive, looking like
            // world-space roots) under the room canvas and must follow it into the room texture.
            var parent = c.transform.parent;
            var parentCanvas = parent != null ? parent.GetComponentInParent<Canvas>() : null;
            if (parentCanvas != null)
            {
                if (parentCanvas.gameObject.name == "RoomDisplayer" && go.layer != RoomLayer)
                {
                    int id = c.GetInstanceID();
                    if (!_worldCanvases.ContainsKey(id)) _worldCanvases[id] = (c, go.layer);
                    go.layer = RoomLayer;
                }
                continue;
            }
            if (!c.isRootCanvas) continue;
            if (c.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                int id = c.GetInstanceID();
                if (!_converted.ContainsKey(id)) _converted[id] = (c, c.sortingOrder, go.layer);
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = cam.nearClipPlane + 0.02f;
                c.sortingOrder = _converted[id].order + OverlayOrderBoost;
                if (go.layer != UiLayer) go.layer = UiLayer; // e.g. the cutscene black screen sits on Default
            }
            else if (c.renderMode == RenderMode.ScreenSpaceCamera)
            {
                if (_converted.ContainsKey(c.GetInstanceID()) && c.worldCamera != cam) c.worldCamera = cam;
                if (c.worldCamera != cam) continue;
                if (go.name == "RoomDisplayer")
                {
                    if (_roomCanvas == null) { _roomCanvas = c; _roomCanvasOriginalLayer = go.layer == RoomLayer ? UiLayer : go.layer; }
                    if (go.layer != RoomLayer) go.layer = RoomLayer;
                    continue;
                }
            }
            else
            {
                // World-space canvases (window views, calendar, phone pins) already live in the 3D scene and are drawn
                // by the eye camera where they stand; keep them out of the flat UI texture.
                if (go.layer == UiLayer)
                {
                    int id = c.GetInstanceID();
                    if (!_worldCanvases.ContainsKey(id)) _worldCanvases[id] = (c, go.layer);
                    go.layer = WorldUiLayer;
                }
                continue;
            }
            mask |= 1 << go.layer;
        }
        _uiMask = mask & ~(1 << RoomLayer) & ~(1 << WorldUiLayer) & ~1; // never the Default layer: that is the 3D world
    }

    /// <summary>Called right before the eyes are rendered (canvas layout for this frame is done).</summary>
    public void Render(Camera cam)
    {
        if (cam == null) return;
        if (Time.unscaledTime >= _nextScan || cam.GetInstanceID() != _camId)
        {
            _nextScan = Time.unscaledTime + 0.5f;
            _camId = cam.GetInstanceID();
            Scan(cam);
        }
        int w = Screen.width, h = Screen.height;
        if (VRConfig.WarnLowUiResolution.Value && (w < 1920 || h < 1080) && (_warnW != w || _warnH != h))
        { _warnW = w; _warnH = h; CorePlugin.Log.LogWarning($"VR UI is {w}x{h}. Use a game window of 1920x1080 or higher for sharper text; capture resolution stays window-sized to preserve layout."); }
        if (_ui == null || _ui.width != w || _ui.height != h)
        {
            Release();
            _ui = New(w, h, "NIVR UI");
            _room = New(w, h, "NIVR Room");
            // The window dome continues the picture past its borders: mirrored sideways, sky / ground stretched up and down.
            _room.wrapModeU = TextureWrapMode.Mirror;
            _room.wrapModeV = TextureWrapMode.Clamp;
        }
        // Keep the moved canvases on desktop, but never show VR-only markers/lasers on game cameras.
        if (!_cameraMasks.ContainsKey(cam.GetInstanceID())) _cameraMasks[cam.GetInstanceID()] = (cam, cam.cullingMask);
        cam.cullingMask = (cam.cullingMask | ExtraLayers) & ~(1 << VrOnlyLayer);

        var data = cam.GetUniversalAdditionalCameraData();
        var target = cam.targetTexture; int mask = cam.cullingMask; var flags = cam.clearFlags; var bg = cam.backgroundColor;
        bool hdr = cam.allowHDR, msaa = cam.allowMSAA, post = data != null && data.renderPostProcessing;
        try
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;  // the HDR colour buffer has no alpha channel
            cam.allowMSAA = false;
            if (data != null) data.renderPostProcessing = false;

            cam.targetTexture = _ui;
            cam.cullingMask = _uiMask;
            if (VRConfig.SubtitleOffset.Value > 0f)
                for (int i = 0; i < _subtitles.Count; i++)
                {
                    var text = _subtitles[i].text;
                    if (text == null) continue;
                    _subtitles[i] = (text, text.localPosition);
                    float offset = VRConfig.SubtitleOffset.Value;
                    var rectTransform = text.TryCast<RectTransform>();
                    if (rectTransform != null && text.parent != null)
                    {
                        // The stock caption already sits close to the window's lower edge.
                        // Clamp the requested lowering so the entire background/text remains in the UI texture.
                        var rect = rectTransform.rect;
                        float bottom = float.MaxValue;
                        for (int corner = 0; corner < 4; corner++)
                            bottom = Mathf.Min(bottom, cam.WorldToScreenPoint(text.TransformPoint(new Vector3((corner & 1) == 0 ? rect.xMin : rect.xMax, (corner & 2) == 0 ? rect.yMin : rect.yMax, 0f))).y);
                        float pixelsPerUnit = Mathf.Abs(cam.WorldToScreenPoint(text.parent.TransformPoint(text.localPosition + Vector3.up)).y - cam.WorldToScreenPoint(text.position).y);
                        if (pixelsPerUnit > 0.0001f) offset = Mathf.Min(offset, Mathf.Max(0f, bottom - 12f) / pixelsPerUnit);
                    }
                    text.localPosition -= new Vector3(0f, offset, 0f);
                }
            cam.Render();

            if (PictureActive)
            {
                cam.targetTexture = _room;
                cam.cullingMask = 1 << RoomLayer;
                cam.Render(); RoomRenders++;
            }
        }
        finally
        {
            if (VRConfig.SubtitleOffset.Value > 0f)
                foreach (var subtitle in _subtitles) if (subtitle.text != null) subtitle.text.localPosition = subtitle.pos;
            cam.targetTexture = target; cam.cullingMask = mask; cam.clearFlags = flags; cam.backgroundColor = bg;
            cam.allowHDR = hdr; cam.allowMSAA = msaa;
            if (data != null) data.renderPostProcessing = post;
        }
    }

    private static RenderTexture New(int w, int h, string name)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = name, hideFlags = HideFlags.HideAndDontSave };
        rt.Create();
        return rt;
    }

    private void Release()
    {
        if (_ui != null) { _ui.Release(); UnityEngine.Object.Destroy(_ui); _ui = null; }
        if (_room != null) { _room.Release(); UnityEngine.Object.Destroy(_room); _room = null; }
    }

    /// <summary>Puts the overlay canvases back (VR went inactive).</summary>
    public void Restore()
    {
        foreach (var kv in _converted)
        {
            var (c, order, layer) = kv.Value;
            if (c == null) continue;
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            c.gameObject.layer = layer;
        }
        _converted.Clear();
        foreach (var kv in _worldCanvases)
            if (kv.Value.canvas != null) kv.Value.canvas.gameObject.layer = kv.Value.layer;
        _worldCanvases.Clear();
        if (_roomCanvas != null) _roomCanvas.gameObject.layer = _roomCanvasOriginalLayer;
        _roomCanvas = null;
        _nextScan = 0f;
        _subtitles.Clear();
        foreach (var kv in _cameraMasks) if (kv.Value.cam != null) kv.Value.cam.cullingMask = kv.Value.mask;
        _cameraMasks.Clear();
    }
    public void Dispose() { Restore(); Release(); }
}
