using System.Runtime.InteropServices;

namespace BitChat.Core.Interop.SimpleBLE;

// ── Enums ───────────────────────────────────────────────────

internal enum SimpleBleErr { Success = 0, Failure = 1 }

internal enum SimpleBleAddressType { Public = 0, Random = 1, Unspecified = 2 }

// ── Opaque handles ──────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SimpleBleAdapter(nint handle)
{
    public readonly nint Handle = handle;
    public bool IsValid => Handle != 0;
    public static implicit operator nint(SimpleBleAdapter a) => a.Handle;
    public static explicit operator SimpleBleAdapter(nint h) => new(h);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SimpleBlePeripheral(nint handle)
{
    public readonly nint Handle = handle;
    public bool IsValid => Handle != 0;
    public static implicit operator nint(SimpleBlePeripheral p) => p.Handle;
    public static explicit operator SimpleBlePeripheral(nint h) => new(h);
}

// ── Constants ───────────────────────────────────────────────

internal static class SimpleBleConstants
{
    internal const int UuidStrLen = 37;
    internal const int CharacteristicMax = 16;
    internal const int DescriptorMax = 16;
}

// ── Structs (must match C layout exactly) ────────────────────

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Size = SimpleBleConstants.UuidStrLen)]
internal unsafe struct SimpleBleUuid
{
    public fixed byte Value[SimpleBleConstants.UuidStrLen];

    public override string ToString()
    {
        fixed (byte* p = Value)
        {
            int len = 0;
            while (len < SimpleBleConstants.UuidStrLen && p[len] != 0) len++;
            return System.Text.Encoding.ASCII.GetString(p, len);
        }
    }

    public static SimpleBleUuid FromString(string uuid)
    {
        var s = new SimpleBleUuid();
        var bytes = System.Text.Encoding.ASCII.GetBytes(uuid);
        var copy = Math.Min(bytes.Length, SimpleBleConstants.UuidStrLen - 1);
        for (int i = 0; i < copy; i++) s.Value[i] = bytes[i];
        s.Value[copy] = 0;
        return s;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SimpleBleDescriptor
{
    public SimpleBleUuid Uuid;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SimpleBleCharacteristic
{
    public SimpleBleUuid Uuid;
    public byte CanRead;
    public byte CanWriteRequest;
    public byte CanWriteCommand;
    public byte CanNotify;
    public byte CanIndicate;
    public nuint DescriptorCount;
    public fixed byte _descriptors[SimpleBleConstants.DescriptorMax * 37];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SimpleBleService
{
    public SimpleBleUuid Uuid;
    public nuint DataLength;
    public fixed byte Data[27];
    public nuint CharacteristicCount;
    public fixed byte _characteristics[SimpleBleConstants.CharacteristicMax * (37 + 5 + 1 + (SimpleBleConstants.DescriptorMax * 37))];
    // Raw storage for characteristics array — accessed via helper methods
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SimpleBleManufacturerData
{
    public ushort ManufacturerId;
    public nuint DataLength;
    public fixed byte Data[27];
}
