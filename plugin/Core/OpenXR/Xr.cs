using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace NIVR.Core.OpenXR;

// Minimal hand-written OpenXR 1.0 bindings (only what the core uses). 64-bit Windows only.

internal enum XrStructureType
{
    InstanceCreateInfo = 3,
    SystemGetInfo = 4,
    SystemProperties = 5,
    ViewLocateInfo = 6,
    View = 7,
    SessionCreateInfo = 8,
    SpaceVelocity = 12,
    SwapchainCreateInfo = 9,
    SessionBeginInfo = 10,
    ViewState = 11,
    FrameEndInfo = 12,
    HapticVibration = 13,
    EventDataBuffer = 16,
    EventDataInstanceLossPending = 17,
    EventDataSessionStateChanged = 18,
    ActionStateBoolean = 23,
    ActionStateFloat = 24,
    ActionStateVector2f = 25,
    ActionStatePose = 27,
    ActionSetCreateInfo = 28,
    ActionCreateInfo = 29,
    InstanceProperties = 32,
    FrameWaitInfo = 33,
    CompositionLayerProjection = 35,
    ReferenceSpaceCreateInfo = 37,
    ActionSpaceCreateInfo = 38,
    EventDataReferenceSpaceChangePending = 40,
    ViewConfigurationView = 41,
    SpaceLocation = 42,
    FrameState = 44,
    FrameBeginInfo = 46,
    CompositionLayerProjectionView = 48,
    EventDataEventsLost = 49,
    InteractionProfileSuggestedBinding = 51,
    EventDataInteractionProfileChanged = 52,
    InteractionProfileState = 53,
    SwapchainImageAcquireInfo = 55,
    SwapchainImageWaitInfo = 56,
    SwapchainImageReleaseInfo = 57,
    ActionStateGetInfo = 58,
    HapticActionInfo = 59,
    SessionActionSetsAttachInfo = 60,
    ActionsSyncInfo = 61,
    GraphicsBindingD3D11 = 1000027000,
    SwapchainImageD3D11 = 1000027001,
    GraphicsRequirementsD3D11 = 1000027002,
}

internal enum XrSessionState
{
    Unknown = 0, Idle = 1, Ready = 2, Synchronized = 3, Visible = 4, Focused = 5, Stopping = 6, LossPending = 7, Exiting = 8,
}

internal enum XrActionType { BooleanInput = 1, FloatInput = 2, Vector2fInput = 3, PoseInput = 4, VibrationOutput = 100 }

[StructLayout(LayoutKind.Sequential)] internal struct XrVector2f { public float x, y; }
[StructLayout(LayoutKind.Sequential)] internal struct XrVector3f { public float x, y, z; }
[StructLayout(LayoutKind.Sequential)] internal struct XrQuaternionf { public float x, y, z, w; }
[StructLayout(LayoutKind.Sequential)] internal struct XrPosef { public XrQuaternionf orientation; public XrVector3f position; }
[StructLayout(LayoutKind.Sequential)] internal struct XrFovf { public float angleLeft, angleRight, angleUp, angleDown; }
[StructLayout(LayoutKind.Sequential)] internal struct XrRect2Di { public int x, y, width, height; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrApplicationInfo
{
    public fixed byte applicationName[128];
    public uint applicationVersion;
    public fixed byte engineName[128];
    public uint engineVersion;
    public ulong apiVersion;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrInstanceCreateInfo
{
    public XrStructureType type; public void* next;
    public ulong createFlags;
    public XrApplicationInfo applicationInfo;
    public uint enabledApiLayerCount; public byte** enabledApiLayerNames;
    public uint enabledExtensionCount; public byte** enabledExtensionNames;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrInstanceProperties
{
    public XrStructureType type; public void* next;
    public ulong runtimeVersion;
    public fixed byte runtimeName[128];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSystemGetInfo { public XrStructureType type; public void* next; public int formFactor; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSystemProperties
{
    public XrStructureType type; public void* next;
    public ulong systemId; public uint vendorId;
    public fixed byte systemName[256];
    public uint maxSwapchainImageHeight, maxSwapchainImageWidth, maxLayerCount;
    public uint orientationTracking, positionTracking;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrViewConfigurationView
{
    public XrStructureType type; public void* next;
    public uint recommendedImageRectWidth, maxImageRectWidth, recommendedImageRectHeight, maxImageRectHeight;
    public uint recommendedSwapchainSampleCount, maxSwapchainSampleCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrGraphicsRequirementsD3D11
{
    public XrStructureType type; public void* next;
    public long adapterLuid; public int minFeatureLevel;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrGraphicsBindingD3D11 { public XrStructureType type; public void* next; public IntPtr device; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSessionCreateInfo { public XrStructureType type; public void* next; public ulong createFlags; public ulong systemId; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrReferenceSpaceCreateInfo
{
    public XrStructureType type; public void* next;
    public int referenceSpaceType; public XrPosef poseInReferenceSpace;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSwapchainCreateInfo
{
    public XrStructureType type; public void* next;
    public ulong createFlags, usageFlags; public long format;
    public uint sampleCount, width, height, faceCount, arraySize, mipCount;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSwapchainImageD3D11 { public XrStructureType type; public void* next; public IntPtr texture; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSwapchainImageWaitInfo { public XrStructureType type; public void* next; public long timeout; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSessionBeginInfo { public XrStructureType type; public void* next; public int primaryViewConfigurationType; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrFrameState
{
    public XrStructureType type; public void* next;
    public long predictedDisplayTime, predictedDisplayPeriod; public uint shouldRender;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrViewLocateInfo
{
    public XrStructureType type; public void* next;
    public int viewConfigurationType; public long displayTime; public ulong space;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrViewState { public XrStructureType type; public void* next; public ulong viewStateFlags; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrView { public XrStructureType type; public void* next; public XrPosef pose; public XrFovf fov; }

[StructLayout(LayoutKind.Sequential)]
internal struct XrSwapchainSubImage { public ulong swapchain; public XrRect2Di imageRect; public uint imageArrayIndex; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrCompositionLayerProjectionView
{
    public XrStructureType type; public void* next;
    public XrPosef pose; public XrFovf fov; public XrSwapchainSubImage subImage;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrCompositionLayerProjection
{
    public XrStructureType type; public void* next;
    public ulong layerFlags; public ulong space;
    public uint viewCount; public XrCompositionLayerProjectionView* views;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrFrameEndInfo
{
    public XrStructureType type; public void* next;
    public long displayTime; public int environmentBlendMode;
    public uint layerCount; public void** layers;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrEventDataBuffer { public XrStructureType type; public void* next; public fixed byte varying[4000]; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrEventDataSessionStateChanged
{
    public XrStructureType type; public void* next;
    public ulong session; public XrSessionState state; public long time;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSpaceLocation { public XrStructureType type; public void* next; public ulong locationFlags; public XrPosef pose; }

/// <summary>Chained to XrSpaceLocation.next to get velocities from xrLocateSpace (metres/s, radians/s, in the base space).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSpaceVelocity { public XrStructureType type; public void* next; public ulong velocityFlags; public XrVector3f linearVelocity; public XrVector3f angularVelocity; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrActionSetCreateInfo
{
    public XrStructureType type; public void* next;
    public fixed byte actionSetName[64];
    public fixed byte localizedActionSetName[128];
    public uint priority;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrActionCreateInfo
{
    public XrStructureType type; public void* next;
    public fixed byte actionName[64];
    public XrActionType actionType;
    public uint countSubactionPaths; public ulong* subactionPaths;
    public fixed byte localizedActionName[128];
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrActionSuggestedBinding { public ulong action; public ulong binding; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrInteractionProfileSuggestedBinding
{
    public XrStructureType type; public void* next;
    public ulong interactionProfile; public uint countSuggestedBindings; public XrActionSuggestedBinding* suggestedBindings;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrSessionActionSetsAttachInfo
{
    public XrStructureType type; public void* next; public uint countActionSets; public ulong* actionSets;
}

[StructLayout(LayoutKind.Sequential)]
internal struct XrActiveActionSet { public ulong actionSet; public ulong subactionPath; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrActionsSyncInfo
{
    public XrStructureType type; public void* next; public uint countActiveActionSets; public XrActiveActionSet* activeActionSets;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrActionStateGetInfo { public XrStructureType type; public void* next; public ulong action; public ulong subactionPath; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrActionStateBoolean
{
    public XrStructureType type; public void* next;
    public uint currentState, changedSinceLastSync; public long lastChangeTime; public uint isActive;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrActionStateFloat
{
    public XrStructureType type; public void* next;
    public float currentState; public uint changedSinceLastSync; public long lastChangeTime; public uint isActive;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrActionStateVector2f
{
    public XrStructureType type; public void* next;
    public XrVector2f currentState; public uint changedSinceLastSync; public long lastChangeTime; public uint isActive;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrActionSpaceCreateInfo
{
    public XrStructureType type; public void* next;
    public ulong action; public ulong subactionPath; public XrPosef poseInActionSpace;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrHapticActionInfo { public XrStructureType type; public void* next; public ulong action; public ulong subactionPath; }

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrHapticVibration
{
    public XrStructureType type; public void* next;
    public long duration; public float frequency; public float amplitude;
}

/// <summary>A struct that is only {type, next} (FrameWaitInfo, FrameBeginInfo, acquire/release info).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XrBaseStruct { public XrStructureType type; public void* next; }

internal static unsafe class Xr
{
    private const string Lib = "openxr_loader";

    public const int Success = 0;
    public const int SessionLossPending = 3;
    public const int EventUnavailable = 4;
    public const int SessionNotFocused = 8;
    public const int ErrorFormFactorUnavailable = -50;
    public const int ErrorRuntimeUnavailable = -51;

    public const int FormFactorHmd = 1;
    public const int ViewConfigStereo = 2;
    public const int BlendOpaque = 1;
    public const int SpaceView = 1, SpaceLocal = 2, SpaceStage = 3;
    public const ulong LocOrientationValid = 1, LocPositionValid = 2, LocOrientationTracked = 4, LocPositionTracked = 8;
    public const ulong VelLinearValid = 1, VelAngularValid = 2;
    public const ulong UsageColorAttachment = 0x1, UsageTransferDst = 0x10, UsageSampled = 0x20;
    public const long InfiniteDuration = long.MaxValue;
    public const ulong ApiVersion = 1UL << 48; // XR_MAKE_VERSION(1, 0, 0)

    public static readonly XrPosef IdentityPose = new() { orientation = new XrQuaternionf { w = 1f } };

    static Xr()
    {
        NativeLibrary.SetDllImportResolver(typeof(Xr).Assembly, Resolve);
    }

    public static string LoaderPath { get; private set; } = "(default search path)";

    private static IntPtr Resolve(string name, Assembly asm, DllImportSearchPath? search)
    {
        if (name != Lib) return IntPtr.Zero;
        var candidates = new[]
        {
            Path.Combine(Path.GetDirectoryName(asm.Location) ?? ".", "openxr_loader.dll"),
            @"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\openxr_loader.dll",
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c) && NativeLibrary.TryLoad(c, out var h)) { LoaderPath = c; return h; }
        }
        return IntPtr.Zero;
    }

    public static bool Ok(int result) => result >= 0;

    public static void WriteString(byte* dst, int capacity, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        int n = Math.Min(bytes.Length, capacity - 1);
        for (int i = 0; i < n; i++) dst[i] = bytes[i];
        dst[n] = 0;
    }

    public static string ReadString(byte* src, int capacity)
    {
        int n = 0;
        while (n < capacity && src[n] != 0) n++;
        return Encoding.UTF8.GetString(src, n);
    }

    public static string ResultName(ulong instance, int result)
    {
        if (instance != 0)
        {
            byte* buf = stackalloc byte[64];
            if (xrResultToString(instance, result, buf) >= 0) return ReadString(buf, 64);
        }
        return result.ToString();
    }

    public static ulong Path_(ulong instance, string path)
    {
        ulong p;
        int r = xrStringToPath(instance, path, &p);
        if (r < 0) throw new InvalidOperationException($"xrStringToPath({path}) failed: {ResultName(instance, r)}");
        return p;
    }

    [DllImport(Lib)] public static extern int xrCreateInstance(XrInstanceCreateInfo* createInfo, ulong* instance);
    [DllImport(Lib)] public static extern int xrDestroyInstance(ulong instance);
    [DllImport(Lib)] public static extern int xrGetInstanceProperties(ulong instance, XrInstanceProperties* props);
    [DllImport(Lib)] public static extern int xrGetInstanceProcAddr(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string name, IntPtr* function);
    [DllImport(Lib)] public static extern int xrResultToString(ulong instance, int value, byte* buffer);
    [DllImport(Lib)] public static extern int xrPollEvent(ulong instance, XrEventDataBuffer* eventData);
    [DllImport(Lib)] public static extern int xrGetSystem(ulong instance, XrSystemGetInfo* getInfo, ulong* systemId);
    [DllImport(Lib)] public static extern int xrGetSystemProperties(ulong instance, ulong systemId, XrSystemProperties* props);
    [DllImport(Lib)] public static extern int xrEnumerateViewConfigurationViews(ulong instance, ulong systemId, int viewConfigurationType, uint capacity, uint* count, XrViewConfigurationView* views);
    [DllImport(Lib)] public static extern int xrCreateSession(ulong instance, XrSessionCreateInfo* createInfo, ulong* session);
    [DllImport(Lib)] public static extern int xrDestroySession(ulong session);
    [DllImport(Lib)] public static extern int xrBeginSession(ulong session, XrSessionBeginInfo* beginInfo);
    [DllImport(Lib)] public static extern int xrEndSession(ulong session);
    [DllImport(Lib)] public static extern int xrRequestExitSession(ulong session);
    [DllImport(Lib)] public static extern int xrEnumerateReferenceSpaces(ulong session, uint capacity, uint* count, int* spaces);
    [DllImport(Lib)] public static extern int xrCreateReferenceSpace(ulong session, XrReferenceSpaceCreateInfo* createInfo, ulong* space);
    [DllImport(Lib)] public static extern int xrDestroySpace(ulong space);
    [DllImport(Lib)] public static extern int xrLocateSpace(ulong space, ulong baseSpace, long time, XrSpaceLocation* location);
    [DllImport(Lib)] public static extern int xrEnumerateSwapchainFormats(ulong session, uint capacity, uint* count, long* formats);
    [DllImport(Lib)] public static extern int xrCreateSwapchain(ulong session, XrSwapchainCreateInfo* createInfo, ulong* swapchain);
    [DllImport(Lib)] public static extern int xrDestroySwapchain(ulong swapchain);
    [DllImport(Lib)] public static extern int xrEnumerateSwapchainImages(ulong swapchain, uint capacity, uint* count, XrSwapchainImageD3D11* images);
    [DllImport(Lib)] public static extern int xrAcquireSwapchainImage(ulong swapchain, XrBaseStruct* acquireInfo, uint* index);
    [DllImport(Lib)] public static extern int xrWaitSwapchainImage(ulong swapchain, XrSwapchainImageWaitInfo* waitInfo);
    [DllImport(Lib)] public static extern int xrReleaseSwapchainImage(ulong swapchain, XrBaseStruct* releaseInfo);
    [DllImport(Lib)] public static extern int xrWaitFrame(ulong session, XrBaseStruct* waitInfo, XrFrameState* frameState);
    [DllImport(Lib)] public static extern int xrBeginFrame(ulong session, XrBaseStruct* beginInfo);
    [DllImport(Lib)] public static extern int xrEndFrame(ulong session, XrFrameEndInfo* endInfo);
    [DllImport(Lib)] public static extern int xrLocateViews(ulong session, XrViewLocateInfo* locateInfo, XrViewState* viewState, uint capacity, uint* count, XrView* views);
    [DllImport(Lib)] public static extern int xrStringToPath(ulong instance, [MarshalAs(UnmanagedType.LPStr)] string pathString, ulong* path);
    [DllImport(Lib)] public static extern int xrCreateActionSet(ulong instance, XrActionSetCreateInfo* createInfo, ulong* actionSet);
    [DllImport(Lib)] public static extern int xrCreateAction(ulong actionSet, XrActionCreateInfo* createInfo, ulong* action);
    [DllImport(Lib)] public static extern int xrSuggestInteractionProfileBindings(ulong instance, XrInteractionProfileSuggestedBinding* bindings);
    [DllImport(Lib)] public static extern int xrAttachSessionActionSets(ulong session, XrSessionActionSetsAttachInfo* attachInfo);
    [DllImport(Lib)] public static extern int xrSyncActions(ulong session, XrActionsSyncInfo* syncInfo);
    [DllImport(Lib)] public static extern int xrGetActionStateBoolean(ulong session, XrActionStateGetInfo* getInfo, XrActionStateBoolean* state);
    [DllImport(Lib)] public static extern int xrGetActionStateFloat(ulong session, XrActionStateGetInfo* getInfo, XrActionStateFloat* state);
    [DllImport(Lib)] public static extern int xrGetActionStateVector2f(ulong session, XrActionStateGetInfo* getInfo, XrActionStateVector2f* state);
    [DllImport(Lib)] public static extern int xrCreateActionSpace(ulong session, XrActionSpaceCreateInfo* createInfo, ulong* space);
    [DllImport(Lib)] public static extern int xrApplyHapticFeedback(ulong session, XrHapticActionInfo* hapticActionInfo, XrHapticVibration* hapticFeedback);
    [DllImport(Lib)] public static extern int xrStopHapticFeedback(ulong session, XrHapticActionInfo* hapticActionInfo);
}
