using System;
using System.Collections.Generic;
using HarmonyLib;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using PlayerController = _Code.Player.PlayerController;

namespace NIVR.Core;

/// <summary>
/// Things that fight head tracking or hurt in a headset: the game's mouse-look, head bob / camera shake and a few
/// post effects. Everything here is undone when VR goes inactive.
/// </summary>
internal static class Comfort
{
    private static readonly Dictionary<int, (VolumeComponent comp, bool original)> s_volumes = new();
    private static readonly Dictionary<int, (CinemachineBasicMultiChannelPerlin noise, float original)> s_noise = new();
    private static PlayerController s_player;
    private static float s_originalBob = -1f;
    private static float s_nextScan;
    private static bool s_loggedIntercept;

    public static PlayerController Player => s_player != null ? s_player : null;

    public static void ApplyPatches(Harmony harmony)
    {
        try
        {
            var target = AccessTools.Method(typeof(PlayerController), nameof(PlayerController.HandleRotation));
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(Comfort), nameof(HandleRotationPrefix)));
        }
        catch (Exception e)
        {
            CorePlugin.Log.LogError($"Could not patch PlayerController.HandleRotation (mouse-look will fight head tracking): {e}");
        }
    }

    // The game's mouse / right-stick look. Skipped while VR is active; turning is done with VRRig + snap turn instead.
    private static bool HandleRotationPrefix()
    {
        if (!VRRig.IsActive || !VRConfig.DisableMouseLook.Value) return true;
        if (!s_loggedIntercept) { s_loggedIntercept = true; CorePlugin.Log.LogInfo("Mouse-look intercepted (PlayerController.HandleRotation skipped while VR is active)."); }
        return false;
    }

    private static _Scripts.Raycast.RaycastSource s_raySource;
    private static Camera s_rayOriginal;

    private static float s_rayReachOriginal = -1f;
    private static float s_nextRayScan;

    public static _Scripts.Raycast.RaycastSource RaySource => s_raySource != null ? s_raySource : null;

    /// <summary>The game's original interaction reach in metres (from the eye).</summary>
    public static float RayReach => s_rayReachOriginal > 0f ? s_rayReachOriginal : 1f;

    /// <summary>
    /// The game's centre-of-view interaction ray is cast from this camera instead of the game camera, reaching
    /// <paramref name="extraReach"/> metres further than the game's own distance (a hand sits behind the eye).
    /// </summary>
    public static void SetRaycastCamera(Camera cam, float extraReach = 0f)
    {
        if (s_raySource == null && Time.unscaledTime >= s_nextRayScan)
        {
            s_nextRayScan = Time.unscaledTime + 1f;
            foreach (var rs in Resources.FindObjectsOfTypeAll<_Scripts.Raycast.RaycastSource>())
                if (rs != null && rs.gameObject.scene.IsValid()) { s_raySource = rs; s_rayOriginal = rs._camera; s_rayReachOriginal = rs._maxRayDistance; break; }
        }
        if (s_raySource == null) return;
        if (cam == null) cam = s_rayOriginal; // null = the game's own camera
        if (cam != null && s_raySource._camera != cam) s_raySource._camera = cam;
        float reach = s_rayReachOriginal + extraReach;
        if (s_rayReachOriginal > 0f && !Mathf.Approximately(s_raySource._maxRayDistance, reach)) s_raySource._maxRayDistance = reach;
    }

    /// <summary>Turn the player body (not the head) by a number of degrees.</summary>
    public static bool Turn(float degrees)
    {
        if (s_player == null) return false;
        s_player.AddControlYawInput(degrees);
        return true;
    }

    public static void Tick()
    {
        if (Time.unscaledTime >= s_nextScan)
        {
            s_nextScan = Time.unscaledTime + 1f;
            Scan();
        }

        if (s_player != null && VRConfig.DisableHeadBob.Value)
        {
            if (s_originalBob < 0f) s_originalBob = s_player.cameraNoiseAmplitudeMultiplier;
            s_player.cameraNoiseAmplitudeMultiplier = 0f;
        }
        if (VRConfig.DisableCameraShake.Value)
        {
            // Every frame: the game animates these gains (PlayerController.UpdateNoiseAmplitude, MushroomDream.LowerShake).
            foreach (var kv in s_noise)
                if (kv.Value.noise != null) kv.Value.noise.AmplitudeGain = 0f;
        }
    }

    private static bool s_keepPost;

    /// <summary>While true (peephole view) the post effects are left as the game set them.</summary>
    public static bool KeepPostEffects
    {
        get => s_keepPost;
        set
        {
            if (value == s_keepPost) return;
            s_keepPost = value;
            if (value)
            {
                foreach (var kv in s_volumes)
                    if (kv.Value.comp != null) kv.Value.comp.active = kv.Value.original;
                s_volumes.Clear();
            }
            s_nextScan = 0f;
        }
    }

    private static void Scan()
    {
        s_dead.Clear(); foreach (var kv in s_noise) if (kv.Value.noise == null) s_dead.Add(kv.Key);
        foreach (int id in s_dead) s_noise.Remove(id);
        s_dead.Clear(); foreach (var kv in s_volumes) if (kv.Value.comp == null) s_dead.Add(kv.Key);
        foreach (int id in s_dead) s_volumes.Remove(id);
        if (s_player == null)
        {
            s_originalBob = -1f;
            s_player = null;
            foreach (var pc in Resources.FindObjectsOfTypeAll<PlayerController>())
                if (pc != null && pc.gameObject.scene.IsValid()) { s_player = pc; break; }
        }

        if (VRConfig.DisableCameraShake.Value)
        {
            foreach (var n in Resources.FindObjectsOfTypeAll<CinemachineBasicMultiChannelPerlin>())
            {
                if (n == null || !n.gameObject.scene.IsValid()) continue;
                int id = n.GetInstanceID();
                if (!s_noise.ContainsKey(id)) s_noise[id] = (n, n.AmplitudeGain);
            }
        }

        if (s_keepPost) return;
        foreach (var profile in Resources.FindObjectsOfTypeAll<VolumeProfile>())
        {
            if (profile == null) continue;
            var comps = profile.components;
            if (comps == null) continue;
            for (int i = 0; i < comps.Count; i++)
            {
                var c = comps[i];
                if (c == null || !ShouldDisable(c.GetIl2CppType().Name)) continue;
                int id = c.GetInstanceID();
                if (!s_volumes.ContainsKey(id)) s_volumes[id] = (c, c.active);
                if (c.active) c.active = false;
            }
        }
    }
    private static readonly List<int> s_dead = new();

    private static bool ShouldDisable(string typeName) => typeName switch
    {
        "Vignette" => VRConfig.DisableVignette.Value,
        "LensDistortion" => VRConfig.DisableLensDistortion.Value,
        "DepthOfField" => VRConfig.DisableDepthOfField.Value,
        "MotionBlur" => VRConfig.DisableMotionBlur.Value,
        "ChromaticAberration" => VRConfig.DisableChromaticAberration.Value,
        "FilmGrain" => VRConfig.DisableFilmGrain.Value,
        _ => false,
    };

    public static void Restore()
    {
        foreach (var kv in s_volumes)
            if (kv.Value.comp != null) kv.Value.comp.active = kv.Value.original;
        s_volumes.Clear();
        foreach (var kv in s_noise)
            if (kv.Value.noise != null) kv.Value.noise.AmplitudeGain = kv.Value.original;
        s_noise.Clear();
        if (s_player != null && s_originalBob >= 0f) s_player.cameraNoiseAmplitudeMultiplier = s_originalBob;
        s_originalBob = -1f;
        s_nextScan = 0f;
        if (s_raySource != null && s_rayOriginal != null) s_raySource._camera = s_rayOriginal;
        if (s_raySource != null && s_rayReachOriginal > 0f) s_raySource._maxRayDistance = s_rayReachOriginal;
        if (s_player != null && s_player.cameraTarget != null) s_player.cameraTarget.transform.localRotation = Quaternion.identity;
        s_raySource = null;
    }

    public static string Describe() => $"player={(s_player != null ? s_player.name : "none")} volumesOff={s_volumes.Count} noiseOff={s_noise.Count}";
}
