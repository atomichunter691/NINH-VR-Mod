using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace NIVR.Core;

/// <summary>Eye-only blackout and peripheral mask. No game post-effect or desktop canvas is modified.</summary>
internal sealed class ComfortOverlay
{
    private GameObject _fade, _vignette;
    private Material _fadeMat, _vignetteMat;
    private Texture2D _mask;
    private readonly Il2CppReferenceArray<Collider> _colliders = new(64);
    private float _blackUntil, _alpha, _vignetteAmount;
    private bool _blocked;
    private static bool s_transition;
    public float Alpha => _alpha;
    public bool Blocked => _blocked;

    public static void ApplyPatches(Harmony h)
    {
        Hook(h, typeof(_Code.Infrastructure.ActionableObjects.AActionableObjectView), "StartLooking");
        Hook(h, typeof(_Code.Infrastructure.Player.PlayerService), "TeleportTo");
        Hook(h, typeof(_Code.Infrastructure.Scenes.ScenesChanger), "OpenScene");
    }

    private static void Hook(Harmony h, Type type, string name)
    {
        try { h.Patch(AccessTools.Method(type, name), prefix: new HarmonyMethod(typeof(ComfortOverlay), nameof(TransitionPrefix))); }
        catch (Exception e) { CorePlugin.Log.LogWarning($"Comfort transition hook {name}: {e.Message}"); }
    }

    private static void TransitionPrefix() { if (VRRig.IsActive) s_transition = true; }

    public void Jump()
    {
        if (!VRConfig.ComfortFade.Value) return;
        _blackUntil = Time.unscaledTime + VRConfig.FadeSeconds.Value * 0.35f;
        _alpha = 1f;
    }

    public void Update(Transform head, bool world, float motion)
    {
        if (s_transition) { s_transition = false; Jump(); }
        _blocked = false;
        if (world && head != null && VRConfig.HeadCollisionFade.Value)
        {
            float scale = VRRig.Origin != null ? VRRig.Origin.localScale.x : 1f;
            int count = Physics.OverlapSphereNonAlloc(head.position, VRConfig.HeadCollisionRadius.Value * scale, _colliders, 1, QueryTriggerInteraction.Ignore);
            var pc = Comfort.Player;
            for (int i = 0; i < count; i++)
            {
                var col = _colliders[i];
                if (col == null || (pc != null && col.transform.IsChildOf(pc.transform))) continue;
                _blocked = true; break;
            }
        }
        if (_blocked) _alpha = 1f;
        else if (!VRConfig.ComfortFade.Value || Time.unscaledTime >= _blackUntil)
            _alpha = Mathf.MoveTowards(_alpha, 0f, Time.unscaledDeltaTime / (VRConfig.FadeSeconds.Value * 0.65f));
        _vignetteAmount = Mathf.MoveTowards(_vignetteAmount, VRConfig.MotionVignette.Value && world ? Mathf.Clamp01(motion) : 0f, Time.unscaledDeltaTime * 5f);
    }

    private GameObject Quad(string name, int queue, out Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name; go.layer = UiCapture.VrOnlyLayer;
        UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        UnityEngine.Object.DontDestroyOnLoad(go);
        mat = new Material(Shader.Find("UI/Default")) { renderQueue = queue };
        mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        var r = go.GetComponent<Renderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        go.SetActive(false);
        return go;
    }

    public void BeginEye(Camera eye)
    {
        if (_alpha < 0.001f && _vignetteAmount < 0.001f) return;
        if (_fade == null)
        {
            _fade = Quad("NIVR Comfort Blackout", 4999, out _fadeMat);
            _vignette = Quad("NIVR Motion Vignette", 4998, out _vignetteMat);
            _mask = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "NIVR Procedural Vignette", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float radius = new Vector2((x - 63.5f) / 63.5f, (y - 63.5f) / 63.5f).magnitude;
                    pixels[y * 128 + x] = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.95f, radius)));
                }
            _mask.SetPixels(pixels); _mask.Apply(); _vignetteMat.mainTexture = _mask;
        }
        Place(_fade.transform, eye); Place(_vignette.transform, eye);
        _fadeMat.color = new Color(0f, 0f, 0f, _alpha);
        _vignetteMat.color = new Color(1f, 1f, 1f, _vignetteAmount * 0.85f);
        _fade.SetActive(_alpha > 0.001f); _vignette.SetActive(_vignetteAmount > 0.001f);
    }

    private static void Place(Transform quad, Camera cam)
    {
        var m = cam.projectionMatrix; float d = cam.nearClipPlane + 0.015f;
        // Cover asymmetric OpenXR frusta as well as the symmetric simulator.
        quad.SetPositionAndRotation(cam.transform.TransformPoint(new Vector3(d * m.m02 / m.m00, d * m.m12 / m.m11, d)), cam.transform.rotation);
        quad.localScale = new Vector3(2.02f * d / m.m00, 2.02f * d / m.m11, 1f);
    }

    public void EndEye() { if (_fade != null) _fade.SetActive(false); if (_vignette != null) _vignette.SetActive(false); }
    public void Reset() { EndEye(); _alpha = _vignetteAmount = 0f; _blackUntil = 0f; s_transition = false; }
    public void Dispose()
    {
        EndEye();
        if (_fade != null) UnityEngine.Object.Destroy(_fade);
        if (_vignette != null) UnityEngine.Object.Destroy(_vignette);
        if (_fadeMat != null) UnityEngine.Object.Destroy(_fadeMat);
        if (_vignetteMat != null) UnityEngine.Object.Destroy(_vignetteMat);
        if (_mask != null) UnityEngine.Object.Destroy(_mask);
    }
}
