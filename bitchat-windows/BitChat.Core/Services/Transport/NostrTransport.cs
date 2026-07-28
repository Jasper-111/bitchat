using System.Collections.Concurrent;
using BitChat.Core.Nostr;
using BitChat.Core.Protocol;
using BitChat.Core.Services.Transport;

namespace BitChat.Core.Services.Transport;

/// <summary>
/// Nostr relay transport implementing ITransport.
/// Always returns false for IsPeerConnected — Nostr has no persistent links.
/// </summary>
public sealed class NostrTransport : ITransport, IDisposable
{
    private readonly NostrIdentity _identity;
    private readonly List<NostrRelayClient> _relays = [];
    private readonly HashSet<string> _processedEvents = [];
    private readonly HashSet<string> _knownPeers = []; // pubkey hex
    private readonly HashSet<PeerID> _reachablePeers = [];
    private readonly ConcurrentDictionary<string, PeerID> _pubkeyToPeerId = [];
    private PeerID _myPeerID;
    private bool _relaysConnected;

    public PeerID MyPeerID => _myPeerID;
    public string MyNickname => "";

    public event Action<TransportEvent>? OnEvent;
    public event Action<string>? OnLog;

    // Exposed for StatusViewModel
    public string PublicKeyHex => _identity.PublicKeyHex;
    public string Npub => _identity.Npub;
    public bool IsRelayConnected => _relaysConnected;

    public NostrTransport(NostrIdentity identity, PeerID? myPeerID = null)
    {
        _identity = identity;
        _myPeerID = myPeerID ?? new PeerID(Convert.FromHexString(identity.PublicKeyHex[..16]));
    }

    public void SetMyPeerID(PeerID peerID) => _myPeerID = peerID;

    // ═══════════════════════════════════════════════════════════
    //  ITransport: Lifecycle
    // ═══════════════════════════════════════════════════════════

    public async Task StartAsync()
    {
        await ConnectAsync(["ws://localhost:4869"]);
    }

    public async Task StopAsync()
    {
        await DisconnectAsync();
    }

    public async Task ConnectAsync(string[] relayUrls)
    {
        foreach (var url in relayUrls)
        {
            try
            {
                var uri = new Uri(url);
                var client = new NostrRelayClient(uri);
                client.OnNotice += msg => Log($"[{uri.Host}] NOTICE: {msg}");
                await client.ConnectAsync();
                _relays.Add(client);
                Log($"Connected to {uri.Host}");

                var since = (int)DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
                var filter = NostrFilter.GiftWrapsFor(_identity.PublicKeyHex, since);
                await client.Subscribe("dm", filter, OnGiftWrapReceived);
            }
            catch (Exception ex)
            {
                Log($"Failed to connect to {url}: {ex.Message}");
            }
        }

        _relaysConnected = _relays.Count > 0;
        if (_relaysConnected)
            Emit(TransportEvent.RelayState(true));
    }

    public async Task DisconnectAsync()
    {
        foreach (var relay in _relays)
            try { await relay.DisconnectAsync(); }
            catch { }
        _relays.Clear();
        _relaysConnected = false;
        Emit(TransportEvent.RelayState(false));
    }

    // ═══════════════════════════════════════════════════════════
    //  ITransport: Connectivity queries
    // ═══════════════════════════════════════════════════════════

    public bool IsPeerConnected(PeerID peer) => false;
    // Nostr has no persistent link-layer connections

    public bool IsPeerReachable(PeerID peer)
    {
        return _reachablePeers.Contains(peer);
    }

    public bool CanDeliverPromptly(PeerID peer)
    {
        return IsPeerReachable(peer) && _relaysConnected;
        // Known npub makes a peer "reachable", but without relay
        // connection a send only queues locally.
    }

    public bool CanDeliverSecurely(PeerID peer)
    {
        return CanDeliverPromptly(peer);
        // Nostr has no forgeable link bindings
    }

    // ═══════════════════════════════════════════════════════════
    //  ITransport: Messaging
    // ═══════════════════════════════════════════════════════════

    public async Task SendPrivateMessage(string content, PeerID to, string? messageID = null)
    {
        // Resolve peer pubkey
        var recipientPubkey = ResolveRecipientPubkey(to);
        if (recipientPubkey == null)
        {
            Log($"Cannot send: no Nostr pubkey for peer {to}");
            return;
        }

        var msgId = messageID ?? Guid.NewGuid().ToString("N")[..16];

        var pm = new PrivateMessagePacket(msgId, content);
        var pmData = pm.Encode();

        var noisePayload = new NoisePayload(NoisePayloadType.PrivateMessage, pmData);
        var noiseData = noisePayload.Encode();

        var senderHex = _identity.PublicKeyHex;
        var senderID = Convert.FromHexString(senderHex[..16]);
        var recipientID = Convert.FromHexString(recipientPubkey[..16]);

        var packet = new BitchatPacket
        {
            Version = 1,
            Type = MessageType.NoiseEncrypted,
            TTL = 7,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SenderID = senderID,
            RecipientID = recipientID,
            Payload = noiseData
        };
        var binaryData = packet.ToBinary(true);
        if (binaryData == null) return;

        var encoded = "bitchat1:" + Crypto.Base64Url.Encode(binaryData);
        var evt = NostrEnvelope.CreatePrivateMessage(encoded, recipientPubkey, _identity);

        foreach (var relay in _relays)
        {
            try { await relay.PublishEvent(evt); }
            catch { }
        }

        Log($"Sent: {content[..Math.Min(content.Length, 40)]}...");
    }

    // ═══════════════════════════════════════════════════════════
    //  ITransport: Peer snapshots
    // ═══════════════════════════════════════════════════════════

    public IReadOnlyList<TransportPeerSnapshot> GetPeerSnapshots()
    {
        return _reachablePeers.Select(p => new TransportPeerSnapshot(
            p, p.ToString()[..8], false)).ToList();
    }

    // ═══════════════════════════════════════════════════════════
    //  Peer registration (called by MessageRouter / favorites)
    // ═══════════════════════════════════════════════════════════

    public void RegisterPeer(PeerID peerID, string pubkeyHex)
    {
        _reachablePeers.Add(peerID);
        _pubkeyToPeerId[pubkeyHex] = peerID;
    }

    private string? ResolveRecipientPubkey(PeerID peerID)
    {
        foreach (var (pubkey, pid) in _pubkeyToPeerId)
        {
            if (pid == peerID) return pubkey;
        }
        // Also check by short form (first 16 hex chars = 8 bytes of PeerID)
        var shortHex = Convert.ToHexString(
            Convert.FromHexString(peerID.ToString())).ToLowerInvariant();
        foreach (var pk in _knownPeers)
        {
            if (pk.StartsWith(shortHex, StringComparison.OrdinalIgnoreCase))
                return pk;
        }
        return null;
    }

    // ═══════════════════════════════════════════════════════════
    //  Inbound processing (from relay WebSocket)
    // ═══════════════════════════════════════════════════════════

    private void OnGiftWrapReceived(NostrEvent giftWrap)
    {
        try
        {
            if (_processedEvents.Contains(giftWrap.Id)) return;
            _ = ProcessGiftWrapAsync(giftWrap);
        }
        catch { }
    }

    private async Task ProcessGiftWrapAsync(NostrEvent giftWrap)
    {
        await Task.Yield();

        var (content, senderPubkey, _) = NostrEnvelope.DecryptPrivateMessage(giftWrap, _identity);
        _processedEvents.Add(giftWrap.Id);
        _knownPeers.Add(senderPubkey);

        if (!content.StartsWith("bitchat1:")) return;

        var encoded = content[9..];
        var binary = Crypto.Base64Url.Decode(encoded);
        var packet = BitchatPacket.FromBinary(binary);
        if (packet == null || packet.Type != MessageType.NoiseEncrypted) return;

        var np = NoisePayload.Decode(packet.Payload);
        if (np == null || np.Type != NoisePayloadType.PrivateMessage) return;

        var pm = PrivateMessagePacket.Decode(np.Data);
        if (pm == null) return;

        var peerID = new PeerID(Convert.FromHexString(senderPubkey[..16]));
        _reachablePeers.Add(peerID);
        _pubkeyToPeerId[senderPubkey] = peerID;

        Emit(TransportEvent.Message(peerID, pm.Content, pm.MessageID));

        Log($"Received from {senderPubkey[..8]}...: {pm.Content[..Math.Min(pm.Content.Length, 40)]}");
    }

    // ═══════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════

    private void Emit(TransportEvent evt) => OnEvent?.Invoke(evt);
    private void Log(string msg) => OnLog?.Invoke($"[Nostr] {msg}");

    public void Dispose()
    {
        _ = DisconnectAsync();
    }
}
