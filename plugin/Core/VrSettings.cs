using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.Localization.Components;
using SettingsInstance = _Code.Infrastructure.Settings.SettingsInstance;
using VolumeRow = _Code.Infrastructure.Settings.Sound.SoundSettingsVolumeSlider;
using UISelectable = _Code.Utils.UI.UISelectable;
using SettingsMarker = _Code.Player.Markers.SettingsMarker;

namespace NIVR.Core;

/// <summary>Native settings clones, held only for the lifetime of their scene-owned Settings instance.</summary>
internal static class VrSettings
{
    private sealed class Page
    {
        public SettingsInstance Owner;
        public GameObject Root;
        public readonly List<Action> Refresh = new();
        public readonly List<Selectable> Selectables = new();
    }
    private static readonly List<Page> s_pages = new();
    private static float s_nextScan;
    public static void ApplyPatches(Harmony harmony)
    {
        foreach (string method in new[] { "Show", "Initialize" })
            try { harmony.Patch(AccessTools.Method(typeof(SettingsInstance), method), postfix: new HarmonyMethod(typeof(VrSettings), nameof(Shown))); }
            catch (Exception e) { CorePlugin.Log.LogWarning($"VR settings hook {method}: {e.Message}"); }
        try { harmony.Patch(AccessTools.Method(typeof(SettingsInstance), "OnItemSelected"), prefix: new HarmonyMethod(typeof(VrSettings), nameof(NativeSelectionPrefix))); }
        catch (Exception e) { CorePlugin.Log.LogWarning("VR settings selection hook: " + e.Message); }
    }
    private static bool NativeSelectionPrefix(SettingsInstance __instance)
    {
        var es = __instance._eventSystem;
        var go = es != null ? es.currentSelectedGameObject : null;
        for (var t = go != null ? go.transform : null; t != null; t = t.parent)
            if (t.name == "NIVR VR Settings") return false;
        return true;
    }
    private static void Shown(SettingsInstance __instance) { if (VRConfig.VrSettings.Value) TryBuild(__instance); }
    public static void Tick()
    {
        if (Time.unscaledTime < s_nextScan) return;
        s_nextScan = Time.unscaledTime + 0.5f;
        for (int i = s_pages.Count - 1; i >= 0; i--)
        {
            var p = s_pages[i];
            if (p.Owner == null || p.Root == null) { s_pages.RemoveAt(i); continue; }
            p.Root.SetActive(VRConfig.VrSettings.Value);
            if (p.Owner.gameObject.activeInHierarchy && VRConfig.VrSettings.Value) foreach (var refresh in p.Refresh) refresh();
        }
        if (!VRConfig.VrSettings.Value) return;
        foreach (var settings in Resources.FindObjectsOfTypeAll<SettingsInstance>())
            if (settings != null && settings.gameObject.scene.IsValid() && settings.gameObject.activeInHierarchy) TryBuild(settings);
    }
    private static void TryBuild(SettingsInstance settings)
    {
        if (settings == null || settings._scrollRect == null || settings._scrollRect.content == null) return;
        foreach (var p in s_pages) if (p.Owner == settings) return;
        GameObject root = null;
        try
        {
            var content = settings._scrollRect.content;
            var rows = content.GetComponentsInChildren<VolumeRow>(true);
            var control = settings._controlSettings;
            if (rows.Length == 0 || control == null || control._gamepadVibrationToggle == null) return;
            var template = rows[0];
            var header = template.transform.parent.Find("Header");
            if (header == null) return;
            root = new GameObject("NIVR VR Settings") { layer = content.gameObject.layer };
            root.AddComponent<RectTransform>(); root.transform.SetParent(content, false); root.SetActive(false);
            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
            layout.spacing = 8f; layout.padding = new RectOffset(25, 25, 15, 15);
            var element = root.AddComponent<LayoutElement>(); element.preferredWidth = 890f;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(890f, 1700f);
            var fitter = root.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var page = new Page { Owner = settings, Root = root };
            var title = Clone(header.gameObject, root.transform, "Header");
            Label(title, "VR");
            var headerLayout = title.GetComponent<LayoutElement>() ?? title.AddComponent<LayoutElement>();
            headerLayout.ignoreLayout = false; headerLayout.minHeight = headerLayout.preferredHeight = 90f;
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Smooth turning", () => VRConfig.TurnMode.Value == VRTurnMode.Smooth,
                value => VRConfig.TurnMode.Value = value ? VRTurnMode.Smooth : VRTurnMode.Snap);
            SliderRow(page, template, "Snap angle", VRConfig.SnapTurnDegrees, 0f, 90f, 5f, "°");
            SliderRow(page, template, "Smooth speed", VRConfig.SmoothTurnSpeed, 15f, 180f, 5f, "°/s");
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Left pointer hand", () => VRConfig.PointerHand.Value == VRHand.Left, value => VRConfig.PointerHand.Value = value ? VRHand.Left : VRHand.Right);
            SliderRow(page, template, "Seated height", VRConfig.SeatedHeightOffset, -0.75f, 0.75f, 0.01f, "m");
            ButtonRow(page, settings, "Recenter now", VRRig.Recenter);
            SliderRow(page, template, "World scale", VRConfig.WorldScale, 0.5f, 2f, 0.01f, "x");
            SliderRow(page, template, "Room width", VRConfig.RoomViewDegrees, 40f, 150f, 1f, "°");
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Room enclosure", () => VRConfig.RoomEnclosure.Value, value => VRConfig.RoomEnclosure.Value = value);
            SliderRow(page, template, "Window width", VRConfig.WindowViewDegrees, 40f, 180f, 1f, "°");
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Window dome", () => VRConfig.WindowDome.Value, value => VRConfig.WindowDome.Value = value);
            SliderRow(page, template, "Peephole size", VRConfig.PeepholeFov, 20f, 120f, 1f, "°");
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Left peephole eye", () => VRConfig.PeepholeEye.Value == VRHand.Left, value => VRConfig.PeepholeEye.Value = value ? VRHand.Left : VRHand.Right);
            SliderRow(page, template, "HUD distance", VRConfig.FlatScreenDistance, 0.6f, 3f, 0.05f, "m");
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Comfort fade", () => VRConfig.ComfortFade.Value, value => VRConfig.ComfortFade.Value = value);
            SliderRow(page, template, "Vibration", VRConfig.VibrationStrength, 0f, 2f, 0.05f, "x");
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Motion vignette", () => VRConfig.MotionVignette.Value, value => VRConfig.MotionVignette.Value = value);
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Lazy follow HUD", () => VRConfig.LazyFollowHud.Value, value => VRConfig.LazyFollowHud.Value = value);
            SliderRow(page, template, "Lower subtitles", VRConfig.SubtitleOffset, 0f, 250f, 5f, "");
            ToggleRow(page, control._gamepadVibrationToggle.gameObject, "Physical crouch", () => VRConfig.PhysicalCrouch.Value, value => VRConfig.PhysicalCrouch.Value = value);
            LinkNavigation(page, content);
            s_pages.Add(page); root.SetActive(VRConfig.VrSettings.Value);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            CorePlugin.Log.LogInfo($"VR settings: added {page.Selectables.Count} native controls to {settings.name} (laser + gamepad, cfg saved live).");
        }
        catch (Exception e)
        { if (root != null) UnityEngine.Object.Destroy(root); CorePlugin.LogThrottled("vr-settings", "VR settings injection failed: " + e); }
    }

    private static GameObject Clone(GameObject source, Transform parent, string name)
    {
        var go = UnityEngine.Object.Instantiate(source, parent, false); go.name = name;
        foreach (var loc in go.GetComponentsInChildren<LocalizeStringEvent>(true)) { loc.enabled = false; UnityEngine.Object.Destroy(loc); }
        foreach (var first in go.GetComponentsInChildren<_Code.Player.Markers.FirstMarker>(true)) { first.enabled = false; UnityEngine.Object.Destroy(first); }
        foreach (var ui in go.GetComponentsInChildren<UISelectable>(true)) { ui.Selected = null; ui.Deselected = null; }
        go.SetActive(true); return go;
    }
    private static void Label(GameObject row, string text)
    {
        var texts = row.GetComponentsInChildren<TMP_Text>(true);
        if (texts.Length > 0) SetText(texts[0], text);
    }
    private static void SetText(TMP_Text component, string text)
    {
        // RTLTMPro hides TMP_Text.text rather than overriding it. Its Update restores
        // OriginalText, so changing only the base property resurrects the cloned label.
        var rtl = component.TryCast<RTLTMPro.RTLTextMeshPro>();
        if (rtl != null) { if (rtl.text != text) rtl.text = text; }
        else if (component.text != text) component.text = text;
    }
    private static void Height(GameObject go, float height)
    {
        var layout = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        layout.preferredHeight = height; layout.minHeight = height; layout.preferredWidth = 840f;
    }
    private static void Register(Page page, Selectable selectable)
    {
        if (selectable.GetComponent<SettingsMarker>() == null) selectable.gameObject.AddComponent<SettingsMarker>();
        var ui = selectable.GetComponent<UISelectable>() ?? selectable.gameObject.AddComponent<UISelectable>();
        ui.Selected = (Il2CppSystem.Action<UnityEngine.EventSystems.BaseEventData>)new Action<UnityEngine.EventSystems.BaseEventData>(e =>
        { try { EnsureVisible(page, selectable); } catch (Exception ex) { CorePlugin.LogThrottled("vr-scroll", "VR settings scroll: " + ex.Message); } });
        page.Selectables.Add(selectable);
    }
    private static void EnsureVisible(Page page, Selectable selectable)
    {
        var scroll = page.Owner._scrollRect;
        if (scroll == null || scroll.viewport == null || scroll.content == null) return;
        var rt = selectable.GetComponent<RectTransform>();
        if (rt == null) return;
        var item = rt.rect;
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            var point = scroll.viewport.InverseTransformPoint(rt.TransformPoint(new Vector3((i & 1) == 0 ? item.xMin : item.xMax, (i & 2) == 0 ? item.yMin : item.yMax, 0f)));
            min = Mathf.Min(min, point.y); max = Mathf.Max(max, point.y);
        }
        var rect = scroll.viewport.rect;
        float delta = min < rect.yMin + 15f ? rect.yMin + 15f - min
            : max > rect.yMax - 15f ? rect.yMax - 15f - max : 0f;
        if (Mathf.Abs(delta) > 0.01f) { scroll.StopMovement(); scroll.content.anchoredPosition += new Vector2(0f, delta); }
    }
    private static void SliderRow(Page page, VolumeRow source, string label, ConfigEntry<float> entry, float min, float max, float step, string unit)
    {
        var go = Clone(source.gameObject, page.Root.transform, "VR " + label);
        var old = go.GetComponent<VolumeRow>(); old.enabled = false; UnityEngine.Object.Destroy(old);
        var slider = go.GetComponentInChildren<Slider>(true);
        ClearEvent(slider.onValueChanged);
        slider.onValueChanged = new Slider.SliderEvent(); slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = false;
        var name = go.transform.Find("Name").GetComponent<TMP_Text>(); SetText(name, label); name.fontSize = 32f;
        var nameRt = name.GetComponent<RectTransform>(); nameRt.sizeDelta = new Vector2(340f, 65f); nameRt.anchoredPosition = new Vector2(170f, 0f);
        var sliderRt = slider.GetComponent<RectTransform>(); sliderRt.sizeDelta = new Vector2(280f, 50f); sliderRt.anchoredPosition = new Vector2(90f, 0f);
        var valueText = go.transform.Find("VolumeValue").GetComponent<TMP_Text>(); valueText.fontSize = 30f;
        valueText.GetComponent<RectTransform>().anchoredPosition = new Vector2(345f, 0f);
        void Refresh() { slider.SetValueWithoutNotify(entry.Value); SetText(valueText, entry.Value.ToString(step >= 1f ? "0" : "0.00", CultureInfo.InvariantCulture) + unit); }
        slider.onValueChanged.AddListener((UnityAction<float>)new Action<float>(v =>
        { entry.Value = Mathf.Clamp(Mathf.Round(v / step) * step, min, max); Refresh(); VRConfig.File.Save(); }));
        Height(go, 80f); Register(page, slider); page.Refresh.Add(Refresh); Refresh();
    }
    private static void ToggleRow(Page page, GameObject source, string label, Func<bool> read, Action<bool> write)
    {
        var go = Clone(source, page.Root.transform, "VR " + label);
        var toggle = go.GetComponent<Toggle>(); ClearEvent(toggle.onValueChanged); toggle.group = null; toggle.onValueChanged = new Toggle.ToggleEvent();
        Label(go, label); Height(go, 65f);
        void Refresh() => toggle.SetIsOnWithoutNotify(read());
        toggle.onValueChanged.AddListener((UnityAction<bool>)new Action<bool>(v => { write(v); VRConfig.File.Save(); }));
        Register(page, toggle); page.Refresh.Add(Refresh); Refresh();
    }
    private static void ButtonRow(Page page, SettingsInstance owner, string label, Action action)
    {
        Button source = null;
        foreach (var button in owner.GetComponentsInChildren<Button>(true)) if (button.name == "Back") { source = button; break; }
        if (source == null) throw new InvalidOperationException("Settings Back button template missing");
        var go = Clone(source.gameObject, page.Root.transform, "VR " + label);
        // HoverableButton's cloned event hooks belong to the old page. Keep its native images and TMP text only.
        foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (b.GetIl2CppType().Name == "HoverableButton" || b.GetIl2CppType().Name == "AnimatedImage") { b.enabled = false; UnityEngine.Object.Destroy(b); }
        var buttonClone = go.GetComponent<Button>(); ClearEvent(buttonClone.onClick); buttonClone.onClick = new Button.ButtonClickedEvent();
        buttonClone.onClick.AddListener((UnityAction)action); Label(go, label); Height(go, 80f); Register(page, buttonClone);
    }
    private static void ClearEvent(UnityEventBase evt)
    {
        if (evt == null) return;
        evt.RemoveAllListeners();
        if (evt.m_PersistentCalls != null) evt.m_PersistentCalls.Clear();
        evt.DirtyPersistentCalls();
    }
    private static bool InPage(Transform t, Transform content)
    {
        for (; t != null && t != content; t = t.parent) if (!t.gameObject.activeSelf) return false;
        return t == content;
    }
    private static void LinkNavigation(Page page, RectTransform content)
    {
        var native = new List<Selectable>();
        foreach (var s in content.GetComponentsInChildren<Selectable>(true))
            if (!s.transform.IsChildOf(page.Root.transform) && s.GetComponent<SettingsMarker>() != null && InPage(s.transform, content)) native.Add(s);
        var all = new List<Selectable>(native); all.AddRange(page.Selectables);
        for (int i = 0; i < all.Count; i++)
        {
            var nav = all[i].navigation; nav.mode = Navigation.Mode.Explicit;
            nav.selectOnUp = i > 0 ? all[i - 1] : all[all.Count - 1];
            nav.selectOnDown = i + 1 < all.Count ? all[i + 1] : all[0]; all[i].navigation = nav;
        }
        var old = page.Owner._selectables;
        var uiAll = new List<UISelectable>();
        if (old != null) for (int i = 0; i < old.Length; i++) if (old[i] != null) uiAll.Add(old[i]);
        foreach (var s in page.Selectables) uiAll.Add(s.GetComponent<UISelectable>());
        page.Owner._selectables = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UISelectable>(uiAll.ToArray());
    }
    internal static void DebugCommand(string[] args)
    {
        foreach (var page in s_pages)
        {
            if (page.Owner == null || !page.Owner.gameObject.activeInHierarchy) continue;
            if (args.Length > 1 && args[1] == "scroll") page.Owner._scrollRect.verticalNormalizedPosition = float.Parse(args[2], CultureInfo.InvariantCulture);
            var canvas = page.Root.GetComponentInParent<Canvas>();
            foreach (var selectable in page.Selectables)
            {
                var pos = RectTransformUtility.WorldToScreenPoint(canvas != null ? canvas.worldCamera : null, selectable.transform.position);
                CorePlugin.Log.LogInfo($"vr setting: {selectable.transform.parent.name}/{selectable.name} px=({pos.x:F0},{pos.y:F0}) value={(selectable.TryCast<Slider>() != null ? selectable.Cast<Slider>().value.ToString("F2") : selectable.TryCast<Toggle>() != null ? selectable.Cast<Toggle>().isOn.ToString() : "button")}");
            }
        }
    }
}
