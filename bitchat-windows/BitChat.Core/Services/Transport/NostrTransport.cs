using System.Collections.Concurrent;
using BitChat.Core.Nostr;
using BitChat.Core.Protocol;

namespace BitChat.Core.Services.Transport;

public sealed class NostrTransport : ITransport, IDisposable
{
    private readonly NostrIdentity _identity;
    private readonly MessageCodec _codec;
    private readonly INostrRelayFactory _relayFactory;
    private readonly string[] _relayUrls;
    private readonly object _lock = new();

    private readonly List<INostrRelay> _relays = [];
    private readonly HashSet<string> _processedEvents = [];
    private readonly HashSet<string> _knownPeers = [];
    private readonly HashSet<PeerID> _reachablePeers = [];
    private readonly ConcurrentDictionary<string, PeerID> _pubkeyToPeerId = [];

    private PeerID _myPeerID;
    private bool _relaysConnected;
    private CancellationTokenSource? _reconnectCts;

    public PeerID MyPeerID => _myPeerID;
    public string PublicKeyHex => _identity.PublicKeyHex;
    public string Npub => _identity.Npub;

    public event Action<TransportEvent>? OnEvent;
    public event Action<string>? OnLog;

    public NostrTransport(NostrIdentity identity, INostrRelayFactory relayFactory,
        string[]? relayUrls = null, PeerID? myPeerID = null)
    {
        _identity = identity;
        _codec = new MessageCodec(identity);
        _relayFactory = relayFactory;
        _relayUrls = relayUrls ?? ["wss://relay.damus.io", "wss://nos.lol"];
        _myPeerID = myPeerID ?? new PeerID(Convert.FromHexString(identity.PublicKeyHex[..16]));
    }

    public void RegisterPeer(PeerID peerID, string pubkeyHex)
    {
        lock (_lock) _reachablePeers.Add(peerID);
        _pubkeyToPeerId[pubkeyHex] = peerID;
    }

    public bool IsPeerReachable(PeerID peer)
    {
        lock (_lock) return _reachablePeers.Contains(peer);
    }

    public bool CanDeliverSecurely(PeerID peer)
    {
        return IsPeerReachable(peer) && _relaysConnected;
    }

    public async Task StartAsync()
    {
        _reconnectCts?.Cancel();
        _reconnectCts = new CancellationTokenSource();

        await ConnectRelaysAsync(_relayUrls);
        _ = ReconnectLoop(_reconnectCts.Token);
    }

    public async Task StopAsync()
    {
        _reconnectCts?.Cancel();
        await DisconnectAsync();
    }

    private async Task ConnectRelaysAsync(string[] relayUrls)
    {
        foreach (var url in relayUrls)
        {
            try
            {
                var uri = new Uri(url);
                var client = _relayFactory.Create(uri);
                await client.ConnectAsync();

                lock (_lock) _relays.Add(client);
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

            lock (_lock)
            {
                var dead = _relays.Where(r => !r.IsConnected).ToList();
                foreach (var r in dead) _relays.Remove(r);
            }

            if (_relays.Count == 0)
            {
                Log("All relays disconnected, reconnecting...");
                Emit(TransportEvent.RelayState(false));
                await ConnectRelaysAsync(_relayUrls);
            }
        }
    }

    private async Task DisconnectAsync()
    {
        _reconnectCts?.Cancel();

        List<INostrRelay> copy;
        lock (_lock)
        {
            copy = new List<INostrRelay>(_relays);
            _relays.Clear();
        }

        foreach (var relay in copy)
        {
            try { await relay.DisconnectAsync(); }
            catch { }
            try { relay.Dispose(); }
            catch { }
        }

        _relaysConnected = false;
        Emit(TransportEvent.RelayState(false));
    }

    public async Task SendPrivateMessage(string content, PeerID to, string? messageID = null)
    {
        var recipientPubkey = ResolveRecipientPubkey(to);
        if (recipientPubkey == null)
        {
            Log($"Cannot send: no Nostr pubkey for peer {to}");
            return;
        }

        var (evt, msgId) = _codec.Encode(content, recipientPubkey, messageID);

        List<INostrRelay> relaysCopy;
        lock (_lock) relaysCopy = new List<INostrRelay>(_relays);

        foreach (var relay in relaysCopy)
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

        var (evt, _) = _codec.EncodeReceipt(receiptType, originalMessageId, recipientPubkey);

        List<INostrRelay> relaysCopy;
        lock (_lock) relaysCopy = new List<INostrRelay>(_relays);

        foreach (var relay in relaysCopy)
        {
            try { await relay.PublishEvent(evt); }
            catch { }
        }
    }

    public IReadOnlyList<TransportPeerSnapshot> GetPeerSnapshots()
    {
        lock (_lock)
        {
            return _reachablePeers
                .Select(p => new TransportPeerSnapshot(p, p.ToString()[..8], false))
                .ToList();
        }
    }

    private string? ResolveRecipientPubkey(PeerID peerID)
    {
        foreach (var (pubkey, pid) in _pubkeyToPeerId)
        {
            if (pid == peerID) return pubkey;
        }

        var shortHex = Convert.ToHexString(
            Convert.FromHexString(peerID.ToString())).ToLowerInvariant();

        lock (_lock)
        {
            return _knownPeers.FirstOrDefault(pk =>
                pk.StartsWith(shortHex, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void OnGiftWrapReceived(NostrEvent giftWrap)
    {
        try
        {
            lock (_lock)
            {
                if (_processedEvents.Contains(giftWrap.Id)) return;
                _processedEvents.Add(giftWrap.Id);
            }
            _ = ProcessGiftWrapAsync(giftWrap);
        }
        catch { }
    }

    private async Task ProcessGiftWrapAsync(NostrEvent giftWrap)
    {
        await Task.Yield();

        var decoded = _codec.Decode(giftWrap, _identity);
        if (decoded == null) return;

        lock (_lock) _knownPeers.Add(decoded.SenderPubkey);

        var peerID = new PeerID(Convert.FromHexString(decoded.SenderPubkey[..16]));

        lock (_lock) _reachablePeers.Add(peerID);
        _pubkeyToPeerId[decoded.SenderPubkey] = peerID;

        switch (decoded.Type)
        {
            case NoisePayloadType.PrivateMessage:
                Emit(TransportEvent.Message(peerID, decoded.Content, decoded.MessageID));
                Log($"Received from {decoded.SenderPubkey[..8]}...: {decoded.Content[..Math.Min(decoded.Content.Length, 40)]}");
                await SendReceipt(NoisePayloadType.Delivered, decoded.MessageID, peerID);
                break;

            case NoisePayloadType.Delivered:
            case NoisePayloadType.ReadReceipt:
                Emit(new TransportEvent
                {
                    Type = TransportEventType.DataReceived,
                    PeerID = peerID,
                    MessageID = decoded.MessageID,
                    Content = decoded.Type == NoisePayloadType.Delivered ? "delivered" : "read",
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

        lock (_lock)
        {
            foreach (var relay in _relays)
            {
                try { relay.Dispose(); }
                catch { }
            }
            _relays.Clear();
        }
    }
}
