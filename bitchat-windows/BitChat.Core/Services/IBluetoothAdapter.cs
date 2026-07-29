namespace BitChat.Core.Services;

/// <summary>
/// Byte identifier for a BITCHAT peer — first 8 bytes of the hex-encoded public key.
/// Sent inside BLE manufacturer data during advertisement.
/// </summary>
public sealed class BlePeer
{
    public byte[] PeerID { get; }   // 8 bytes
    public string? Nickname { get; }
    public int Rssi { get; }

    public BlePeer(byte[] peerID, string? nickname = null, int rssi = -100)
    {
        PeerID = peerID;
        Nickname = nickname;
        Rssi = rssi;
    }

    public override string ToString() =>
        Convert.ToHexString(PeerID).ToLowerInvariant();
}

/// <summary>
/// Abstraction over the platform's BLE adapter.
///
/// On Windows this backs onto SimpleBLE (scanning / central) and WinRT
/// BLE APIs (advertising / peripheral), which together provide the
/// dual-role GAP needed for mesh networking.
///
/// A <see cref="StubBluetoothAdapter"/> exists for testing; it simulates
/// peer discovery but does not transfer bytes.
/// </summary>
public interface IBluetoothAdapter : IDisposable
{
    /// <summary>True when the adapter is powered on and usable.</summary>
    bool IsPoweredOn { get; }

    /// <summary>True when actively scanning for peers.</summary>
    bool IsScanning { get; }

    /// <summary>True when advertising our own peer identity.</summary>
    bool IsAdvertising { get; }

    /// <summary>Our 8-byte peer ID announced in manufacturer data.</summary>
    byte[] MyPeerID { get; }

    /// <summary>Start scanning for BITCHAT peers.</summary>
    Task StartScanningAsync(CancellationToken ct = default);

    /// <summary>Stop scanning.</summary>
    Task StopScanningAsync();

    /// <summary>
    /// Start advertising our own PeerID + nickname so other devices
    /// can discover and connect to us.
    /// </summary>
    Task StartAdvertisingAsync(byte[] peerID, string? nickname = null,
        CancellationToken ct = default);

    /// <summary>Stop advertising.</summary>
    Task StopAdvertisingAsync();

    /// <summary>
    /// Connect to a discovered peer's GATT server and establish
    /// a writable/notifiable characteristic for data exchange.
    /// </summary>
    Task<IBleLink> ConnectAsync(BlePeer peer, CancellationToken ct = default);

    /// <summary>Maximum MTU bytes writable per GATT write.</summary>
    int Mtu { get; }

    /// <summary>Fired when a new peer is discovered during scanning.</summary>
    event Action<BlePeer>? OnPeerDiscovered;
    /// <summary>Fired when the adapter powers on or off.</summary>
    event Action<bool>? OnAdapterStateChanged;
}

/// <summary>
/// A single BLE data link to one peer, established after GATT connection
/// and characteristic negotiation.
/// </summary>
public interface IBleLink : IDisposable
{
    /// <summary>The remote peer.</summary>
    BlePeer Peer { get; }

    /// <summary>True while the GATT connection is alive.</summary>
    bool IsConnected { get; }

    /// <summary>Maximum payload bytes per write (negotiated MTU minus overhead).</summary>
    int Mtu { get; }

    /// <summary>
    /// Write raw bytes to the remote characteristic.
    /// Throws if the link is disconnected.
    /// </summary>
    Task SendAsync(byte[] data, CancellationToken ct = default);

    /// <summary>
    /// Fired when the remote writes to our characteristic.
    /// The payload is the raw bytes received.
    /// </summary>
    event Action<byte[]>? OnDataReceived;
    /// <summary>Fired when the link is torn down (intentional or not).</summary>
    event Action? OnDisconnected;
}

/// <summary>
/// In-memory stub for tests and development. Simulates peer discovery
/// but provides no real data transfer.
/// </summary>
public sealed class StubBluetoothAdapter : IBluetoothAdapter
{
    private readonly List<BlePeer> _knownPeers = [];
    private CancellationTokenSource? _scanCts;

    public bool IsPoweredOn { get; private set; } = true;
    public bool IsScanning { get; private set; }
    public bool IsAdvertising { get; private set; }
    public byte[] MyPeerID { get; private set; } = Array.Empty<byte>();
    public int Mtu => 512;

    public event Action<BlePeer>? OnPeerDiscovered;
    public event Action<bool>? OnAdapterStateChanged;

    public void InjectPeer(BlePeer peer)
    {
        _knownPeers.Add(peer);
    }

    public Task StartScanningAsync(CancellationToken ct = default)
    {
        IsScanning = true;
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ScanLoop(_scanCts.Token);
        return Task.CompletedTask;
    }

    private async Task ScanLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var peer in _knownPeers)
                OnPeerDiscovered?.Invoke(peer);
            try { await Task.Delay(2000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public Task StopScanningAsync()
    {
        _scanCts?.Cancel();
        IsScanning = false;
        return Task.CompletedTask;
    }

    public Task StartAdvertisingAsync(byte[] peerID, string? nickname = null,
        CancellationToken ct = default)
    {
        MyPeerID = peerID;
        IsAdvertising = true;
        return Task.CompletedTask;
    }

    public Task StopAdvertisingAsync()
    {
        IsAdvertising = false;
        return Task.CompletedTask;
    }

    public Task<IBleLink> ConnectAsync(BlePeer peer, CancellationToken ct = default) =>
        Task.FromResult<IBleLink>(new StubBleLink(peer));

    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}

internal sealed class StubBleLink : IBleLink
{
    public BlePeer Peer { get; }
    public bool IsConnected => true;
    public int Mtu => 512;

    public event Action<byte[]>? OnDataReceived;
    public event Action? OnDisconnected;

    public StubBleLink(BlePeer peer) => Peer = peer;

    public Task SendAsync(byte[] data, CancellationToken ct = default)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(10, ct);
            OnDataReceived?.Invoke(data);
        }, ct);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        OnDisconnected?.Invoke();
    }
}
