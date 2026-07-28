using System.Collections.Concurrent;
using BitChat.Core.Interop.SimpleBLE;
using BitChat.Core.Interop.WinRT;

namespace BitChat.Core.Services.Transport;

public sealed class BleTransport : ITransport, IDisposable
{
    private readonly Guid _serviceUuid;
    private readonly Guid _characteristicUuid;
    private readonly ushort _manufacturerId;

    private readonly BlePeripheral _peripheral;
    private readonly ConcurrentDictionary<string, BleLink> _links = [];
    private readonly ConcurrentDictionary<PeerID, string> _peerToMac = [];
    private readonly ConcurrentDictionary<string, PeerID> _macToPeer = [];

    private nint _adapterHandle;
    private bool _isScanning;
    private PeerID _myPeerID;
    private string _myNickname = "";

    private NativeMethods.ScanPeripheralCallback? _scanFoundDelegate;
    private NativeMethods.ScanCallback? _scanStartDelegate;
    private NativeMethods.ScanCallback? _scanStopDelegate;

    public PeerID MyPeerID => _myPeerID;
    public string MyNickname => _myNickname;
    public event Action<TransportEvent>? OnEvent;
    public event Action<string>? OnLog;

    public BleTransport(PeerID myPeerID, string nickname = "",
        Guid? serviceUuid = null, Guid? characteristicUuid = null, ushort manufacturerId = 0xFFFF)
    {
        _myPeerID = myPeerID; _myNickname = nickname;
        _serviceUuid = serviceUuid ?? new Guid("F47B5E2D-1A8B-4C3E-9D5F-6A7B8C9D0E1F");
        _characteristicUuid = characteristicUuid ?? new Guid("A1B2C3D4-5E6F-7890-ABCD-EF1234567890");
        _manufacturerId = manufacturerId;
        _peripheral = new BlePeripheral(_serviceUuid, _characteristicUuid, _manufacturerId);
        _peripheral.OnDataReceived += _ => Log("Peripheral received data");
        _peripheral.OnLog += Log;
    }

    public async Task StartAsync() { await Task.Run(StartSync); }
    public async Task StopAsync() { await Task.Run(StopSync); }

    private void StartSync()
    {
        if (!NativeMethods.simpleble_adapter_is_bluetooth_enabled()) { Emit(TransportEvent.BluetoothState(false)); return; }
        var count = NativeMethods.simpleble_adapter_get_count();
        if (count == 0) { Emit(TransportEvent.BluetoothState(false)); return; }
        var adapter = NativeMethods.simpleble_adapter_get_handle(0);
        _adapterHandle = adapter.Handle;
        NativeMethods.simpleble_adapter_power_on(_adapterHandle);
        Emit(TransportEvent.BluetoothState(true));
        var id = NativeFree.ReadAndFree(NativeMethods.simpleble_adapter_identifier(_adapterHandle));
        var addr = NativeFree.ReadAndFree(NativeMethods.simpleble_adapter_address(_adapterHandle));
        Log($"Adapter: {id} ({addr})");

        // Scan callbacks - use ScanPeripheralCallback for all to keep delegate type consistent
        _scanFoundDelegate = OnScanFound;
        _scanStartDelegate = (_, _) => Log("Scan started");
        _scanStopDelegate = (_, _) => Log("Scan stopped");
        NativeMethods.simpleble_adapter_set_callback_on_scan_found(_adapterHandle, _scanFoundDelegate, IntPtr.Zero);
        NativeMethods.simpleble_adapter_set_callback_on_scan_start(_adapterHandle, _scanStartDelegate, IntPtr.Zero);
        NativeMethods.simpleble_adapter_set_callback_on_scan_stop(_adapterHandle, _scanStopDelegate, IntPtr.Zero);

        StartScan();
        _ = Task.Run(() => _peripheral.StartGattServiceAsync());
        // Also start advertising with announce data
        var announce = BuildAnnounce();
        _peripheral.StartAdvertising(announce);

        Log("BleTransport started");
    }

    private void StopSync()
    {
        StopScan();
        foreach (var (_, link) in _links) try { link.DisconnectSync(); } catch { }
        _links.Clear(); _peerToMac.Clear(); _macToPeer.Clear();
        _peripheral.Dispose();
        if (_adapterHandle != IntPtr.Zero)
        {
            NativeMethods.simpleble_adapter_power_off(_adapterHandle);
            NativeMethods.simpleble_adapter_release_handle(_adapterHandle);
            _adapterHandle = IntPtr.Zero;
        }
        Log("BleTransport stopped");
    }

    public bool IsPeerConnected(PeerID peer)
        => _peerToMac.TryGetValue(peer, out var mac) && _links.TryGetValue(mac, out var l) && l.IsConnected;

    public bool IsPeerReachable(PeerID peer)
    {
        if (_peerToMac.TryGetValue(peer, out var mac) && _links.TryGetValue(mac, out var l))
            return l.IsConnected || (DateTime.UtcNow - l.LastSeen).TotalSeconds < 30;
        return false;
    }

    public bool CanDeliverPromptly(PeerID peer) => IsPeerReachable(peer);
    public bool CanDeliverSecurely(PeerID peer) => IsPeerConnected(peer);

    public async Task SendPrivateMessage(string content, PeerID to, string? messageID = null)
    {
        if (!_peerToMac.TryGetValue(to, out var mac)) { Log($"No BLE address for peer {to}"); return; }
        if (_links.TryGetValue(mac, out var link) && link.IsConnected)
            await Task.Run(() => link.WriteSync(System.Text.Encoding.UTF8.GetBytes(content)));
        else
            Log($"Not connected to {to}");
    }

    public IReadOnlyList<TransportPeerSnapshot> GetPeerSnapshots()
        => _links.Values.Where(l => l.PeerID.HasValue)
            .Select(l => new TransportPeerSnapshot(l.PeerID!.Value, l.Nickname ?? "", l.IsConnected, lastSeen: l.LastSeen)).ToList();

    public void StartScan()
    {
        if (_adapterHandle == IntPtr.Zero || _isScanning) return;
        NativeMethods.simpleble_adapter_scan_start(_adapterHandle);
        _isScanning = true; Log("Scan started");
    }

    public void StopScan()
    {
        if (_adapterHandle == IntPtr.Zero || !_isScanning) return;
        NativeMethods.simpleble_adapter_scan_stop(_adapterHandle);
        _isScanning = false; Log("Scan stopped");
    }

    private void OnScanFound(IntPtr adapter, IntPtr peripheralHandle, IntPtr userData)
    {
        try
        {
            var addrPtr = NativeMethods.simpleble_peripheral_address(peripheralHandle);
            var mac = NativeFree.ReadAndFree(addrPtr) ?? "";
            var rssi = NativeMethods.simpleble_peripheral_rssi(peripheralHandle);
            if (string.IsNullOrEmpty(mac)) return;

            var mfrCount = NativeMethods.simpleble_peripheral_manufacturer_data_count(peripheralHandle);
            byte[]? announceData = null;
            for (nuint i = 0; i < mfrCount; i++)
            {
                unsafe
                {
                    var mfr = new SimpleBleManufacturerData();
                    if (NativeMethods.simpleble_peripheral_manufacturer_data_get(peripheralHandle, i, &mfr) == SimpleBleErr.Success)
                    {
                        if (mfr.ManufacturerId == _manufacturerId)
                        {
                            announceData = new byte[(int)mfr.DataLength];
                            for (int j = 0; j < announceData.Length; j++) announceData[j] = mfr.Data[j];
                            break;
                        }
                    }
                }
            }

            if (announceData != null && announceData.Length >= 8)
            {
                var peerID = new PeerID(announceData[..8]);
                var nickname = announceData.Length > 8
                    ? System.Text.Encoding.UTF8.GetString(announceData, 8, Math.Min(announceData.Length - 8, 19)).TrimEnd('\0')
                    : peerID.ToString()[..8];
                RegisterPeer(peerID, mac, nickname);
                if (!_links.ContainsKey(mac))
                    Log($"Discovered {peerID} ({nickname}) at {mac} RSSI={rssi}");
            }
        }
        catch (Exception ex) { Log($"Scan error: {ex.Message}"); }
    }

    private byte[] BuildAnnounce()
    {
        var announce = new byte[27];
        var idBytes = Convert.FromHexString(_myPeerID.ToString());
        Array.Copy(idBytes, announce, Math.Min(idBytes.Length, 8));
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(_myNickname);
        Array.Copy(nameBytes, 0, announce, 8, Math.Min(nameBytes.Length, 19));
        return announce;
    }

    private void RegisterPeer(PeerID peerID, string mac, string nickname)
    {
        _peerToMac[peerID] = mac; _macToPeer[mac] = peerID;
        if (!_links.TryGetValue(mac, out var link))
        { link = new BleLink(mac, _characteristicUuid, _serviceUuid); _links[mac] = link; }
        link.UpdatePeer(peerID, nickname);
    }

    private void Emit(TransportEvent evt) => OnEvent?.Invoke(evt);
    private void Log(string msg) => OnLog?.Invoke($"[BLE] {msg}");
    public void Dispose() { _ = StopAsync(); }
}

internal sealed class BleLink
{
    private readonly Guid _characteristicUuid;
    private readonly Guid _serviceUuid;
    private readonly string _mac;
    private nint _handle;
    private bool _connected;

    public string MacAddress => _mac;
    public PeerID? PeerID { get; private set; }
    public string? Nickname { get; private set; }
    public bool IsConnected => _connected;
    public DateTime LastSeen { get; private set; }

    public BleLink(string mac, Guid characteristicUuid, Guid serviceUuid)
    { _mac = mac; _characteristicUuid = characteristicUuid; _serviceUuid = serviceUuid; }

    public void UpdatePeer(PeerID peerID, string nickname)
    { PeerID = peerID; Nickname = nickname; LastSeen = DateTime.UtcNow; }

    public void WriteSync(byte[] data)
    {
        if (_handle == IntPtr.Zero || !_connected) return;
        var svc = SimpleBleUuid.FromString(_serviceUuid.ToString("D"));
        var chr = SimpleBleUuid.FromString(_characteristicUuid.ToString("D"));
        unsafe { fixed (byte* p = data) { NativeMethods.simpleble_peripheral_write_command(_handle, svc, chr, p, (nuint)data.Length); } }
    }

    public void DisconnectSync()
    {
        if (_handle != IntPtr.Zero) { NativeMethods.simpleble_peripheral_disconnect(_handle); NativeMethods.simpleble_peripheral_release_handle(_handle); _handle = IntPtr.Zero; }
        _connected = false;
    }
}
