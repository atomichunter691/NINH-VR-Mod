using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace NIVR.Core;

public enum VRFlatScreenMode { Auto, Always, Off }

/// <summary>
/// Places the captured screen UI in the world for the eye camera (never visible to the game's cameras):
///  * UI panel: the transparent UI texture.
///    - Cursor locked (walking): centred on the headset's forward direction (= the interaction ray) and sized to the game camera's own frustum, so everything the game
///      positions with WorldToScreenPoint (crosshair, interaction hints) lies exactly over the object it belongs to and
///      the centre of the panel is the interaction ray.
///    - Otherwise: stands still in front of the game's camera position.
///  * Picture: the room illustration / window view, further away along the same view frustum.
///    - Room: the front wall of a closed box around the player whose side walls, floor and ceiling continue the edges
///      of the picture, drawn over the house. No black void, no view back into the hallway.
///    - Window: a sphere section around the player, depth-tested against the house, seen between the blinds.
///  * a dot for the cursor (the OS cursor is not part of any render).
/// </summary>
internal sealed class FlatScreen
{
    private const float EdgeStrip = 0.015f; // fraction of the picture stretched along a wall
    private const float BoxBehind = 1.5f;   // metres of room behind the player

    private GameObject _root;
    private Transform _ui, _room, _dot;
    private Renderer _uiR, _roomR, _dotR;
    private Material _uiMat, _roomMat;
    private readonly Transform[] _wall = new Transform[5];   // left, right, floor, ceiling, back
    private readonly Renderer[] _wallR = new Renderer[5];
    private readonly Material[] _wallMat = new Material[5];

    public static bool WorldMode => Cursor.lockState == CursorLockMode.Locked;

    private static bool Enabled => (VRRig.FlatScreenMode ?? VRConfig.FlatScreen.Value) != VRFlatScreenMode.Off;

    public void Replace() { }
    private Quaternion _lazyRotation = Quaternion.identity;
    private bool _lazyPlaced, _lazyMoving;
    private static Transform s_hud;
    internal static float HudYaw => s_hud != null ? s_hud.eulerAngles.y : float.NaN;
    public static Vector3 ReprojectHudPixel(Vector3 pixel, Camera cam)
    {
        if (!VRConfig.LazyFollowHud.Value || VRCoreBehaviour.InputMode != VRInputMode.Walk || s_hud == null || cam == null) return pixel;
        var ray = cam.ScreenPointToRay(pixel);
        var plane = new Plane(s_hud.forward, s_hud.position);
        if (!plane.Raycast(ray, out float distance) || distance <= 0f) return pixel;
        var local = s_hud.InverseTransformPoint(ray.GetPoint(distance));
        return new Vector3((local.x + 0.5f) * Screen.width, (local.y + 0.5f) * Screen.height, 0f);
    }

    private static Transform Quad(string name, Transform parent, Shader shader, int queue, out Renderer r, out Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col);
        go.transform.SetParent(parent, false);
        r = go.GetComponent<Renderer>();
        m = new Material(shader) { renderQueue = queue };
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.enabled = false;
        return go.transform;
    }

    // The view outside a window: the inside of a sphere section around the player. The picture covers the middle and
    // continues beyond its borders (mirrored sideways, bent over the viewer up and down, see SetDomeUvs), so there is
    // picture in every direction one can look through the window.
    private const float DomeYaw = 120f, DomePitch = 88f; // half extents in degrees
    private Transform _dome;
    private Renderer _domeR;
    private Material _domeMat;

    private const int DomeNx = 48, DomeNy = 64;
    private Mesh _domeMesh;
    private float _domeSpan, _domeAspect, _domeFill;

    /// <summary>
    /// Where the picture lies on the dome. Sideways it is angle-linear (and mirrored past its borders). Up and down it
    /// is true to scale in the middle and is drawn out more and more towards its top / bottom edge, which lies at
    /// <paramref name="fill"/> degrees: the picture bends over the viewer instead of ending at its natural height
    /// with its last row of pixels smeared up to the pole.
    /// </summary>
    private void SetDomeUvs(float span, float aspect, float fill)
    {
        if (span == _domeSpan && aspect == _domeAspect && fill == _domeFill) return;
        _domeSpan = span; _domeAspect = aspect; _domeFill = fill;
        float vSpan = span * aspect;                          // the picture's natural height in degrees
        fill = Mathf.Clamp(fill, vSpan * 0.5f, DomePitch);
        float n = 2f * fill / vSpan;                          // 1 = natural height (no bend)
        var uvs = new Vector2[(DomeNx + 1) * (DomeNy + 1)];
        for (int y = 0, i = 0; y <= DomeNy; y++)
        {
            float pitch = Mathf.Lerp(-DomePitch, DomePitch, (float)y / DomeNy);
            float k = Mathf.Min(Mathf.Abs(pitch) / fill, 1f);
            float v = 0.5f + Mathf.Sign(pitch) * 0.5f * (1f - Mathf.Pow(1f - k, n));
            for (int x = 0; x <= DomeNx; x++, i++)
                uvs[i] = new Vector2(0.5f + Mathf.Lerp(-DomeYaw, DomeYaw, (float)x / DomeNx) / span, v);
        }
        _domeMesh.uv = uvs;
    }

    private void BuildDome(Shader shader)
    {
        const int nx = DomeNx, ny = DomeNy;
        var verts = new Vector3[(nx + 1) * (ny + 1)];
        var tris = new int[nx * ny * 6];
        for (int y = 0, i = 0; y <= ny; y++)
            for (int x = 0; x <= nx; x++, i++)
            {
                float yaw = Mathf.Lerp(-DomeYaw, DomeYaw, (float)x / nx), pitch = Mathf.Lerp(-DomePitch, DomePitch, (float)y / ny);
                float cy = Mathf.Cos(yaw * Mathf.Deg2Rad), sy = Mathf.Sin(yaw * Mathf.Deg2Rad);
                float cp = Mathf.Cos(pitch * Mathf.Deg2Rad), sp = Mathf.Sin(pitch * Mathf.Deg2Rad);
                verts[i] = new Vector3(sy * cp, sp, cy * cp);
            }
        for (int y = 0, t = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int a = y * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                tris[t++] = a; tris[t++] = b; tris[t++] = c;
                tris[t++] = b; tris[t++] = d; tris[t++] = c;
            }
        var mesh = new Mesh { name = "NIVR Window Dome", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        _domeMesh = mesh;

        var go = new GameObject("WindowDome");
        go.transform.SetParent(_root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        // Drawn as solid geometry (depth-writing, first) so the window frame, blinds and curtains are drawn over it
        // whatever pass they render in (the highlighted frame is transparent and would otherwise be painted over).
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlit != null)
        {
            _domeMat = new Material(unlit) { renderQueue = 1990 };
            _domeMat.SetFloat("_Cull", 0f);
        }
        else
        {
            CorePlugin.Log.LogWarning("URP Unlit shader not found; the window view is drawn as an overlay.");
            _domeMat = new Material(shader) { renderQueue = 3990 };
            _domeMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);
        }
        mr.sharedMaterial = _domeMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.enabled = false;
        _dome = go.transform; _domeR = mr;
    }

    private void Build()
    {
        _root = new GameObject("NIVR Flat Screen");
        UnityEngine.Object.DontDestroyOnLoad(_root);
        var shader = Shader.Find("UI/Default");
        BuildDome(shader);
        string[] names = { "WallLeft", "WallRight", "Floor", "Ceiling", "WallBack" };
        for (int i = 0; i < 5; i++)
        {
            _wall[i] = Quad(names[i], _root.transform, shader, 3980 + i, out _wallR[i], out _wallMat[i]);
            _wallMat[i].SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        }
        // Each wall shows a thin strip of the picture's edge, stretched along the wall.
        _wallMat[0].mainTextureScale = new Vector2(EdgeStrip, 1f);
        _wallMat[1].mainTextureScale = new Vector2(EdgeStrip, 1f); _wallMat[1].mainTextureOffset = new Vector2(1f - EdgeStrip, 0f);
        _wallMat[2].mainTextureScale = new Vector2(1f, EdgeStrip);
        _wallMat[3].mainTextureScale = new Vector2(1f, EdgeStrip); _wallMat[3].mainTextureOffset = new Vector2(0f, 1f - EdgeStrip);
        _wallMat[4].color = Color.black;

        _room = Quad("Picture", _root.transform, shader, 3990, out _roomR, out _roomMat);
        _ui = Quad("UI", _root.transform, shader, 4000, out _uiR, out _uiMat);
        _dot = Quad("Cursor", _ui, shader, 4001, out _dotR, out var dotMat);
        dotMat.color = new Color(0.36f, 0.95f, 0.86f, 1f); // the laser's teal
        // UI/Default reads its depth test from this property.
        _uiMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        dotMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
    }

    /// <param name="scale">metres to world units (1 / WorldScale)</param>
    /// <param name="pictureDistance">0 = no room / window picture this frame</param>
    /// <param name="enclose">true = room (box around the player), false = window (quad behind the blinds)</param>
    public void BeginEyeRender(RenderTexture ui, RenderTexture room, Vector3 basePos, float baseYaw, Vector3 headPos, Quaternion headRot,
        float gameFov, float scale, float pictureDistance, bool enclose)
    {
        if (!Enabled || ui == null) return;
        if (_root == null) Build();
        Shader.SetGlobalInt("unity_GUIZTestMode", (int)CompareFunction.LessEqual);

        bool world = WorldMode;
        float dist = VRConfig.FlatScreenDistance.Value, width = VRConfig.FlatScreenWidth.Value;
        // Inside a room the picture (and the UI over it, which must stay aligned with it) fills much more of the view.
        if (enclose && pictureDistance > 0f)
            width = 2f * dist * Mathf.Tan(Mathf.Clamp(VRConfig.RoomViewDegrees.Value, 40f, 150f) * 0.5f * Mathf.Deg2Rad);
        float aspect = (float)ui.height / ui.width;
        var baseRot = Quaternion.Euler(0f, baseYaw, 0f);

        if (world)
        {
            if (VRConfig.LazyFollowHud.Value && VRCoreBehaviour.InputMode == VRInputMode.Walk)
            {
                if (!_lazyPlaced) { _lazyRotation = headRot; _lazyPlaced = true; }
                if (Quaternion.Angle(_lazyRotation, headRot) > VRConfig.HudFollowAngle.Value) _lazyMoving = true;
                if (_lazyMoving)
                {
                    _lazyRotation = Quaternion.Slerp(_lazyRotation, headRot, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
                    if (Quaternion.Angle(_lazyRotation, headRot) < 1f) _lazyMoving = false;
                }
                headRot = _lazyRotation;
            }
            else _lazyPlaced = _lazyMoving = false;
            float h = 2f * dist * Mathf.Tan(Mathf.Clamp(gameFov, 10f, 120f) * 0.5f * Mathf.Deg2Rad);
            _ui.SetPositionAndRotation(headPos + headRot * new Vector3(0f, 0f, dist * scale), headRot);
            _ui.localScale = new Vector3(h / aspect * scale, h * scale, 1f);
        }
        else
        {
            _ui.SetPositionAndRotation(basePos + baseRot * new Vector3(0f, 0f, dist * scale), baseRot);
            _ui.localScale = new Vector3(width * scale, width * aspect * scale, 1f);
        }
        _uiMat.mainTexture = ui;
        s_hud = _ui;
        _uiR.enabled = true;

        bool showPicture = room != null && pictureDistance > 0f;
        bool box = showPicture && enclose;
        bool dome = showPicture && !enclose && VRConfig.WindowDome.Value;
        if (dome)
        {
            float span = Mathf.Clamp(VRConfig.WindowViewDegrees.Value, 40f, 180f);
            _dome.SetPositionAndRotation(basePos, baseRot);
            _dome.localScale = Vector3.one * (pictureDistance * scale);
            _domeMat.mainTexture = room;
            SetDomeUvs(span, aspect, VRConfig.WindowFillDegrees.Value);
            showPicture = false;
        }
        _domeR.enabled = dome;
        if (showPicture)
        {
            // Along the game camera's own view (it is looking at the door / window); same view angle as the fixed UI panel.
            float k = pictureDistance / dist;
            float w = width * k * scale, h = width * aspect * k * scale, front = pictureDistance * scale;
            _room.SetPositionAndRotation(basePos + baseRot * new Vector3(0f, 0f, front), baseRot);
            _room.localScale = new Vector3(w, h, 1f);
            _roomMat.mainTexture = room;
            _roomMat.SetInt("unity_GUIZTestMode", (int)(box ? CompareFunction.Always : CompareFunction.LessEqual));

            if (box)
            {
                float back = BoxBehind * scale, depth = front + back, mid = (front - back) * 0.5f;
                Place(0, basePos + baseRot * new Vector3(-w * 0.5f, 0f, mid), baseRot * Quaternion.Euler(0f, -90f, 0f), depth, h, room);
                Place(1, basePos + baseRot * new Vector3(w * 0.5f, 0f, mid), baseRot * Quaternion.Euler(0f, 90f, 0f), depth, h, room);
                Place(2, basePos + baseRot * new Vector3(0f, -h * 0.5f, mid), baseRot * Quaternion.Euler(90f, 0f, 0f), w, depth, room);
                Place(3, basePos + baseRot * new Vector3(0f, h * 0.5f, mid), baseRot * Quaternion.Euler(-90f, 0f, 0f), w, depth, room);
                Place(4, basePos + baseRot * new Vector3(0f, 0f, -back), baseRot * Quaternion.Euler(0f, 180f, 0f), w, h, null);
            }
        }
        _roomR.enabled = showPicture;
        for (int i = 0; i < 5; i++) _wallR[i].enabled = box;

        _uiPlaced = true;

        // Cursor dot: where the controller laser meets the panel, or (no controllers) the desktop mouse.
        bool dot = false;
        if (!world)
        {
            try
            {
                Vector2? point = UiPointer.PanelPoint;
                if (!point.HasValue && !UiPointer.ControllersPresent)
                {
                    var mouse = Mouse.current;
                    if (mouse != null) { Vector2 p = mouse.position.ReadValue(); point = new Vector2(p.x / Screen.width, p.y / Screen.height); }
                }
                if (point.HasValue)
                {
                    float u = point.Value.x, v = point.Value.y;
                    if (u >= 0f && u <= 1f && v >= 0f && v <= 1f)
                    {
                        dot = true;
                        float size = 0.022f * scale; // metres (about 0.8 degrees at the panel distance)
                        _dot.localPosition = new Vector3(u - 0.5f, v - 0.5f, -0.001f);
                        _dot.localScale = new Vector3(size / _ui.localScale.x, size / _ui.localScale.y, 1f);
                    }
                }
            }
            catch (Exception) { }
        }
        _dotR.enabled = dot;
    }

    private bool _uiPlaced;

    /// <summary>Another quad showing the game window (the peephole picture) that the pointer uses instead of the panel.</summary>
    public Transform ScreenOverride { get; set; }

    /// <summary>World position of a point (uv 0..1 over the game window) on the screen panel as last drawn.</summary>
    public Vector3? PanelWorld(Vector2 uv)
    {
        Transform panel = ScreenOverride != null ? ScreenOverride : (_ui != null && _uiPlaced ? _ui : null);
        return panel != null ? panel.TransformPoint(new Vector3(uv.x - 0.5f, uv.y - 0.5f, 0f)) : null;
    }

    /// <summary>Where a ray meets the screen panel as it was last drawn (uv 0..1 over the game window).</summary>
    public bool PanelHit(Ray ray, out Vector2 uv, out Vector3 hit)
    {
        uv = default; hit = default;
        // The peephole picture shows the whole game window too, so the pointer works on it the same way.
        Transform panel = ScreenOverride != null ? ScreenOverride : (_ui != null && _uiPlaced && Enabled ? _ui : null);
        if (panel == null) return false;
        var plane = new Plane(panel.forward, panel.position);
        if (!plane.Raycast(ray, out float t) || t <= 0f) return false;
        hit = ray.GetPoint(t);
        var local = panel.InverseTransformPoint(hit);
        uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
        return uv.x >= -0.05f && uv.x <= 1.05f && uv.y >= -0.05f && uv.y <= 1.05f;
    }

    private void Place(int i, Vector3 pos, Quaternion rot, float w, float h, Texture tex)
    {
        _wall[i].SetPositionAndRotation(pos, rot);
        _wall[i].localScale = new Vector3(w, h, 1f);
        if (tex != null) _wallMat[i].mainTexture = tex;
    }

    public void EndEyeRender()
    {
        if (_uiR != null) _uiR.enabled = false;
        if (_roomR != null) _roomR.enabled = false;
        if (_dotR != null) _dotR.enabled = false;
        if (_domeR != null) _domeR.enabled = false;
        for (int i = 0; i < 5; i++) if (_wallR[i] != null) _wallR[i].enabled = false;
    }
    public void Dispose()
    {
        s_hud = null;
        if (_root == null) return;
        foreach (var r in _root.GetComponentsInChildren<Renderer>(true)) if (r != null && r.sharedMaterial != null) UnityEngine.Object.Destroy(r.sharedMaterial);
        if (_dome != null) UnityEngine.Object.Destroy(_dome.GetComponent<MeshFilter>().sharedMesh);
        UnityEngine.Object.Destroy(_root);
    }
}
