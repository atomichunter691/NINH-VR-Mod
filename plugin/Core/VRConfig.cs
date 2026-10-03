using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace NIVR.Core;

public enum VRBackend { Auto, OpenXR, Simulator }
public enum VRTrackingMode { Seated, Standing }
public enum VRRigRotation { YawOnly, Full }
public enum VRMirrorMode { GameCamera, LeftEye }
public enum VRInteractionRay { Controller, Head, Game }
public enum VRPointerScheme { Gamepad, Mouse }
public enum VRTurnMode { Snap, Smooth, Off }

/// <summary>BepInEx config (BepInEx\config\nivr.core.cfg). Other modules may read these entries; only Core binds them.</summary>
public static class VRConfig
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<VRBackend> Backend;

    public static ConfigEntry<VRTrackingMode> TrackingMode;
    public static ConfigEntry<float> SeatedHeightOffset;
    public static ConfigEntry<float> StandingEyeHeight;
    public static ConfigEntry<float> WorldScale;
    public static ConfigEntry<float> IpdOverrideMm;
    public static ConfigEntry<VRRigRotation> RigRotation;
    public static ConfigEntry<Key> RecenterKey;
    public static ConfigEntry<bool> RecenterWithThumbsticks;
    public static ConfigEntry<float> SnapTurnDegrees;

    public static ConfigEntry<float> RenderScale;
    public static ConfigEntry<int> Msaa;
    public static ConfigEntry<float> NearClip;
    public static ConfigEntry<float> FarClip;
    public static ConfigEntry<bool> FlipY;
    public static ConfigEntry<VRMirrorMode> Mirror;
    public static ConfigEntry<VRFlatScreenMode> FlatScreen;
    public static ConfigEntry<float> FlatScreenDistance;
    public static ConfigEntry<float> FlatScreenWidth;
    public static ConfigEntry<float> RoomDistance;
    public static ConfigEntry<bool> ControllerAsGamepad;
    public static ConfigEntry<float> WindowDistance;
    public static ConfigEntry<bool> RoomEnclosure;
    public static ConfigEntry<bool> WindowDome;
    public static ConfigEntry<float> WindowViewDegrees;
    public static ConfigEntry<float> RoomViewDegrees;
    public static ConfigEntry<bool> BodyFollowsHead;
    public static ConfigEntry<VRInteractionRay> InteractionRay;
    public static ConfigEntry<VRHand> PointerHand;
    public static ConfigEntry<bool> MoveSystemCursor;
    public static ConfigEntry<VRPointerScheme> PointerScheme;
    public static ConfigEntry<VRHand> PeepholeEye;
    public static ConfigEntry<float> PeepholeFov;

    public static ConfigEntry<bool> DisableMouseLook;
    public static ConfigEntry<bool> DisableHeadBob;
    public static ConfigEntry<bool> DisableCameraShake;
    public static ConfigEntry<bool> DisableVignette;
    public static ConfigEntry<bool> DisableLensDistortion;
    public static ConfigEntry<bool> DisableDepthOfField;
    public static ConfigEntry<bool> DisableMotionBlur;
    public static ConfigEntry<bool> DisableChromaticAberration;
    public static ConfigEntry<bool> DisableFilmGrain;

    public static ConfigEntry<string> DebugCommandDir;
    public static ConfigEntry<bool> Verbose;
    public static ConfigEntry<VRTurnMode> TurnMode;
    public static ConfigEntry<float> SmoothTurnSpeed, FadeSeconds, JumpDistance, JumpAngle, HeadCollisionRadius;
    public static ConfigEntry<bool> ComfortFade, HeadCollisionFade, MotionVignette, ControllerVisuals, PauseOnFocusLoss, RecenterWithRightMenu, VrSettings;
    public static ConfigEntry<float> VibrationStrength, RadioDetentDegrees, RightMenuHoldSeconds;
    public static ConfigEntry<bool> ShowControlsCard, ControlsCardSeen, WarnLowUiResolution, LazyFollowHud, PhysicalCrouch;
    public static ConfigEntry<float> HudFollowAngle, SubtitleOffset, CrouchDrop;
    internal static ConfigFile File;

    internal static void Bind(ConfigFile c)
    {
        File = c;
        Enabled = c.Bind("General", "Enabled", true, "Master switch. false = the game runs flat, untouched.");
        Backend = c.Bind("General", "Backend", VRBackend.Auto,
            "Auto/OpenXR: use the active OpenXR runtime (SteamVR etc.); the game stays flat until a headset is found. " +
            "Simulator: no headset, renders two fake eyes for development.");

        TrackingMode = c.Bind("Tracking", "Mode", VRTrackingMode.Seated,
            "Seated: your head position at recenter becomes the game's eye point. Standing: your real floor is the game floor.");
        SeatedHeightOffset = c.Bind("Tracking", "SeatedHeightOffset", 0f, "Seated mode: metres added to the eye height (negative = lower).");
        StandingEyeHeight = c.Bind("Tracking", "StandingEyeHeight", 1.65f, "Standing mode: the game's eye height above its floor, in metres (the game uses 1.65).");
        WorldScale = c.Bind("Tracking", "WorldScale", 1f, "1 = one game unit per metre. Larger values make the world look smaller (you move/see as a bigger person).");
        IpdOverrideMm = c.Bind("Tracking", "IpdOverrideMm", 0f, "Eye separation in millimetres. 0 = use the headset's own IPD.");
        RigRotation = c.Bind("Tracking", "RigRotation", VRRigRotation.YawOnly,
            "How scripted camera moves (looking at a door etc.) turn the VR rig. YawOnly keeps the horizon level; Full also applies pitch/roll.");
        RecenterKey = c.Bind("Tracking", "RecenterKey", Key.F8, "Keyboard key that recenters the view.");
        RecenterWithThumbsticks = c.Bind("Tracking", "RecenterWithThumbsticks", true, "Hold both thumbsticks pressed for half a second to recenter.");
        SnapTurnDegrees = c.Bind("Tracking", "SnapTurnDegrees", 30f, "Right thumbstick left/right turns the player by this many degrees. 0 = off.");
        TurnMode = c.Bind("Tracking", "TurnMode", VRTurnMode.Snap, "Right stick turning while walking: Snap, Smooth or Off.");
        SmoothTurnSpeed = Range(c, "Tracking", "SmoothTurnSpeed", 60f, 15f, 180f, "Smooth turning speed in degrees per second.");
        RecenterWithRightMenu = c.Bind("Tracking", "RecenterWithRightMenu", true, "Hold the right Menu button to recenter, if exposed by the runtime. Quest reserves its system button; use both stick clicks there.");
        RightMenuHoldSeconds = Range(c, "Tracking", "RightMenuHoldSeconds", 1f, 0.5f, 3f, "Right Menu hold duration before recentering.");
        PhysicalCrouch = c.Bind("Tracking", "PhysicalCrouch", false, "Crouch when your real head drops below its recentered height. Respects the game's crouch zones.");
        CrouchDrop = Range(c, "Tracking", "CrouchDrop", 0.35f, 0.15f, 0.8f, "Head drop in metres required for physical crouch; releases 8 cm above this threshold.");

        RenderScale = c.Bind("Rendering", "RenderScale", 1f, "Multiplier on the headset's recommended eye resolution.");
        Msaa = c.Bind("Rendering", "MSAA", 4, "MSAA samples for the eye textures (1, 2, 4 or 8).");
        NearClip = c.Bind("Rendering", "NearClip", 0.05f, "Near clip plane in metres.");
        FarClip = c.Bind("Rendering", "FarClip", 0f, "Far clip plane. 0 = use the game camera's.");
        FlipY = c.Bind("Rendering", "FlipY", true, "Flip the image vertically when handing it to the headset. Change only if the headset image is upside down.");
        Mirror = c.Bind("Rendering", "Mirror", VRMirrorMode.GameCamera,
            "Desktop window. GameCamera: the normal flat game view, following your head (menus stay clickable with the mouse). " +
            "LeftEye: the left eye image is drawn over it.");
        FlatScreen = c.Bind("Rendering", "FlatScreen", VRFlatScreenMode.Auto,
            "The game's screen UI in VR (HUD while walking, menus, dialogs, room pictures). Auto / Always: shown. Off: not shown (for a module that draws the UI itself).");
        FlatScreenDistance = c.Bind("Rendering", "UiDistance", 1.5f, "Distance of the UI layer in metres.");
        FlatScreenWidth = c.Bind("Rendering", "UiWidth", 2.1f, "Width of the UI layer in metres (2.1 m at 1.5 m is about 70 degrees).");
        RoomDistance = c.Bind("Rendering", "RoomDistance", 3f, "How far away the room picture behind an opened door is drawn, in metres.");
        BodyFollowsHead = c.Bind("Tracking", "BodyFollowsHead", true,
            "While walking, the game's player turns to where your head points, so the crosshair, interaction and walking direction follow your gaze.");
        InteractionRay = c.Bind("Input", "InteractionRay", VRInteractionRay.Controller,
            "Where the game's interaction ray (door / object targeting, crosshair) comes from in the hallway. Controller: the pointing controller (laser shown while something can be used). " +
            "Head: straight out of the headset. Game: the game's own ray through the mouse position (only works while the game window has focus).");
        MoveSystemCursor = c.Bind("Input", "MoveSystemCursor", true,
            "In menus / rooms the real Windows mouse cursor follows the controller pointer (some game screens read it directly). Turn off if it gets in the way on the desktop.");
        PointerScheme = c.Bind("Input", "PointerScheme", VRPointerScheme.Gamepad,
            "Menus, rooms, phone, fridge. Gamepad: the game stays in controller mode (A/B/X/Y prompts, stick/d-pad menu navigation, B always backs out) and the laser moves the game's controller cursor. " +
            "Mouse: the game's keyboard & mouse mode, the laser is the mouse (keyboard prompts).");
        PointerHand = c.Bind("Input", "PointerHand", VRHand.Right, "Controller that points at start; pulling the other controller's trigger switches hands.");
        RoomEnclosure = c.Bind("Rendering", "RoomEnclosure", true,
            "Room pictures become the front wall of a closed room around you (walls, floor and ceiling continue the picture's edges). false = just the picture seen through the doorway.");
        RoomViewDegrees = c.Bind("Rendering", "RoomViewDegrees", 105f, "How wide the room picture is in your view, in degrees, when RoomEnclosure is on.");
        WindowDistance = c.Bind("Rendering", "WindowDistance", 10f, "How far away the view outside a window is drawn, in metres.");
        WindowDome = c.Bind("Rendering", "WindowDome", true,
            "The view outside a window is drawn on a curved surface around you and continues (mirrored) past the picture's borders, so no angle through the window shows anything else. false = a flat picture.");
        WindowViewDegrees = c.Bind("Rendering", "WindowViewDegrees", 110f, "How wide the window picture itself is on that surface, in degrees. Larger = bigger picture, more curvature.");
        PeepholeEye = c.Bind("Rendering", "PeepholeEye", VRHand.Right, "Which eye looks through the door peephole; the other eye is black.");
        PeepholeFov = c.Bind("Rendering", "PeepholeDegrees", 60f, "Width of the peephole picture in degrees (it stays still in front of you; the whole picture should fit in view).");
        ControllerAsGamepad = c.Bind("Input", "ControllerAsGamepad", true,
            "Feed the VR controllers to the game as a gamepad (left stick walks, A interacts, right stick moves the menu cursor).");
        ControllerVisuals = c.Bind("Rendering", "ControllerVisuals", true, "Show procedural teal controller markers at the tracked grip poses (hidden in peephole).");
        VibrationStrength = Range(c, "Input", "VibrationStrength", 1f, 0f, 2f, "Multiplier for VR hover, submit, opening and radio-detent pulses; 0 disables them.");
        RadioDetentDegrees = Range(c, "Input", "RadioDetentDegrees", 12f, 3f, 45f, "Controller twist in degrees between radio-knob vibration detents.");
        VrSettings = c.Bind("General", "VrSettings", true, "Add a VR section to the game's Settings page. Changes apply live and save to nivr.core.cfg.");
        PauseOnFocusLoss = c.Bind("General", "PauseOnFocusLoss", true, "Open the game's pause menu when headset focus is lost. Resume manually after returning.");
        ShowControlsCard = c.Bind("General", "ShowControlsCard", true, "Show a short controls card the first time VR becomes active; dismiss with any controller button.");
        ControlsCardSeen = c.Bind("General", "ControlsCardSeen", false, "Controls card has been dismissed. Set false to show it again.");
        WarnLowUiResolution = c.Bind("Rendering", "WarnLowUiResolution", true, "Log once per window size below 1920x1080; window resolution determines UI sharpness.");
        LazyFollowHud = c.Bind("Rendering", "LazyFollowHud", false, "Let walking HUD text catch up only when you turn beyond HudFollowAngle. World-projected interaction markers retain their alignment.");
        HudFollowAngle = Range(c, "Rendering", "HudFollowAngle", 20f, 5f, 45f, "Angle in degrees before the walking HUD starts catching up.");
        SubtitleOffset = Range(c, "Rendering", "SubtitleOffset", 0f, 0f, 250f, "Lower subtitle panels by this many canvas units without shifting interaction markers. Clamped to keep the whole caption above the window's bottom edge.");
        ComfortFade = c.Bind("Comfort", "ComfortFade", true, "Briefly fade camera jumps, scripted look transitions and scene loads to black in both eyes.");
        FadeSeconds = Range(c, "Comfort", "FadeSeconds", 0.22f, 0.15f, 0.3f, "Blackout and reveal duration for camera transitions in seconds.");
        JumpDistance = Range(c, "Comfort", "JumpDistance", 0.45f, 0.1f, 2f, "Base camera movement per frame in metres that triggers a comfort fade.");
        JumpAngle = Range(c, "Comfort", "JumpAngle", 35f, 10f, 120f, "Unexpected base camera rotation per frame in degrees that triggers a fade. Head motion and deliberate stick turns are excluded.");
        HeadCollisionFade = c.Bind("Comfort", "HeadCollisionFade", true, "Black out while the tracked head overlaps solid Default-layer geometry; ignores triggers and the player's collider.");
        HeadCollisionRadius = Range(c, "Comfort", "HeadCollisionRadius", 0.08f, 0.02f, 0.2f, "Head collision sphere radius in metres.");
        MotionVignette = c.Bind("Comfort", "MotionVignette", false, "Optional peripheral black vignette during smooth turns and fast stick walking.");

        DisableMouseLook = c.Bind("Comfort", "DisableMouseLook", true, "Stop mouse / right stick from rotating the camera while VR is active.");
        DisableHeadBob = c.Bind("Comfort", "DisableHeadBob", true, "Remove the walking head bob.");
        DisableCameraShake = c.Bind("Comfort", "DisableCameraShake", true, "Remove camera noise/shake on every Cinemachine camera.");
        DisableVignette = c.Bind("Comfort", "DisableVignette", true, "Turn off the vignette post effect (it is drawn per eye and looks wrong in stereo).");
        DisableLensDistortion = c.Bind("Comfort", "DisableLensDistortion", true, "Turn off lens distortion (peephole fisheye etc.).");
        DisableDepthOfField = c.Bind("Comfort", "DisableDepthOfField", true, "Turn off depth of field.");
        DisableMotionBlur = c.Bind("Comfort", "DisableMotionBlur", true, "Turn off motion blur.");
        DisableChromaticAberration = c.Bind("Comfort", "DisableChromaticAberration", true, "Turn off chromatic aberration.");
        DisableFilmGrain = c.Bind("Comfort", "DisableFilmGrain", false, "Turn off film grain.");

        DebugCommandDir = c.Bind("Debug", "CommandDir", "", "Development only: folder polled for *.vr command files (eyes, recenter, status, simhead, turn). Empty = off.");
        Verbose = c.Bind("Debug", "Verbose", false, "Extra logging.");
    }

    private static ConfigEntry<float> Range(ConfigFile c, string section, string key, float value, float min, float max, string description)
        => c.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
}
