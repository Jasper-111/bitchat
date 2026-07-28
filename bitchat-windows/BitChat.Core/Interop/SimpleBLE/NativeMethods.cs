using System.Runtime.InteropServices;

namespace BitChat.Core.Interop.SimpleBLE;

internal static unsafe class NativeMethods
{
    private const string DllName = "simpleble_c";

    // ── Adapter ──────────────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern bool simpleble_adapter_is_bluetooth_enabled();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint simpleble_adapter_get_count();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleAdapter simpleble_adapter_get_handle(nuint index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void simpleble_adapter_release_handle(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr simpleble_adapter_identifier(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr simpleble_adapter_address(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_power_on(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_power_off(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_is_powered(IntPtr handle, out byte powered);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_scan_start(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_scan_stop(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_scan_is_active(IntPtr handle, out byte active);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_scan_for(IntPtr handle, int timeoutMs);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint simpleble_adapter_scan_get_results_count(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBlePeripheral simpleble_adapter_scan_get_results_handle(IntPtr handle, nuint index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint simpleble_adapter_get_paired_peripherals_count(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBlePeripheral simpleble_adapter_get_paired_peripherals_handle(IntPtr handle, nuint index);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint simpleble_adapter_get_connected_peripherals_count(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBlePeripheral simpleble_adapter_get_connected_peripherals_handle(IntPtr handle, nuint index);

    // ── Adapter callbacks ────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_set_callback_on_scan_start(
        IntPtr handle, ScanCallback callback, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_set_callback_on_scan_stop(
        IntPtr handle, ScanCallback callback, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_set_callback_on_scan_updated(
        IntPtr handle, ScanPeripheralCallback callback, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_adapter_set_callback_on_scan_found(
        IntPtr handle, ScanPeripheralCallback callback, IntPtr userData);

    // ── Peripheral ───────────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void simpleble_peripheral_release_handle(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr simpleble_peripheral_identifier(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr simpleble_peripheral_address(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern short simpleble_peripheral_rssi(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern ushort simpleble_peripheral_mtu(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_connect(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_disconnect(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_is_connected(IntPtr handle, out byte connected);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_is_connectable(IntPtr handle, out byte connectable);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_is_paired(IntPtr handle, out byte paired);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_unpair(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint simpleble_peripheral_services_count(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_services_get(
        IntPtr handle, nuint index, SimpleBleService* service);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nuint simpleble_peripheral_manufacturer_data_count(IntPtr handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_manufacturer_data_get(
        IntPtr handle, nuint index, SimpleBleManufacturerData* data);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_read(
        IntPtr handle, SimpleBleUuid service, SimpleBleUuid characteristic,
        out byte* data, out nuint dataLength);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_write_request(
        IntPtr handle, SimpleBleUuid service, SimpleBleUuid characteristic,
        byte* data, nuint dataLength);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_write_command(
        IntPtr handle, SimpleBleUuid service, SimpleBleUuid characteristic,
        byte* data, nuint dataLength);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_notify(
        IntPtr handle, SimpleBleUuid service, SimpleBleUuid characteristic,
        NotifyCallback callback, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_unsubscribe(
        IntPtr handle, SimpleBleUuid service, SimpleBleUuid characteristic);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_set_callback_on_connected(
        IntPtr handle, PeripheralCallback callback, IntPtr userData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern SimpleBleErr simpleble_peripheral_set_callback_on_disconnected(
        IntPtr handle, PeripheralCallback callback, IntPtr userData);

    // ── Memory ───────────────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void simpleble_free(IntPtr ptr);

    // ── Delegate types (must match C function signatures) ────

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ScanCallback(IntPtr adapter, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ScanPeripheralCallback(IntPtr adapter, IntPtr peripheral, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PeripheralCallback(IntPtr peripheral, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void NotifyCallback(
        IntPtr peripheral, SimpleBleUuid service, SimpleBleUuid characteristic,
        byte* data, nuint dataLength, IntPtr userData);
}
