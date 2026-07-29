using System.Collections.Concurrent;
using BitChat.Core.Nostr;
using BitChat.Core.Protocol;

namespace BitChat.Core.Services.Transport;

public sealed class NostrTransport : ITransport, IDisposable
{
    private readonly NostrIdentity _identity;
    private readonly string[] _defaultRelayUrls;
    private readonly List<NostrRelayClient> _relays = [];
    private readonly HashSet<string> _processedEvents = [];
    private readonly HashSet<string> _knownPeers = [];
    private readonly HashSet<PeerID> _reachablePeers = [];
    private readonly ConcurrentDictionary<string, PeerID> _pubkeyToPeerId = [];
    private PeerID _myPeerID;
    private bool _relaysConnected;
    private CancellationTokenSource? _reconnectCts;

    public PeerID MyPeerID => _myPeerID;
    public string MyNickname => "";

    public event Action<TransportEvent>? OnEvent;
    public event Action<string>? OnLog;

    public string PublicKeyHex => _identity.PublicKeyHex;
    public string Npub => _identity.Npub;
    public bool IsRelayConnected => _relaysConnected;

    public NostrTransport(NostrIdentity identity, PeerID? myPeerID = null,
        string[]? relayUrls = null)
    {
        _identity = identity;
        _myPeerID = myPeerID ?? new PeerID(Convert.FromHexString(identity.PublicKeyHex[..16]));
        _defaultRelayUrls = relayUrls ?? ["ws://localhost:4869"];
    }

    public void SetMyPeerID(PeerID peerID) => _myPeerID = peerID;

    public async Task StartAsync()
    {
        await ConnectAsync(_defaultRelayUrls);
    }

    public async Task StopAsync()
    {
        _reconnectCts?.Cancel();
        await DisconnectAsync();
    }

    public async Task ConnectAsync(string[] relayUrls)
    {
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();

        await ConnectRelaysAsync(relayUrls);
        _ = ReconnectLoop(_reconnectCts.Token);
    }

    private async Task ConnectRelaysAsync(string[] relayUrls)
    {
        foreach (var url in relayUrls)
        {
            try
            {
                var uri = new Uri(url);
                var client = new NostrRelayClient(uri);
                await client.ConnectAsync();
                _relays.Add(client);
                Log($"Connected to {uri.Host}");

                var since = (int)DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds();
                var filter = NostrFilter.GiftWrapsFor(_identity.PublicKeyHex, since);
                await client.Subscribe("dm-" + Guid.NewGuid().ToString("N")[..6], filter, OnGiftWrapReceived);
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

    private async Task ReconnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (OperationCanceledException) { break; }

            var dead = _relays.Where(r => !r.IsConnected).ToList();
            foreach (var r in dead) _relays.Remove(r);

            if (_relays.Count == 0)
            {
                Log("All relays disconnected, reconnecting...");
                Emit(TransportEvent.RelayState(false));
                await ConnectRelaysAsync(_defaultRelayUrls);
            }
        }
    }

    public async Task DisconnectAsync()
    {
        _reconnectCts?.Cancel();
        foreach (var relay in _relays)
            try { await relay.DisconnectAsync(); }
            catch { }
        _relays.Clear();
        _relaysConnected = false;
        Emit(TransportEvent.RelayState(false));
    }

    public bool IsPeerConnected(PeerID peer) => false;

    public bool IsPeerReachable(PeerID peer)
    {
        return _reachablePeers.Contains(peer);
    }

    public bool CanDeliverPromptly(PeerID peer)
    {
        return IsPeerReachable(peer) && _relaysConnected;
    }

    public bool CanDeliverSecurely(PeerID peer)
    {
        return CanDeliverPromptly(peer);
    }

    public async Task SendPrivateMessage(string content, PeerID to, string? messageID = null)
    {
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

    public async Task SendReceipt(byte receiptType, string originalMessageId, PeerID to)
    {
        var recipientPubkey = ResolveRecipientPubkey(to);
        if (recipientPubkey == null) return;

        var receiptData = System.Text.Encoding.UTF8.GetBytes(originalMessageId);
        var noisePayload = new NoisePayload(receiptType, receiptData);
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
    }

    public IReadOnlyList<TransportPeerSnapshot> GetPeerSnapshots()
    {
        return _reachablePeers.Select(p => new TransportPeerSnapshot(
            p, p.ToString()[..8], false)).ToList();
    }

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
        var shortHex = Convert.ToHexString(
            Convert.FromHexString(peerID.ToString())).ToLowerInvariant();
        foreach (var pk in _knownPeers)
        {
            if (pk.StartsWith(shortHex, StringComparison.OrdinalIgnoreCase))
                return pk;
        }
        return null;
    }

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
        if (np == null) return;

        var peerID = new PeerID(Convert.FromHexString(senderPubkey[..16]));
        _reachablePeers.Add(peerID);
        _pubkeyToPeerId[senderPubkey] = peerID;

        switch (np.Type)
        {
            case NoisePayloadType.PrivateMessage:
                var pm = PrivateMessagePacket.Decode(np.Data);
                if (pm == null) return;
                Emit(TransportEvent.Message(peerID, pm.Content, pm.MessageID));
                Log($"Received from {senderPubkey[..8]}...: {pm.Content[..Math.Min(pm.Content.Length, 40)]}");
                await SendReceipt(NoisePayloadType.Delivered, pm.MessageID, peerID);
                break;

            case NoisePayloadType.Delivered:
                var deliveredMsgId = System.Text.Encoding.UTF8.GetString(np.Data);
                Emit(new TransportEvent
                {
                    Type = TransportEventType.DataReceived,
                    PeerID = peerID,
                    MessageID = deliveredMsgId,
                    Content = "delivered",
                    Timestamp = DateTime.UtcNow
                });
                break;

            case NoisePayloadType.ReadReceipt:
                var readMsgId = System.Text.Encoding.UTF8.GetString(np.Data);
                Emit(new TransportEvent
                {
                    Type = TransportEventType.DataReceived,
                    PeerID = peerID,
                    MessageID = readMsgId,
                    Content = "read",
                    Timestamp = DateTime.UtcNow
                });
                break;
        }
    }

    private void Emit(TransportEvent evt) => OnEvent?.Invoke(evt);
    private void Log(string msg) => OnLog?.Invoke($"[Nostr] {msg}");

    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _ = DisconnectAsync();
    }
}
