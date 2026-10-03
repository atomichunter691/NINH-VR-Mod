using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace NIVR.Core;

/// <summary>How the controllers drive the game this frame.</summary>
internal enum VRInputMode
{
    /// <summary>Hallway: gamepad (left stick walks, A interacts), right stick = snap turn, laser = interaction ray.</summary>
    Walk,
    /// <summary>Screens built for a gamepad (radio knob = right stick, bands = grips): plain gamepad, no pointer.</summary>
    Gamepad,
    /// <summary>Anything with a mouse cursor (rooms, dialogs, phone, fridge, menus): the laser is the mouse.</summary>
    Pointer,
}

/// <summary>
/// The controller laser.
///  * Pointer mode: the game is switched to its own Keyboard &amp; Mouse scheme, paired with a virtual mouse and keyboard
///    that this class feeds: the mouse sits where the laser meets the screen panel, the pointing hand's trigger (or A)
///    is the left button, B / X / Y / Menu / grip / sticks become the keys the game uses for them. Everything the PC
///    game does with a mouse (hover, click, hold, drag) then works unchanged.
///  * Walk mode: the game's interaction ray is cast from the pointing hand; the laser is only drawn while the game
///    offers an interaction, and the game's crosshair is moved onto the spot the laser hits.
///  * Gamepad mode: the game's Gaypad scheme with the virtual gamepad, no laser.
/// </summary>
internal sealed unsafe class UiPointer
{
    private const string KbmScheme = "Keyboard And Mouse", PadScheme = "Gaypad";
    private const float ExtraReach = 0.4f; // the hand is behind the eye; the game measures its reach from the eye

    private static readonly Color LaserColor = new(0.36f, 0.95f, 0.86f, 1f); // the game's teal UI text

    private InputDevice _mouse, _keyboard;
    private Mouse _mouseT; private Keyboard _keyboardT;
    private bool _failed;
    private int _hand = -1;
    private Vector2 _lastPx; private bool _haveLastPx;
    private readonly bool[] _keys = new bool[128];
    private bool _sentKeyboardOnce;
    private float _nextSchemeCheck, _nextScroll;
    private VRInputMode _lastMode = (VRInputMode)(-1);

    private GameObject _laserGo;
    private LineRenderer _laser;
    private Camera _handRayCam, _headRayCam;
    private _Code.Menues.OpenRoomView _marker;
    private float _nextMarkerScan;
    private Transform _lastTarget;

    /// <summary>World point the game's crosshair is drawn on (walk mode, laser on a target); null = game default.</summary>
    public static Vector3? CrosshairTarget { get; private set; }

    /// <summary>Panel point (0..1) of the laser in pointer mode; null when the laser is not on the panel.</summary>
    public static Vector2? PanelPoint { get; private set; }

    /// <summary>What Input.mousePosition reports while pointing (kept between frames: game scripts may run before us).</summary>
    public static Vector2? LegacyMouse { get; private set; }

    /// <summary>Pointer screens use the game's keyboard &amp; mouse scheme (else: its gamepad scheme + gamepad cursor).</summary>
    public static bool MouseScheme => VRConfig.PointerScheme.Value == VRPointerScheme.Mouse;

    /// <summary>
    /// Puts the game's cursor where the laser points: the virtual mouse (and the OS cursor) always; in the gamepad
    /// scheme also the game's own gamepad cursor, which is what its gamepad A / hover logic uses.
    /// </summary>
    private void PlaceCursor(Vector2 px, bool leftButton)
    {
        LegacyMouse = px;
        // Gamepad scheme: no virtual mouse (a hovering mouse puts buttons in their "highlighted" look, which hides the
        // gamepad "selected" look); the selection, the game's gamepad cursor and the OS cursor carry the position.
        SendMouse(px, leftButton, MouseScheme);
        if (MouseScheme) return;
        var gc = GameState.Input != null ? GameState.Input._gamepadCursor : null;
        if (gc != null) gc._cursorPosition = px;
        SelectUnderPointer(px);
    }

    private UnityEngine.EventSystems.PointerEventData _ped;
    private Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> _hits;
    private int _lastHoveredSelectable;
    private UnityEngine.UI.Selectable _hoverSel;
    private bool _trigPrev, _swallow;
    /// <summary>The pointing hand's trigger click was delivered straight to the button under the laser; do not also send A.</summary>
    public static bool SwallowTrigger { get; private set; }
    private bool _navOverride;

    // Gamepad menus act on the selected button (A = submit). The button under the laser becomes the selection when
    // the laser moves onto it; stick / d-pad navigation keeps working until the laser moves to another button.
    public static bool DebugHover;
    public static string HoverInfo = "";

    private void SelectUnderPointer(Vector2 px)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return;
        if (_ped == null || _ped.Pointer == IntPtr.Zero) _ped = new UnityEngine.EventSystems.PointerEventData(es);
        _hits ??= new Il2CppSystem.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        _ped.position = px;
        _hits.Clear();
        es.RaycastAll(_ped, _hits);
        UnityEngine.UI.Selectable sel = null;
        for (int i = 0; i < _hits.Count && sel == null; i++)
        {
            var go = _hits[i].gameObject;
            if (go == null) continue;
            var s = go.GetComponentInParent<UnityEngine.UI.Selectable>();
            if (s != null && s.IsActive() && s.IsInteractable()) sel = s;
            else if (go.GetComponentInParent<Canvas>() != null) break; // the topmost UI under the laser is not a button
        }
        var cur = es.currentSelectedGameObject;
        int curId = cur != null ? cur.GetInstanceID() : 0;
        if (curId != _lastCurrentId) { _selectionChanges++; _lastCurrentId = curId; }
        if (VRConfig.Verbose.Value || DebugHover)
        {
            var sb = new System.Text.StringBuilder($"es={es.name} changes={_selectionChanges} sets={_selectionSets} foreignCursor={ForeignCursorMoves} frame={Time.frameCount} hits={_hits.Count}:");
            for (int i = 0; i < _hits.Count && i < 4; i++) sb.Append(' ').Append(_hits[i].gameObject != null ? _hits[i].gameObject.name : "null");
            sb.Append($" sel={(sel != null ? sel.name : "none")} current={(es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "none")}");
            HoverInfo = sb.ToString();
        }
        _hoverSel = sel;
        ScrollUnderPointer();
        int id = sel != null ? sel.GetInstanceID() : 0;
        if (id != _lastHoveredSelectable) { _lastHoveredSelectable = id; _navOverride = false; if (id != 0) Haptics.Tick(HandSide, "hover"); }
        // Stick / d-pad navigation takes over until the laser moves to another button.
        if (VRRig.LeftController.Stick.sqrMagnitude > 0.25f || VRRig.RightController.Stick.sqrMagnitude > 0.25f || VirtualGamepad.DebugNavigating) _navOverride = true;
        // Kept every frame: the game re-asserts its own gamepad selection.
        if (sel != null && !_navOverride && es.currentSelectedGameObject != sel.gameObject) { es.SetSelectedGameObject(sel.gameObject); _selectionSets++; }
        // Sliders need a value change as well as selection when the laser is clicked or held.
        var slider = sel != null ? sel.TryCast<UnityEngine.UI.Slider>() : null;
        if (slider != null && !_navOverride && ButtonsDown(Hand))
        {
            var handle = slider.handleRect;
            var area = handle != null ? handle.parent.TryCast<RectTransform>() : slider.GetComponent<RectTransform>();
            var canvas = slider.GetComponentInParent<Canvas>();
            if (area != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(area, px, canvas != null ? canvas.worldCamera : null, out var local))
            {
                var rect = area.rect;
                bool vertical = slider.direction == UnityEngine.UI.Slider.Direction.BottomToTop || slider.direction == UnityEngine.UI.Slider.Direction.TopToBottom;
                float value = vertical ? Mathf.InverseLerp(rect.yMin, rect.yMax, local.y) : Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
                if (slider.direction == UnityEngine.UI.Slider.Direction.RightToLeft || slider.direction == UnityEngine.UI.Slider.Direction.TopToBottom) value = 1f - value;
                slider.normalizedValue = value;
            }
        }
    }

    /// <summary>
    /// Before the virtual gamepad is fed: a trigger press on a button under the laser is delivered to that button
    /// directly (its own submit handler) instead of as gamepad A. A goes through the EventSystem, which needs the game
    /// window to have focus and the laser's selection to survive the frame; a direct submit does not.
    /// </summary>
    public void PrePress(VRInputMode mode)
    {
        SwallowTrigger = false;
        bool on = mode == VRInputMode.Pointer && !MouseScheme && ControllersPresent && !_failed;
        if (!on) { _trigPrev = _swallow = false; return; }
        bool trig = ButtonGate.Down(VRRig.Controller(HandSide), VRButton.Trigger);
        if (trig && !_trigPrev && _hoverSel != null)
        {
            try
            {
                var sel = _hoverSel;
                if (sel != null && sel.IsActive() && sel.IsInteractable() && sel.TryCast<UnityEngine.UI.Slider>() == null)
                {
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    var submit = sel.TryCast<UnityEngine.EventSystems.ISubmitHandler>();
                    if (es != null && submit != null)
                    {
                        if (es.currentSelectedGameObject != sel.gameObject) es.SetSelectedGameObject(sel.gameObject);
                        submit.OnSubmit(new UnityEngine.EventSystems.BaseEventData(es));
                        _swallow = true;
                        Haptics.Tick(HandSide, "submit", 0.4f, 0.04f);
                        CorePlugin.Log.LogInfo($"VR UI click -> {sel.name} ({sel.GetIl2CppType().Name}) focus={Application.isFocused}/{es.isFocused}");
                    }
                }
            }
            catch (Exception e) { _swallow = false; CorePlugin.LogThrottled("direct-submit", "Direct UI click failed, using gamepad A instead: " + e.Message); }
        }
        if (!trig) _swallow = false;
        _trigPrev = trig;
        SwallowTrigger = _swallow;
    }

    // Right stick scrolls the list under the laser (the game's own scroll follows the gamepad cursor, which the laser owns).
    private void ScrollUnderPointer()
    {
        float sy = VirtualGamepad.RightStickValue.y;
        if (Mathf.Abs(sy) < 0.3f) return;
        UnityEngine.UI.ScrollRect scroll = null;
        for (int i = 0; i < _hits.Count && scroll == null; i++)
        {
            var go = _hits[i].gameObject;
            if (go != null) scroll = go.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        }
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        float range = scroll.content.rect.height - scroll.viewport.rect.height;
        if (range < 1f) return;
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + sy * 1100f * Mathf.Min(Time.unscaledDeltaTime, 0.05f) / range);
    }

    private int _lastCurrentId, _selectionChanges, _selectionSets;

    public static bool ControllersPresent => VRRig.RightController.IsConnected || VRRig.LeftController.IsConnected;

    public VRController Hand => VRRig.Controller((VRHand)_hand);

    /// <summary>The pointing hand (its trigger is the click / interact button).</summary>
    public VRHand HandSide => _hand < 0 ? VRConfig.PointerHand.Value : (VRHand)_hand;

    public void Tick(VRInputMode mode, FlatScreen screen, VirtualGamepad pad, Transform head)
    {
        CrosshairTarget = null;
        PanelPoint = null;
        bool laser = false;
        Vector3 laserFrom = default, laserTo = default;
        if (_failed) { SetLaser(false, default, default); return; }
        try
        {
            ChooseHand();
            var hand = Hand;
            bool tracked = hand.IsAimTracked && hand.Aim != null;
            bool controllers = ControllersPresent;

            if (mode != _lastMode) { _nextSchemeCheck = 0f; if (_lastMode == VRInputMode.Pointer) ReleaseAll(); _lastMode = mode; }
            bool mouseScheme = MouseScheme;
            if (controllers) EnsureScheme(mode == VRInputMode.Pointer && mouseScheme, pad);

            if (mode != VRInputMode.Pointer || !controllers) LegacyMouse = null;
            if (mode == VRInputMode.Pointer && controllers)
            {
                if (tracked)
                {
                    var ray = new Ray(hand.Aim.position, hand.Aim.forward);
                    if (screen.PanelHit(ray, out var uv, out var hit))
                    {
                        laser = true; laserFrom = ray.origin; laserTo = hit;
                        PanelPoint = uv;
                        var px = new Vector2(Mathf.Clamp01(uv.x) * Screen.width, Mathf.Clamp01(uv.y) * Screen.height);
                        PlaceCursor(px, mouseScheme && ButtonsDown(hand));
                    }
                    else PlaceCursor(_haveLastPx ? _lastPx : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), mouseScheme && ButtonsDown(hand));
                }
                if (mouseScheme) SendKeys();
            }

            // Interaction ray of the hallway (also used by look-at views: harmless, they do not raycast).
            UpdateRayCamera(hand, tracked, head);
            if (mode == VRInputMode.Walk && tracked && VRConfig.InteractionRay.Value == VRInteractionRay.Controller)
            {
                var ray = new Ray(hand.Aim.position, hand.Aim.forward);
                float reach = Comfort.RayReach + ExtraReach;
                var rs = Comfort.RaySource;
                int mask = rs != null ? rs._layerMask : 1;
                Vector3 end = Physics.Raycast(ray, out var rh, reach, mask) ? rh.point : ray.origin + ray.direction * reach;
                var marker = Marker();
                bool showing = marker != null && marker._isShowing && marker._currentTarget != null;
                if (showing)
                {
                    CrosshairTarget = end;
                    laser = true; laserFrom = ray.origin; laserTo = end;
                    if (marker._currentTarget != _lastTarget) Haptics.Tick(hand.Hand, "hover");
                }
                _lastTarget = showing ? marker._currentTarget : null;
            }
            else _lastTarget = null;
        }
        catch (Exception e)
        {
            _nextSchemeCheck = 0f;
            CorePlugin.LogThrottled("pointer", "Controller pointer temporarily unavailable: " + e.Message);
        }
        SetLaser(laser, laserFrom, laserTo);
    }

    // ------------------------------------------------------------------ hand / ray

    private void ChooseHand()
    {
        if (_hand < 0 || _configuredHand != VRConfig.PointerHand.Value) { _configuredHand = VRConfig.PointerHand.Value; _hand = (int)_configuredHand; }
        // Whichever hand pulled its trigger last points.
        for (int h = 0; h < 2; h++)
        {
            var c = VRRig.Controller((VRHand)h);
            if (h != _hand && c.IsTracked && c.GetButtonDown(VRButton.Trigger)) { _hand = h; return; }
        }
        if (!Hand.IsTracked && VRRig.Controller((VRHand)(1 - _hand)).IsTracked) _hand = 1 - _hand;
    }
    private VRHand _configuredHand;

    private static bool ButtonsDown(VRController hand) =>
        ButtonGate.Down(hand, VRButton.Trigger) || ButtonGate.Down(VRRig.RightController, VRButton.Primary);

    private void UpdateRayCamera(VRController hand, bool tracked, Transform head)
    {
        var mode = VRConfig.InteractionRay.Value;
        Camera cam = null;
        if (mode == VRInteractionRay.Controller && tracked)
        {
            _handRayCam ??= NarrowCamera("NIVR Hand Ray Camera");
            if (_handRayCam.transform.parent != hand.Aim) _handRayCam.transform.SetParent(hand.Aim, false);
            cam = _handRayCam;
        }
        else if (mode != VRInteractionRay.Game)
        {
            _headRayCam ??= NarrowCamera("NIVR Head Ray Camera");
            if (_headRayCam.transform.parent != head) _headRayCam.transform.SetParent(head, false);
            cam = _headRayCam;
        }
        Comfort.SetRaycastCamera(cam, cam == _handRayCam && cam != null ? ExtraReach : 0f);
    }

    private static Camera NarrowCamera(string name)
    {
        var go = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(go);
        var cam = go.AddComponent<Camera>();
        cam.enabled = false; // never renders; only a Camera for the game's raycast
        // The game builds its interaction ray from the mouse position through this camera. The mouse is anywhere
        // (the game window rarely has focus with a headset on), so the frustum is made so narrow that every screen
        // position is the transform's forward direction.
        cam.fieldOfView = 0.1f;
        cam.nearClipPlane = 0.01f;
        return cam;
    }

    private _Code.Menues.OpenRoomView Marker()
    {
        if (_marker == null && Time.unscaledTime >= _nextMarkerScan)
        {
            _nextMarkerScan = Time.unscaledTime + 1f;
            foreach (var m in Resources.FindObjectsOfTypeAll<_Code.Menues.OpenRoomView>())
                if (m != null && m.gameObject.scene.IsValid()) { _marker = m; break; }
        }
        return _marker != null ? _marker : null;
    }

    // ------------------------------------------------------------------ laser

    private void SetLaser(bool on, Vector3 from, Vector3 to)
    {
        if (!on) { if (_laser != null) _laser.enabled = false; return; }
        if (_laser == null)
        {
            _laserGo = new GameObject("NIVR Laser") { layer = UiCapture.VrOnlyLayer };
            UnityEngine.Object.DontDestroyOnLoad(_laserGo);
            _laser = _laserGo.AddComponent<LineRenderer>();
            var mat = new Material(Shader.Find("UI/Default")) { renderQueue = 4005 };
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always); // over the screen panel, like a cursor
            _laser.sharedMaterial = mat;
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.shadowCastingMode = ShadowCastingMode.Off;
            _laser.receiveShadows = false;
            _laser.numCapVertices = 2;
            _laser.startWidth = 0.0035f; _laser.endWidth = 0.0015f;
            _laser.startColor = new Color(LaserColor.r, LaserColor.g, LaserColor.b, 0.05f);
            _laser.endColor = new Color(LaserColor.r, LaserColor.g, LaserColor.b, 0.85f);
        }
        float scale = VRRig.Origin != null ? VRRig.Origin.localScale.x : 1f;
        _laser.startWidth = 0.0035f * scale; _laser.endWidth = 0.0015f * scale;
        _laser.SetPosition(0, from);
        _laser.SetPosition(1, to);
        _laser.enabled = true;
    }

    // ------------------------------------------------------------------ scheme

    private void EnsureScheme(bool kbm, VirtualGamepad pad)
    {
        if (Time.unscaledTime < _nextSchemeCheck) return;
        _nextSchemeCheck = Time.unscaledTime + 0.5f;
        var ih = GameState.Input;
        var pi = ih != null ? ih._playerInput : null;
        if (pi == null) return;
        if (kbm)
        {
            EnsureDevices();
            if (pi.currentControlScheme == KbmScheme && Paired(pi, _mouse) && Paired(pi, _keyboard)) return;
            pi.SwitchCurrentControlScheme(KbmScheme, new Il2CppReferenceArray<InputDevice>(new[] { _keyboard, _mouse }));
        }
        else
        {
            if (pad.Device == null) return;
            if (pi.currentControlScheme == PadScheme && Paired(pi, pad.Device)) return;
            pi.SwitchCurrentControlScheme(PadScheme, new Il2CppReferenceArray<InputDevice>(new[] { pad.Device }));
        }
        if (VRConfig.Verbose.Value) CorePlugin.Log.LogInfo($"Control scheme -> {pi.currentControlScheme}");
    }

    private static bool Paired(UnityEngine.InputSystem.PlayerInput pi, InputDevice d)
    {
        if (d == null) return false;
        try
        {
            var devs = pi.devices;
            for (int i = 0; i < devs.Count; i++) if (devs[i] != null && devs[i].deviceId == d.deviceId) return true;
            return false;
        }
        catch (Exception) { return true; } // cannot tell: trust the scheme name
    }

    private void EnsureDevices()
    {
        if (_mouse == null)
        {
            _mouse = InputSystem.AddDevice("Mouse", "NIVR Pointer"); _mouseT = _mouse.Cast<Mouse>();
            CorePlugin.Log.LogInfo($"Virtual mouse added (device id {_mouse.deviceId}).");
        }
        if (_keyboard == null)
        {
            _keyboard = InputSystem.AddDevice("Keyboard", "NIVR Keys"); _keyboardT = _keyboard.Cast<Keyboard>();
            CorePlugin.Log.LogInfo($"Virtual keyboard added (device id {_keyboard.deviceId}).");
        }
    }

    // ------------------------------------------------------------------ virtual mouse / keyboard

    private void SendMouse(Vector2 px, bool left, bool virtualMouse = true)
    {
        if (!virtualMouse) { WarpSystemCursor(px, _haveLastPx ? px - _lastPx : Vector2.zero); _lastPx = px; _haveLastPx = true; return; }
        EnsureDevices();
        var m = _mouseT;
        Vector2 delta = _haveLastPx ? px - _lastPx : Vector2.zero;
        _lastPx = px; _haveLastPx = true;
        float scroll = 0f;
        float sy = VRRig.RightController.Stick.y;
        if (Mathf.Abs(sy) > 0.5f && Time.unscaledTime >= _nextScroll) { scroll = Mathf.Sign(sy) * 120f; _nextScroll = Time.unscaledTime + 0.15f; }
        else if (Mathf.Abs(sy) <= 0.5f) _nextScroll = 0f;

        int size = StateSize(_mouse);
        byte* buf = stackalloc byte[24 + size];
        Header(buf, _mouse, size);
        byte* s = buf + 24;
        WriteVector2(s, m.position, px);
        WriteVector2(s, m.delta, delta);
        WriteVector2(s, m.scroll, new Vector2(0f, scroll));
        if (left) WriteBit(s, m.leftButton);
        InputSystem.QueueEvent(new InputEventPtr((InputEvent*)buf));
        WarpSystemCursor(px, delta);
    }

    private void WarpSystemCursor(Vector2 px, Vector2 delta)
    {
        // Some screens (fridge hover) read the legacy Input.mousePosition, which is the real OS cursor.
        // Never while the game holds the cursor locked (door dialogs): Windows puts it back to the centre every
        // frame, so the cursor would flip between the laser and the centre each frame.
        if (VRConfig.MoveSystemCursor.Value && !UiCapture.LegacyMousePatched && Cursor.lockState != CursorLockMode.Locked)
        {
            var sys = SystemMouse();
            if (sys != null)
            {
                Vector2 now = sys.position.ReadValue();
                if (_haveWarp && (now - _lastWarp).sqrMagnitude > 4f) ForeignCursorMoves++;
                if (delta.sqrMagnitude > 0.25f || (now - px).sqrMagnitude > 4f) { sys.WarpCursorPosition(px); _lastWarp = px; _haveWarp = true; }
            }
        }
    }

    private Mouse _systemMouse;
    private Vector2 _lastWarp; private bool _haveWarp;
    public static int ForeignCursorMoves;

    private Mouse SystemMouse()
    {
        if (_systemMouse != null) return _systemMouse;
        var devs = InputSystem.devices;
        for (int i = 0; i < devs.Count; i++)
        {
            var d = devs[i];
            if (d == null || (_mouse != null && d.deviceId == _mouse.deviceId)) continue;
            var mouse = d.TryCast<Mouse>();
            if (mouse != null && d.native) { _systemMouse = mouse; break; }
        }
        return _systemMouse;
    }

    private void SendKeys()
    {
        var l = VRRig.LeftController; var r = VRRig.RightController;
        Array.Clear(_keys, 0, _keys.Length);
        if (ButtonGate.Down(r, VRButton.Secondary)) _keys[(int)Key.Q] = true;           // B: back / exit / cancel
        if (ButtonGate.Down(l, VRButton.Primary)) _keys[(int)Key.Space] = true;         // X: skip dialog line / video
        if (ButtonGate.Down(l, VRButton.Secondary)) _keys[(int)Key.F] = true;           // Y: tutorial / hint
        if (ButtonGate.Down(l, VRButton.Menu)) _keys[(int)Key.Escape] = true;           // Menu: pause
        if (ButtonGate.Down(r, VRButton.Grip)) _keys[(int)Key.LeftShift] = true;        // grip: speed up text
        Vector2 st = l.Stick;                                                    // left stick: navigate
        if (st.y > 0.6f) _keys[(int)Key.W] = true; else if (st.y < -0.6f) _keys[(int)Key.S] = true;
        if (st.x > 0.6f) _keys[(int)Key.D] = true; else if (st.x < -0.6f) _keys[(int)Key.A] = true;

        // Only on change (a state event replaces the whole keyboard state).
        bool changed = !_sentKeyboardOnce;
        var kb = _keyboardT;
        for (int k = 1; k < _keys.Length && !changed; k++) changed = _keys[k] != _sentKeys[k];
        if (!changed) return;
        Array.Copy(_keys, _sentKeys, _keys.Length);
        _sentKeyboardOnce = true;

        int size = StateSize(_keyboard);
        byte* buf = stackalloc byte[24 + size];
        Header(buf, _keyboard, size);
        for (int k = 1; k < _keys.Length; k++)
            if (_keys[k]) WriteBit(buf + 24, kb[(Key)k]);
        InputSystem.QueueEvent(new InputEventPtr((InputEvent*)buf));
    }

    private readonly bool[] _sentKeys = new bool[128];

    /// <summary>Lets go of every button / key (leaving pointer mode).</summary>
    public void ReleaseAll()
    {
        try
        {
            if (_mouse != null && _haveLastPx)
            {
                int size = StateSize(_mouse);
                byte* buf = stackalloc byte[24 + size];
                Header(buf, _mouse, size);
                WriteVector2(buf + 24, _mouseT.position, _lastPx);
                InputSystem.QueueEvent(new InputEventPtr((InputEvent*)buf));
            }
            if (_keyboard != null && _sentKeyboardOnce)
            {
                int size = StateSize(_keyboard);
                byte* buf = stackalloc byte[24 + size];
                Header(buf, _keyboard, size);
                InputSystem.QueueEvent(new InputEventPtr((InputEvent*)buf));
                Array.Clear(_sentKeys, 0, _sentKeys.Length);
            }
        }
        catch (Exception) { }
    }

    public void Restore()
    {
        ReleaseAll();
        _lastMode = (VRInputMode)(-1);
        SetLaser(false, default, default);
        Comfort.SetRaycastCamera(null);
        CrosshairTarget = null; PanelPoint = null; LegacyMouse = null;
        _lastHoveredSelectable = 0; _lastTarget = null; _navOverride = false; _failed = false;
    }

    // StateEvent: InputEvent header (type, size, device id, time, event id) + state format, then the device state.
    public void Dispose()
    {
        Restore();
        if (_mouse != null) InputSystem.RemoveDevice(_mouse);
        if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
        if (_laser != null) UnityEngine.Object.Destroy(_laser.sharedMaterial);
        if (_laserGo != null) UnityEngine.Object.Destroy(_laserGo);
        if (_handRayCam != null) UnityEngine.Object.Destroy(_handRayCam.gameObject);
        if (_headRayCam != null) UnityEngine.Object.Destroy(_headRayCam.gameObject);
    }
    private static void Header(byte* buf, InputDevice dev, int stateSize)
    {
        for (int i = 0; i < 24 + stateSize; i++) buf[i] = 0;
        *(int*)buf = ('S' << 24) | ('T' << 16) | ('A' << 8) | 'T';
        *(ushort*)(buf + 4) = (ushort)(24 + stateSize);
        *(ushort*)(buf + 6) = (ushort)dev.deviceId;
        *(double*)(buf + 8) = InputState.currentTime;
        *(int*)(buf + 20) = (int)dev.stateBlock.format;
    }

    private static int StateSize(InputDevice d) => (int)((d.stateBlock.sizeInBits + 7) / 8);

    private static int Offset(InputControl c) => (int)(c.stateBlock.byteOffset - c.device.stateBlock.byteOffset);

    private static void WriteVector2(byte* state, Vector2Control c, Vector2 v)
    {
        *(float*)(state + Offset(c.x)) = v.x;
        *(float*)(state + Offset(c.y)) = v.y;
    }

    private static void WriteBit(byte* state, InputControl c)
    {
        int bit = (int)c.stateBlock.bitOffset;
        state[Offset(c) + bit / 8] |= (byte)(1 << (bit % 8));
    }
}
