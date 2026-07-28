using System.Collections.Concurrent;
using BitChat.Core.Services.Transport;

namespace BitChat.Core.Services.Store;

/// <summary>
/// Thread-safe scratchpad that maps PeerID ↔ transport addresses (BLE MAC, Nostr pubkey).
/// All transports write into this shared registry so MessageRouter can resolve peers.
/// </summary>
public sealed class PeerRegistry
{
    private readonly ConcurrentDictionary<PeerID, PeerEntry> _entries = [];

    public event Action<PeerID>? OnPeerAdded;
    public event Action<PeerID>? OnPeerUpdated;
    public event Action<string>? OnLog;

    // ═══════════════════════════════════════════════════════════
    //  Write
    // ═══════════════════════════════════════════════════════════

    /// <summary>Register or update a BLE address for a peer.</summary>
    public void SetBleAddress(PeerID peer, string macAddress)
    {
        var entry = _entries.GetOrAdd(peer, _ => new PeerEntry(peer));
        if (entry.MacAddress != macAddress)
        {
            entry.MacAddress = macAddress;
            Log($"Peer {peer} BLE → {macAddress}");
            OnPeerUpdated?.Invoke(peer);
        }
    }

    /// <summary>Register or update a Nostr pubkey for a peer.</summary>
    public void SetNostrPubkey(PeerID peer, string pubkeyHex)
    {
        var entry = _entries.GetOrAdd(peer, _ => new PeerEntry(peer));
        if (entry.NostrPubkey != pubkeyHex)
        {
            entry.NostrPubkey = pubkeyHex;
            Log($"Peer {peer} Nostr → {pubkeyHex[..16]}...");
            OnPeerUpdated?.Invoke(peer);
        }
    }

    /// <summary>Set peer nickname.</summary>
    public void SetNickname(PeerID peer, string nickname)
    {
        var entry = _entries.GetOrAdd(peer, _ => new PeerEntry(peer));
        entry.Nickname = nickname;
        entry.LastSeen = DateTime.UtcNow;
    }

    /// <summary>Touch last-seen timestamp.</summary>
    public void Touch(PeerID peer)
    {
        if (_entries.TryGetValue(peer, out var entry))
            entry.LastSeen = DateTime.UtcNow;
    }

    // ═══════════════════════════════════════════════════════════
    //  Read
    // ═══════════════════════════════════════════════════════════

    public string? GetMacAddress(PeerID peer)
        => _entries.TryGetValue(peer, out var e) ? e.MacAddress : null;

    public string? GetNostrPubkey(PeerID peer)
        => _entries.TryGetValue(peer, out var e) ? e.NostrPubkey : null;

    public string GetNickname(PeerID peer)
        => _entries.TryGetValue(peer, out var e) ? e.Nickname ?? peer.ToString()[..8] : peer.ToString()[..8];

    public bool TryGetPeerByMac(string mac, out PeerID peer)
    {
        foreach (var (pid, entry) in _entries)
        {
            if (entry.MacAddress == mac)
            {
                peer = pid;
                return true;
            }
        }
        peer = default;
        return false;
    }

    public bool TryGetPeerByPubkey(string pubkeyHex, out PeerID peer)
    {
        foreach (var (pid, entry) in _entries)
        {
            if (entry.NostrPubkey == pubkeyHex)
            {
                peer = pid;
                return true;
            }
        }
        peer = default;
        return false;
    }

    public IReadOnlyList<PeerID> AllPeers => _entries.Keys.ToList();

    private void Log(string msg) => OnLog?.Invoke($"[PeerReg] {msg}");

    private sealed class PeerEntry(PeerID peer)
    {
        public PeerID PeerID = peer;
        public string? MacAddress;
        public string? NostrPubkey;
        public string? Nickname;
        public DateTime LastSeen = DateTime.UtcNow;
    }
}
