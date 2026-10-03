using System;
using System.Runtime.InteropServices;

namespace NIVR.Core;

/// <summary>
/// The handful of raw D3D11 COM calls the core needs (vtable calls, no managed wrappers):
/// get the device Unity renders with, and copy an eye texture into an OpenXR swapchain image.
/// </summary>
internal static unsafe class D3D11
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
    }

    public const uint FormatR8G8B8A8Typeless = 27, FormatR8G8B8A8UNorm = 28, FormatR8G8B8A8UNormSrgb = 29;
    public const uint FormatB8G8R8A8UNorm = 87, FormatB8G8R8A8Typeless = 90, FormatB8G8R8A8UNormSrgb = 91;

    private static void** VTable(IntPtr obj) => *(void***)obj;

    public static uint AddRef(IntPtr unknown) => ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(unknown)[1])(unknown);
    public static uint Release(IntPtr unknown) => ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(unknown)[2])(unknown);

    /// <summary>ID3D11DeviceChild::GetDevice. The returned device has been AddRef'd.</summary>
    public static IntPtr GetDevice(IntPtr deviceChild)
    {
        IntPtr device;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)VTable(deviceChild)[3])(deviceChild, &device);
        return device;
    }

    /// <summary>ID3D11Device::GetImmediateContext. The returned context has been AddRef'd.</summary>
    public static IntPtr GetImmediateContext(IntPtr device)
    {
        IntPtr ctx;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)VTable(device)[40])(device, &ctx);
        return ctx;
    }

    /// <summary>ID3D11Texture2D::GetDesc.</summary>
    public static Texture2DDesc GetDesc(IntPtr texture2D)
    {
        Texture2DDesc desc;
        ((delegate* unmanaged[Stdcall]<IntPtr, Texture2DDesc*, void>)VTable(texture2D)[10])(texture2D, &desc);
        return desc;
    }

    /// <summary>ID3D11DeviceContext::CopyResource. Render thread only.</summary>
    public static void CopyResource(IntPtr context, IntPtr dst, IntPtr src) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)VTable(context)[47])(context, dst, src);

    /// <summary>ID3D11DeviceContext::ResolveSubresource (subresource 0 to 0). Render thread only.</summary>
    public static void Resolve(IntPtr context, IntPtr dst, IntPtr src, uint format) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, IntPtr, uint, uint, void>)VTable(context)[57])(context, dst, 0, src, 0, format);

    /// <summary>Formats in the same typeless family can be copied with CopyResource.</summary>
    public static int Family(uint format) => format switch
    {
        FormatR8G8B8A8Typeless or FormatR8G8B8A8UNorm or FormatR8G8B8A8UNormSrgb => 1,
        FormatB8G8R8A8Typeless or FormatB8G8R8A8UNorm or FormatB8G8R8A8UNormSrgb => 2,
        _ => 1000 + (int)format,
    };
}
