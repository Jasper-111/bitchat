namespace BitChat.Core.Services.Transport;

/// <summary>Peer identifier across all transports (16 bytes, hex).</summary>
public readonly struct PeerID : IEquatable<PeerID>
{
    private readonly byte[] _bytes; // 8 bytes
    public PeerID(byte[] bytes) { _bytes = bytes.Length >= 8 ? bytes[..8] : PadTo8(bytes); }
    public PeerID(string hex) : this(Convert.FromHexString(hex)) { }

    private static byte[] PadTo8(byte[] src)
    {
        var dst = new byte[8];
        Array.Copy(src, dst, Math.Min(src.Length, 8));
        return dst;
    }

    public override string ToString() => Convert.ToHexString(_bytes).ToLowerInvariant();
    public bool Equals(PeerID other) => _bytes.AsSpan().SequenceEqual(other._bytes);
    public override bool Equals(object? obj) => obj is PeerID other && Equals(other);
    public override int GetHashCode() => BitConverter.ToInt32(_bytes, 0);
    public static bool operator ==(PeerID a, PeerID b) => a.Equals(b);
    public static bool operator !=(PeerID a, PeerID b) => !a.Equals(b);
}

/// <summary>Type of transport event emitted from transport layer up.</summary>
public enum TransportEventType
{
    PeerConnected,
    PeerDisconnected,
    PeerListUpdated,
    PublicMessageReceived,
    PrivateMessageReceived,
    AnnounceReceived,
    DataReceived,
    BluetoothStateUpdated,
    RelayConnected,
    RelayDisconnected,
}

/// <summary>Unified transport event carrying type and payload.</summary>
public readonly struct TransportEvent
{
    public TransportEventType Type { get; init; }
    public PeerID PeerID { get; init; }
    public string? Nickname { get; init; }
    public string? Content { get; init; }
    public byte[]? Payload { get; init; }
    public DateTime Timestamp { get; init; }
    public string? MessageID { get; init; }

    public static TransportEvent PeerConnected(PeerID peer, string? nickname = null)
        => new() { Type = TransportEventType.PeerConnected, PeerID = peer, Nickname = nickname, Timestamp = DateTime.UtcNow };

    public static TransportEvent PeerDisconnected(PeerID peer)
        => new() { Type = TransportEventType.PeerDisconnected, PeerID = peer, Timestamp = DateTime.UtcNow };

    public static TransportEvent PeerListUpdated()
        => new() { Type = TransportEventType.PeerListUpdated };

    public static TransportEvent Message(PeerID peer, string content, string? msgId = null)
        => new() { Type = TransportEventType.PrivateMessageReceived, PeerID = peer, Content = content, MessageID = msgId, Timestamp = DateTime.UtcNow };

    public static TransportEvent PublicMessage(PeerID peer, string content, string? msgId = null)
        => new() { Type = TransportEventType.PublicMessageReceived, PeerID = peer, Content = content, MessageID = msgId, Timestamp = DateTime.UtcNow };

    public static TransportEvent BluetoothState(bool poweredOn)
        => new() { Type = TransportEventType.BluetoothStateUpdated, Content = poweredOn ? "on" : "off" };

    public static TransportEvent RelayState(bool connected)
        => new() { Type = connected ? TransportEventType.RelayConnected : TransportEventType.RelayDisconnected };
}

/// <summary>Point-in-time snapshot of a peer from a transport.</summary>
public readonly struct TransportPeerSnapshot : IEquatable<TransportPeerSnapshot>
{
    public PeerID PeerID { get; init; }
    public string Nickname { get; init; }
    public bool IsConnected { get; init; }
    public byte[]? NoisePublicKey { get; init; }
    public DateTime LastSeen { get; init; }
    public bool IsVerified { get; init; }

    public TransportPeerSnapshot(PeerID peerID, string nickname, bool isConnected,
        byte[]? noisePublicKey = null, DateTime? lastSeen = null, bool isVerified = false)
    {
        PeerID = peerID;
        Nickname = nickname;
        IsConnected = isConnected;
        NoisePublicKey = noisePublicKey;
        LastSeen = lastSeen ?? DateTime.UtcNow;
        IsVerified = isVerified;
    }

    public bool Equals(TransportPeerSnapshot other) => PeerID == other.PeerID;
    public override bool Equals(object? obj) => obj is TransportPeerSnapshot other && Equals(other);
    public override int GetHashCode() => PeerID.GetHashCode();
}
