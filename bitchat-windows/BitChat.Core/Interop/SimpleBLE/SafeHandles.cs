using Microsoft.Win32.SafeHandles;

namespace BitChat.Core.Interop.SimpleBLE;

internal sealed class SafeAdapterHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeAdapterHandle(nint existingHandle) : base(true)
    {
        handle = existingHandle;
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.simpleble_adapter_release_handle(handle);
        return true;
    }
}

internal sealed class SafePeripheralHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafePeripheralHandle(nint existingHandle) : base(true)
    {
        handle = existingHandle;
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.simpleble_peripheral_release_handle(handle);
        return true;
    }
}
