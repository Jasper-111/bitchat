using System.Runtime.InteropServices;

namespace BitChat.Core.Interop.SimpleBLE;

internal static class NativeFree
{
    /// <summary>Free a string returned by SimpleBLE C API.</summary>
    internal static string? ReadAndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringAnsi(ptr);
        }
        finally
        {
            NativeMethods.simpleble_free(ptr);
        }
    }

    /// <summary>Free a byte buffer returned by SimpleBLE C API.</summary>
    internal static byte[]? ReadAndFreeBytes(IntPtr ptr, int length)
    {
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var buf = new byte[length];
            Marshal.Copy(ptr, buf, 0, length);
            return buf;
        }
        finally
        {
            NativeMethods.simpleble_free(ptr);
        }
    }
}
