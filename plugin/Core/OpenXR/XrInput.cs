using System;
using System.Collections.Generic;

namespace NIVR.Core.OpenXR;

/// <summary>Raw per-hand controller state read from OpenXR each frame.</summary>
internal struct HandState
{
    public bool active;         // a controller is bound and reporting
    public float trigger, grip;
    public float stickX, stickY;
    public bool stickClick, primary, secondary, menu;
    public bool gripValid, aimValid;
    public XrPosef gripPose, aimPose; // in the tracking space passed to Read
    public XrVector3f gripLinear, gripAngular, aimLinear, aimAngular; // m/s and rad/s in that space
    public ulong gripVelFlags, aimVelFlags; // Xr.VelLinearValid | Xr.VelAngularValid, 0 when the runtime gave none
}

/// <summary>One action set with the usual controller actions, bound for the common interaction profiles.</summary>
internal sealed unsafe class XrInput
{
    private ulong _instance, _set;
    private readonly ulong[] _hand = new ulong[2];
    private ulong _trigger, _grip, _stick, _stickClick, _primary, _secondary, _menu, _gripPose, _aimPose, _haptic;
    private readonly ulong[] _gripSpace = new ulong[2], _aimSpace = new ulong[2];
    private bool _attached;

    public void Create(ulong instance)
    {
        _instance = instance;
        _hand[0] = Xr.Path_(instance, "/user/hand/left");
        _hand[1] = Xr.Path_(instance, "/user/hand/right");

        var sci = new XrActionSetCreateInfo { type = XrStructureType.ActionSetCreateInfo };
        Xr.WriteString(sci.actionSetName, 64, "nivr");
        Xr.WriteString(sci.localizedActionSetName, 128, "NIVR");
        ulong set;
        int r = Xr.xrCreateActionSet(instance, &sci, &set);
        if (r < 0) throw new InvalidOperationException("xrCreateActionSet: " + Xr.ResultName(instance, r));
        _set = set;

        _trigger = Action("trigger", "Trigger", XrActionType.FloatInput);
        _grip = Action("grip", "Grip", XrActionType.FloatInput);
        _stick = Action("stick", "Thumbstick", XrActionType.Vector2fInput);
        _stickClick = Action("stick_click", "Thumbstick click", XrActionType.BooleanInput);
        _primary = Action("primary", "Primary button (A/X)", XrActionType.BooleanInput);
        _secondary = Action("secondary", "Secondary button (B/Y)", XrActionType.BooleanInput);
        _menu = Action("menu", "Menu button", XrActionType.BooleanInput);
        _gripPose = Action("grip_pose", "Grip pose", XrActionType.PoseInput);
        _aimPose = Action("aim_pose", "Aim pose", XrActionType.PoseInput);
        _haptic = Action("haptic", "Haptic", XrActionType.VibrationOutput);

        // L:/R: prefixes restrict a binding to one hand; no prefix = both hands.
        Suggest("/interaction_profiles/oculus/touch_controller",
            (_trigger, "/input/trigger/value"), (_grip, "/input/squeeze/value"), (_stick, "/input/thumbstick"),
            (_stickClick, "/input/thumbstick/click"), (_primary, "L:/input/x/click"), (_primary, "R:/input/a/click"),
            (_secondary, "L:/input/y/click"), (_secondary, "R:/input/b/click"), (_menu, "L:/input/menu/click"),
            (_gripPose, "/input/grip/pose"), (_aimPose, "/input/aim/pose"), (_haptic, "/output/haptic"));
        Suggest("/interaction_profiles/valve/index_controller",
            (_trigger, "/input/trigger/value"), (_grip, "/input/squeeze/value"), (_stick, "/input/thumbstick"),
            (_stickClick, "/input/thumbstick/click"), (_primary, "/input/a/click"), (_secondary, "/input/b/click"),
            (_gripPose, "/input/grip/pose"), (_aimPose, "/input/aim/pose"), (_haptic, "/output/haptic"));
        Suggest("/interaction_profiles/htc/vive_controller",
            (_trigger, "/input/trigger/value"), (_grip, "/input/squeeze/click"), (_stick, "/input/trackpad"),
            (_stickClick, "/input/trackpad/click"), (_menu, "/input/menu/click"),
            (_gripPose, "/input/grip/pose"), (_aimPose, "/input/aim/pose"), (_haptic, "/output/haptic"));
        Suggest("/interaction_profiles/microsoft/motion_controller",
            (_trigger, "/input/trigger/value"), (_grip, "/input/squeeze/click"), (_stick, "/input/thumbstick"),
            (_stickClick, "/input/thumbstick/click"), (_primary, "/input/trackpad/click"), (_menu, "/input/menu/click"),
            (_gripPose, "/input/grip/pose"), (_aimPose, "/input/aim/pose"), (_haptic, "/output/haptic"));
        Suggest("/interaction_profiles/khr/simple_controller",
            (_trigger, "/input/select/click"), (_menu, "/input/menu/click"),
            (_gripPose, "/input/grip/pose"), (_aimPose, "/input/aim/pose"), (_haptic, "/output/haptic"));
    }

    private ulong Action(string name, string localized, XrActionType type)
    {
        var ci = new XrActionCreateInfo { type = XrStructureType.ActionCreateInfo, actionType = type, countSubactionPaths = 2 };
        Xr.WriteString(ci.actionName, 64, name);
        Xr.WriteString(ci.localizedActionName, 128, localized);
        ulong action;
        fixed (ulong* hands = _hand)
        {
            ci.subactionPaths = hands;
            int r = Xr.xrCreateAction(_set, &ci, &action);
            if (r < 0) throw new InvalidOperationException($"xrCreateAction({name}): " + Xr.ResultName(_instance, r));
        }
        return action;
    }

    private void Suggest(string profile, params (ulong action, string path)[] bindings)
    {
        var list = new List<XrActionSuggestedBinding>();
        foreach (var (action, path) in bindings)
        {
            bool left = !path.StartsWith("R:"), right = !path.StartsWith("L:");
            string component = path.StartsWith("L:") || path.StartsWith("R:") ? path.Substring(2) : path;
            if (left) list.Add(new XrActionSuggestedBinding { action = action, binding = Xr.Path_(_instance, "/user/hand/left" + component) });
            if (right) list.Add(new XrActionSuggestedBinding { action = action, binding = Xr.Path_(_instance, "/user/hand/right" + component) });
        }
        var arr = list.ToArray();
        fixed (XrActionSuggestedBinding* p = arr)
        {
            var sb = new XrInteractionProfileSuggestedBinding
            {
                type = XrStructureType.InteractionProfileSuggestedBinding,
                interactionProfile = Xr.Path_(_instance, profile),
                countSuggestedBindings = (uint)arr.Length,
                suggestedBindings = p,
            };
            int r = Xr.xrSuggestInteractionProfileBindings(_instance, &sb);
            if (r < 0) CorePlugin.Log.LogWarning($"OpenXR bindings for {profile} rejected: {Xr.ResultName(_instance, r)}");
        }
    }

    public void Attach(ulong session)
    {
        _attached = false;
        ulong set = _set;
        var ai = new XrSessionActionSetsAttachInfo { type = XrStructureType.SessionActionSetsAttachInfo, countActionSets = 1, actionSets = &set };
        int r = Xr.xrAttachSessionActionSets(session, &ai);
        if (r < 0) { CorePlugin.Log.LogError("xrAttachSessionActionSets: " + Xr.ResultName(_instance, r)); return; }
        for (int h = 0; h < 2; h++)
        {
            _gripSpace[h] = Space(session, _gripPose, h);
            _aimSpace[h] = Space(session, _aimPose, h);
        }
        _attached = true;
    }

    private ulong Space(ulong session, ulong action, int hand)
    {
        var ci = new XrActionSpaceCreateInfo { type = XrStructureType.ActionSpaceCreateInfo, action = action, subactionPath = _hand[hand], poseInActionSpace = Xr.IdentityPose };
        ulong space;
        int r = Xr.xrCreateActionSpace(session, &ci, &space);
        if (r < 0) { CorePlugin.Log.LogError("xrCreateActionSpace: " + Xr.ResultName(_instance, r)); return 0; }
        return space;
    }

    /// <summary>Syncs actions and reads both hands. Returns false (all inactive) while the session is not focused.</summary>
    public bool Read(XrSession xr, ulong trackingSpace, long time, HandState[] hands)
    {
        hands[0] = default; hands[1] = default;
        if (!_attached || xr.Session == 0) return false;
        var active = new XrActiveActionSet { actionSet = _set };
        var si = new XrActionsSyncInfo { type = XrStructureType.ActionsSyncInfo, countActiveActionSets = 1, activeActionSets = &active };
        int r = Xr.xrSyncActions(xr.Session, &si);
        if (r != Xr.Success) return false; // XR_SESSION_NOT_FOCUSED or an error

        for (int h = 0; h < 2; h++)
        {
            ref HandState s = ref hands[h];
            s.trigger = Float(xr.Session, _trigger, h, ref s.active);
            s.grip = Float(xr.Session, _grip, h, ref s.active);
            s.stickClick = Bool(xr.Session, _stickClick, h);
            s.primary = Bool(xr.Session, _primary, h);
            s.secondary = Bool(xr.Session, _secondary, h);
            s.menu = Bool(xr.Session, _menu, h);

            var gi = new XrActionStateGetInfo { type = XrStructureType.ActionStateGetInfo, action = _stick, subactionPath = _hand[h] };
            var v = new XrActionStateVector2f { type = XrStructureType.ActionStateVector2f };
            if (Xr.xrGetActionStateVector2f(xr.Session, &gi, &v) >= 0 && v.isActive != 0) { s.stickX = v.currentState.x; s.stickY = v.currentState.y; }

            if (_gripSpace[h] != 0) s.gripValid = xr.LocateSpace(_gripSpace[h], trackingSpace, time, out s.gripPose, out _, out s.gripLinear, out s.gripAngular, out s.gripVelFlags);
            if (_aimSpace[h] != 0) s.aimValid = xr.LocateSpace(_aimSpace[h], trackingSpace, time, out s.aimPose, out _, out s.aimLinear, out s.aimAngular, out s.aimVelFlags);
            s.active |= s.gripValid || s.aimValid;
        }
        return true;
    }

    private float Float(ulong session, ulong action, int hand, ref bool active)
    {
        var gi = new XrActionStateGetInfo { type = XrStructureType.ActionStateGetInfo, action = action, subactionPath = _hand[hand] };
        var st = new XrActionStateFloat { type = XrStructureType.ActionStateFloat };
        if (Xr.xrGetActionStateFloat(session, &gi, &st) < 0 || st.isActive == 0) return 0f;
        active = true;
        return st.currentState;
    }

    private bool Bool(ulong session, ulong action, int hand)
    {
        var gi = new XrActionStateGetInfo { type = XrStructureType.ActionStateGetInfo, action = action, subactionPath = _hand[hand] };
        var st = new XrActionStateBoolean { type = XrStructureType.ActionStateBoolean };
        return Xr.xrGetActionStateBoolean(session, &gi, &st) >= 0 && st.isActive != 0 && st.currentState != 0;
    }

    public void Haptic(ulong session, int hand, float amplitude, float seconds, float frequency)
    {
        if (!_attached || session == 0) return;
        var hi = new XrHapticActionInfo { type = XrStructureType.HapticActionInfo, action = _haptic, subactionPath = _hand[hand] };
        var vib = new XrHapticVibration
        {
            type = XrStructureType.HapticVibration,
            duration = seconds <= 0 ? -1 : (long)(seconds * 1e9), // -1 = XR_MIN_HAPTIC_DURATION
            frequency = frequency, amplitude = Math.Clamp(amplitude, 0f, 1f),
        };
        int r = Xr.xrApplyHapticFeedback(session, &hi, &vib);
        if (r < 0) CorePlugin.LogThrottled("haptic", "xrApplyHapticFeedback: " + Xr.ResultName(_instance, r));
    }
}
