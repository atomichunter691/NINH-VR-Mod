using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NIVR.Core.OpenXR;

/// <summary>What the main thread hands to the render thread for one frame.</summary>
internal struct FrameSubmit
{
    public long displayTime;
    public bool hasLayer;
    public ulong space;
    public XrPosef pose0, pose1;
    public XrFovf fov0, fov1;
    public IntPtr src0, src1; // ID3D11Texture2D* of the two eye textures (same size/format family as the swapchains)
}

/// <summary>
/// OpenXR instance + session + swapchains on Unity's own D3D11 device.
/// Threading: xrWaitFrame, xrLocate*, events and input run on the main thread; xrBeginFrame, the swapchain copy and
/// xrEndFrame run on Unity's render thread through GL.IssuePluginEvent, so they are ordered after the eye rendering.
/// </summary>
internal sealed unsafe class XrSession
{
    public ulong Instance, SystemId, Session;
    public ulong LocalSpace, StageSpace, ViewSpace;
    public XrSessionState State;
    public bool Running;
    public int EyeWidth, EyeHeight;
    public long SwapchainFormat;
    public string RuntimeName = "?", SystemName = "?";
    public bool InstanceLost;
    public volatile bool RecoveryRequested;
    public double RenderMilliseconds;
    private int _generation;
    private void ObserveResult(int r)
    {
        if (r == -13 || r == -17 || r == -2 || r == Xr.SessionLossPending) RecoveryRequested = true;
    }

    public static readonly ConcurrentQueue<string> RenderThreadLog = new();

    private readonly ulong[] _swapchains = new ulong[2];
    private readonly IntPtr[][] _images = new IntPtr[2][];
    private IntPtr _device, _context;
    private readonly object _lock = new();
    private readonly FrameSubmit[] _ring = new FrameSubmit[16];
    private int _ringNext;
    private bool _copyChecked, _copyOk, _resolve;
    private uint _resolveFormat;
    private int _renderErrors;

    private static XrSession s_current;

    // UnmanagedCallersOnly is ambiguous here (the interop UnityEngine.CoreModule declares its own copy), so use a delegate.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void RenderEventDelegate(int eventId);
    private static readonly RenderEventDelegate s_renderDelegate = OnRenderEvent;
    public static readonly IntPtr RenderCallback = Marshal.GetFunctionPointerForDelegate(s_renderDelegate);

    public string Err(int r) => Xr.ResultName(Instance, r);

    public bool CreateInstance(out int result)
    {
        var info = new XrInstanceCreateInfo { type = XrStructureType.InstanceCreateInfo };
        Xr.WriteString(info.applicationInfo.applicationName, 128, "NIVR");
        Xr.WriteString(info.applicationInfo.engineName, 128, "Unity");
        info.applicationInfo.applicationVersion = 1;
        info.applicationInfo.apiVersion = Xr.ApiVersion;
        var ext = Marshal.StringToHGlobalAnsi("XR_KHR_D3D11_enable");
        try
        {
            byte* extPtr = (byte*)ext;
            info.enabledExtensionCount = 1;
            info.enabledExtensionNames = &extPtr;
            ulong instance;
            result = Xr.xrCreateInstance(&info, &instance);
            if (result < 0) return false;
            Instance = instance;
        }
        finally { Marshal.FreeHGlobal(ext); }

        var props = new XrInstanceProperties { type = XrStructureType.InstanceProperties };
        if (Xr.xrGetInstanceProperties(Instance, &props) >= 0)
            RuntimeName = $"{Xr.ReadString(props.runtimeName, 128)} {props.runtimeVersion >> 48}.{(props.runtimeVersion >> 32) & 0xffff}.{props.runtimeVersion & 0xffffffff}";
        InstanceLost = false;
        RecoveryRequested = false;
        return true;
    }

    /// <summary>Finds the HMD and its recommended eye resolution. Fails (form factor unavailable) while no headset is connected.</summary>
    public bool TryGetSystem(out int result)
    {
        var get = new XrSystemGetInfo { type = XrStructureType.SystemGetInfo, formFactor = Xr.FormFactorHmd };
        ulong id;
        result = Xr.xrGetSystem(Instance, &get, &id);
        if (result < 0) return false;
        SystemId = id;

        var props = new XrSystemProperties { type = XrStructureType.SystemProperties };
        if (Xr.xrGetSystemProperties(Instance, SystemId, &props) >= 0)
            SystemName = Xr.ReadString(props.systemName, 256);

        var views = stackalloc XrViewConfigurationView[2];
        views[0].type = views[1].type = XrStructureType.ViewConfigurationView;
        uint count;
        result = Xr.xrEnumerateViewConfigurationViews(Instance, SystemId, Xr.ViewConfigStereo, 2, &count, views);
        if (result < 0 || count < 2) return false;
        EyeWidth = (int)views[0].recommendedImageRectWidth;
        EyeHeight = (int)views[0].recommendedImageRectHeight;
        return true;
    }

    /// <summary>Lists the swapchain formats the runtime offers; valid once the session exists.</summary>
    private long PickFormat()
    {
        uint count;
        if (Xr.xrEnumerateSwapchainFormats(Session, 0, &count, null) < 0 || count == 0) return 0;
        var formats = stackalloc long[(int)count];
        if (Xr.xrEnumerateSwapchainFormats(Session, count, &count, formats) < 0) return 0;
        var list = "";
        bool rgba = false, bgra = false;
        for (int i = 0; i < count; i++)
        {
            list += formats[i] + " ";
            if (formats[i] == D3D11.FormatR8G8B8A8UNormSrgb) rgba = true;
            if (formats[i] == D3D11.FormatB8G8R8A8UNormSrgb) bgra = true;
        }
        CorePlugin.Log.LogInfo($"OpenXR swapchain formats (DXGI): {list}");
        return rgba ? D3D11.FormatR8G8B8A8UNormSrgb : bgra ? D3D11.FormatB8G8R8A8UNormSrgb : 0;
    }

    /// <param name="anyUnityTexture">native pointer of any texture created by Unity; used to reach Unity's ID3D11Device.</param>
    public bool CreateSession(IntPtr anyUnityTexture, int width, int height, out string error)
    {
        error = null;
        IntPtr fn;
        int r = Xr.xrGetInstanceProcAddr(Instance, "xrGetD3D11GraphicsRequirementsKHR", &fn);
        if (r < 0) { error = "xrGetD3D11GraphicsRequirementsKHR not available: " + Err(r); return false; }
        var req = new XrGraphicsRequirementsD3D11 { type = XrStructureType.GraphicsRequirementsD3D11 };
        r = ((delegate* unmanaged[Stdcall]<ulong, ulong, XrGraphicsRequirementsD3D11*, int>)fn)(Instance, SystemId, &req);
        if (r < 0) { error = "xrGetD3D11GraphicsRequirementsKHR: " + Err(r); return false; }

        _device = D3D11.GetDevice(anyUnityTexture);
        _context = D3D11.GetImmediateContext(_device);

        var binding = new XrGraphicsBindingD3D11 { type = XrStructureType.GraphicsBindingD3D11, device = _device };
        var sci = new XrSessionCreateInfo { type = XrStructureType.SessionCreateInfo, next = &binding, systemId = SystemId };
        ulong session;
        r = Xr.xrCreateSession(Instance, &sci, &session);
        if (r < 0) { error = "xrCreateSession: " + Err(r); return false; }
        Session = session;
        State = XrSessionState.Unknown;

        LocalSpace = CreateSpace(Xr.SpaceLocal);
        ViewSpace = CreateSpace(Xr.SpaceView);
        StageSpace = CreateSpace(Xr.SpaceStage); // 0 if the runtime has no stage (no room setup)

        SwapchainFormat = PickFormat();
        if (SwapchainFormat == 0) { error = "runtime offers no 8-bit sRGB swapchain format"; DestroySession(); return false; }

        EyeWidth = width; EyeHeight = height;
        for (int eye = 0; eye < 2; eye++)
        {
            var ci = new XrSwapchainCreateInfo
            {
                type = XrStructureType.SwapchainCreateInfo,
                usageFlags = Xr.UsageColorAttachment | Xr.UsageSampled | Xr.UsageTransferDst,
                format = SwapchainFormat,
                sampleCount = 1, width = (uint)width, height = (uint)height, faceCount = 1, arraySize = 1, mipCount = 1,
            };
            ulong sc;
            r = Xr.xrCreateSwapchain(Session, &ci, &sc);
            if (r < 0) { error = $"xrCreateSwapchain({width}x{height}, fmt {SwapchainFormat}): " + Err(r); DestroySession(); return false; }
            _swapchains[eye] = sc;

            uint n;
            Xr.xrEnumerateSwapchainImages(sc, 0, &n, null);
            var images = new XrSwapchainImageD3D11[(int)n];
            fixed (XrSwapchainImageD3D11* imgs = images)
            {
                for (int i = 0; i < n; i++) imgs[i] = new XrSwapchainImageD3D11 { type = XrStructureType.SwapchainImageD3D11 };
                r = Xr.xrEnumerateSwapchainImages(sc, n, &n, imgs);
                if (r < 0) { error = "xrEnumerateSwapchainImages: " + Err(r); DestroySession(); return false; }
                _images[eye] = new IntPtr[n];
                for (int i = 0; i < n; i++) _images[eye][i] = imgs[i].texture;
            }
        }
        _copyChecked = false;
        _renderErrors = 0;
        s_current = this;
        return true;
    }

    private ulong CreateSpace(int type)
    {
        var ci = new XrReferenceSpaceCreateInfo { type = XrStructureType.ReferenceSpaceCreateInfo, referenceSpaceType = type, poseInReferenceSpace = Xr.IdentityPose };
        ulong space;
        return Xr.xrCreateReferenceSpace(Session, &ci, &space) >= 0 ? space : 0;
    }

    public void DestroySession()
    {
        lock (_lock)
        {
            Running = false;
            _generation++;
            if (Session != 0) Xr.xrDestroySession(Session); // also destroys its spaces and swapchains
            Session = 0; LocalSpace = StageSpace = ViewSpace = 0;
            _swapchains[0] = _swapchains[1] = 0;
            State = XrSessionState.Unknown;
            _images[0] = _images[1] = null;
            if (_context != IntPtr.Zero) { D3D11.Release(_context); _context = IntPtr.Zero; }
            if (_device != IntPtr.Zero) { D3D11.Release(_device); _device = IntPtr.Zero; }
        }
    }

    public void DestroyInstance()
    {
        DestroySession();
        if (Instance != 0) Xr.xrDestroyInstance(Instance);
        Instance = 0; SystemId = 0;
        InstanceLost = false;
    }

    /// <summary>Main thread. Returns true if the session state changed.</summary>
    public bool PollEvents()
    {
        bool changed = false;
        while (Instance != 0)
        {
            var buf = new XrEventDataBuffer { type = XrStructureType.EventDataBuffer };
            int r = Xr.xrPollEvent(Instance, &buf);
            ObserveResult(r);
            if (r != Xr.Success) break; // XR_EVENT_UNAVAILABLE or an error
            switch (buf.type)
            {
                case XrStructureType.EventDataSessionStateChanged:
                    var e = (XrEventDataSessionStateChanged*)&buf;
                    if (e->session != Session) break;
                    State = e->state;
                    changed = true;
                    CorePlugin.Log.LogInfo($"OpenXR session state -> {State}");
                    if (State == XrSessionState.Ready)
                    {
                        var bi = new XrSessionBeginInfo { type = XrStructureType.SessionBeginInfo, primaryViewConfigurationType = Xr.ViewConfigStereo };
                        r = Xr.xrBeginSession(Session, &bi);
                        if (r < 0) CorePlugin.Log.LogError("xrBeginSession: " + Err(r));
                        else lock (_lock) Running = true;
                    }
                    else if (State == XrSessionState.Stopping)
                    {
                        lock (_lock) { Running = false; Xr.xrEndSession(Session); }
                    }
                    else if (State == XrSessionState.Exiting || State == XrSessionState.LossPending)
                    {
                        RecoveryRequested = true;
                        lock (_lock) Running = false;
                    }
                    break;
                case XrStructureType.EventDataInstanceLossPending:
                    CorePlugin.Log.LogWarning("OpenXR instance loss pending (runtime is shutting down)");
                    InstanceLost = true;
                    lock (_lock) Running = false;
                    changed = true;
                    break;
            }
        }
        return changed;
    }

    /// <summary>Main thread. Blocks until the runtime wants the next frame.</summary>
    public bool WaitFrame(out XrFrameState state)
    {
        state = new XrFrameState { type = XrStructureType.FrameState };
        var wi = new XrBaseStruct { type = XrStructureType.FrameWaitInfo };
        fixed (XrFrameState* p = &state)
        {
            int r = Xr.xrWaitFrame(Session, &wi, p);
            ObserveResult(r);
            if (r < 0) { CorePlugin.LogThrottled("waitframe", "xrWaitFrame: " + Err(r)); return false; }
        }
        return true;
    }

    public bool LocateViews(long time, ulong space, XrView* views, out ulong flags)
    {
        views[0].type = views[1].type = XrStructureType.View;
        var li = new XrViewLocateInfo { type = XrStructureType.ViewLocateInfo, viewConfigurationType = Xr.ViewConfigStereo, displayTime = time, space = space };
        var vs = new XrViewState { type = XrStructureType.ViewState };
        uint count;
        int r = Xr.xrLocateViews(Session, &li, &vs, 2, &count, views);
        ObserveResult(r);
        flags = vs.viewStateFlags;
        if (r < 0) { CorePlugin.LogThrottled("locateviews", "xrLocateViews: " + Err(r)); return false; }
        return count == 2 && (flags & Xr.LocOrientationValid) != 0 && (flags & Xr.LocPositionValid) != 0;
    }

    public bool LocateSpace(ulong space, ulong baseSpace, long time, out XrPosef pose, out ulong flags)
    {
        var loc = new XrSpaceLocation { type = XrStructureType.SpaceLocation };
        int r = Xr.xrLocateSpace(space, baseSpace, time, &loc);
        pose = loc.pose; flags = loc.locationFlags;
        return r >= 0 && (flags & Xr.LocOrientationValid) != 0 && (flags & Xr.LocPositionValid) != 0;
    }

    /// <summary>Main thread: stores the frame and returns the event id to pass to GL.IssuePluginEvent(RenderCallback, id).</summary>
    public int QueueFrame(in FrameSubmit frame)
    {
        int slot = _ringNext;
        _ringNext = (_ringNext + 1) & 15;
        _ring[slot] = frame;
        return (_generation << 4) | slot;
    }

    private static void OnRenderEvent(int eventId)
    {
        try { s_current?.RenderThreadSubmit(eventId); }
        catch (Exception e) { RenderThreadLog.Enqueue("render thread exception: " + e); }
    }

    private void RenderError(string msg)
    {
        if (_renderErrors++ < 10) RenderThreadLog.Enqueue(msg);
    }

    /// <summary>Checks once that the eye texture can be copied straight into the swapchain image.</summary>
    private void CheckCopy(IntPtr src, IntPtr dst)
    {
        _copyChecked = true;
        var s = D3D11.GetDesc(src);
        var d = D3D11.GetDesc(dst);
        _copyOk = s.Width == d.Width && s.Height == d.Height && D3D11.Family(s.Format) == D3D11.Family(d.Format) && d.SampleCount == 1;
        _resolve = s.SampleCount > 1;
        _resolveFormat = (uint)SwapchainFormat;
        RenderThreadLog.Enqueue($"eye texture {s.Width}x{s.Height} fmt {s.Format} x{s.SampleCount} -> swapchain {d.Width}x{d.Height} fmt {d.Format} x{d.SampleCount}: " +
                                (_copyOk ? (_resolve ? "resolve" : "copy") : "INCOMPATIBLE, nothing will be shown"));
    }

    private void RenderThreadSubmit(int slot)
    {
        lock (_lock)
        {
            if (!Running || Session == 0 || (slot >> 4) != _generation) return;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            FrameSubmit f = _ring[slot & 15];

            var begin = new XrBaseStruct { type = XrStructureType.FrameBeginInfo };
            int r = Xr.xrBeginFrame(Session, &begin);
            ObserveResult(r);
            if (r < 0) { RenderError("xrBeginFrame: " + Err(r)); return; }

            bool layerOk = f.hasLayer;
            if (layerOk)
            {
                for (int eye = 0; eye < 2; eye++)
                {
                    IntPtr src = eye == 0 ? f.src0 : f.src1;
                    uint index;
                    var acq = new XrBaseStruct { type = XrStructureType.SwapchainImageAcquireInfo };
                    r = Xr.xrAcquireSwapchainImage(_swapchains[eye], &acq, &index);
                    ObserveResult(r);
                    if (r < 0) { RenderError("xrAcquireSwapchainImage: " + Err(r)); layerOk = false; continue; }
                    var wait = new XrSwapchainImageWaitInfo { type = XrStructureType.SwapchainImageWaitInfo, timeout = Xr.InfiniteDuration };
                    r = Xr.xrWaitSwapchainImage(_swapchains[eye], &wait);
                    ObserveResult(r);
                    if (r < 0) { RenderError("xrWaitSwapchainImage: " + Err(r)); layerOk = false; }
                    else
                    {
                        IntPtr dst = _images[eye][index];
                        if (!_copyChecked) CheckCopy(src, dst);
                        if (!_copyOk) layerOk = false;
                        else if (_resolve) D3D11.Resolve(_context, dst, src, _resolveFormat);
                        else D3D11.CopyResource(_context, dst, src);
                    }
                    var rel = new XrBaseStruct { type = XrStructureType.SwapchainImageReleaseInfo };
                    r = Xr.xrReleaseSwapchainImage(_swapchains[eye], &rel);
                    if (r < 0) { RenderError("xrReleaseSwapchainImage: " + Err(r)); layerOk = false; }
                }
            }

            var views = stackalloc XrCompositionLayerProjectionView[2];
            for (int eye = 0; eye < 2; eye++)
            {
                views[eye] = new XrCompositionLayerProjectionView
                {
                    type = XrStructureType.CompositionLayerProjectionView,
                    pose = eye == 0 ? f.pose0 : f.pose1,
                    fov = eye == 0 ? f.fov0 : f.fov1,
                    subImage = new XrSwapchainSubImage
                    {
                        swapchain = _swapchains[eye],
                        imageRect = new XrRect2Di { x = 0, y = 0, width = EyeWidth, height = EyeHeight },
                    },
                };
            }
            var layer = new XrCompositionLayerProjection { type = XrStructureType.CompositionLayerProjection, space = f.space, viewCount = 2, views = views };
            void* layerPtr = &layer;
            var end = new XrFrameEndInfo
            {
                type = XrStructureType.FrameEndInfo,
                displayTime = f.displayTime,
                environmentBlendMode = Xr.BlendOpaque,
                layerCount = layerOk ? 1u : 0u,
                layers = layerOk ? &layerPtr : null,
            };
            r = Xr.xrEndFrame(Session, &end);
            ObserveResult(r);
            RenderMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (r < 0) RenderError("xrEndFrame: " + Err(r));
        }
    }
}
