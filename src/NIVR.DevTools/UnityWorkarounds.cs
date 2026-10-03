using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace NIVR.DevTools;

/// <summary>
/// Unity 6 returns blittable arrays from engine icalls through UnityEngine.Bindings.BlittableArrayWrapper.
/// The Il2CppInterop-unstripped managed side of that (BlittableArrayWrapper.Unmarshal) throws
/// "Instances of abstract classes cannot be created", so APIs like ImageConversion.EncodeToPNG are
/// unusable through the interop assemblies. Call the *_Injected icall directly instead.
/// </summary>
internal static class UnityWorkarounds
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BlittableArrayWrapperNative
    {
        public IntPtr data;
        public int size;
        public int updateFlags;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EncodeToPngDelegate(IntPtr tex, out BlittableArrayWrapperNative ret);

    private static EncodeToPngDelegate _encodeToPng;

    public static byte[] EncodeToPNG(Texture2D tex)
    {
        _encodeToPng ??= IL2CPP.ResolveICall<EncodeToPngDelegate>("UnityEngine.ImageConversion::EncodeToPNG_Injected");
        _encodeToPng(tex.m_CachedPtr, out var ret);
        if (ret.data == IntPtr.Zero || ret.size <= 0)
            throw new InvalidOperationException($"EncodeToPNG_Injected returned no data (size={ret.size})");
        // UpdateFlags.DataIsNativeOwnedMemory (3): 'data' is an allocation header whose first field
        // points at the bytes (BindingsAllocator.GetNativeOwnedDataPointer). Otherwise it is the bytes.
        var src = ret.updateFlags == 3 ? Marshal.ReadIntPtr(ret.data) : ret.data;
        var bytes = new byte[ret.size];
        Marshal.Copy(src, bytes, 0, ret.size);
        if (ret.updateFlags == 3)
        {
            try
            {
                _freeNativeOwned ??= IL2CPP.ResolveICall<FreeDelegate>("UnityEngine.Bindings.BindingsAllocator::FreeNativeOwnedMemory");
                _freeNativeOwned(ret.data);
            }
            catch (Exception) { /* dev tool: leaking one PNG buffer is acceptable */ }
        }
        return bytes;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeDelegate(IntPtr ptr);

    private static FreeDelegate _freeNativeOwned;
}
