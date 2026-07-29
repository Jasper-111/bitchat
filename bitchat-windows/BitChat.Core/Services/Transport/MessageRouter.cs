using BitChat.Core.Services.Transport;

namespace BitChat.Core.Services.Transport;

public sealed class MessageRouter
{
    private readonly ITransport[] _transports;
    private readonly OutboxQueue _outbox;

    public event Action<string>? OnLog;

    public MessageRouter(params ITransport[] transports)
    {
        _transports = transports;
        _outbox = new OutboxQueue();
        _outbox.OnRetry += OnOutboxRetry;
        _outbox.OnLog += msg => Log(msg);
    }

    public IReadOnlyList<ITransport> Transports => _transports;
    public OutboxQueue Outbox => _outbox;

    private ITransport? ConnectedTransportFor(PeerID peer)
    {
        foreach (var t in _transports)
        {
            if (t.IsPeerConnected(peer))
                return t;
        }
        return null;
    }

    private ITransport? ReachableTransportFor(PeerID peer)
    {
        foreach (var t in _transports)
        {
            if (t.IsPeerReachable(peer))
                return t;
        }
        return null;
    }

    public async Task SendPrivateMessage(string content, PeerID to, string? messageID = null)
    {
        var msgId = messageID ?? Guid.NewGuid().ToString("N")[..16];

        // Tier 1: Connected + secure session
        foreach (var t in _transports)
        {
            if (t.IsPeerConnected(to) && t.CanDeliverSecurely(to))
            {
                Log($"Route: direct-secure via {t.GetType().Name}");
                await t.SendPrivateMessage(content, to, msgId);
                return;
            }
        }

        // Tier 2: Connected but unsecured
        foreach (var t in _transports)
        {
            if (t.IsPeerConnected(to))
            {
                Log($"Route: direct-unsecured via {t.GetType().Name}");
                await t.SendPrivateMessage(content, to, msgId);
                return;
            }
        }

        // Tier 3: Reachable but not connected (e.g. Nostr relay)
        foreach (var t in _transports)
        {
            if (t.IsPeerReachable(to))
            {
                Log($"Route: reachable via {t.GetType().Name}");
                await t.SendPrivateMessage(content, to, msgId);
                return;
            }
        }

        // Tier 4: No transport available — queue for retry + courier fallback
        Log($"Queued: {msgId} for peer {to}");
        _outbox.Enqueue(msgId, to, content);
        _outbox.Start();
    }

    public void MarkDelivered(string messageID) => _outbox.MarkDelivered(messageID);

    private async void OnOutboxRetry(string msgId, PeerID to, byte[]? msgIdBytes)
    {
        foreach (var t in _transports)
        {
            if (t.IsPeerConnected(to) || t.IsPeerReachable(to))
            {
                var pm = new Protocol.PrivateMessagePacket(msgId, "");
                await t.SendPrivateMessage("", to, msgId);
                return;
            }
        }
    }

    public bool IsPeerReachable(PeerID peer)
    {
        return ReachableTransportFor(peer) != null;
    }

    public IReadOnlyList<TransportPeerSnapshot> GetAllPeerSnapshots()
    {
        var all = new List<TransportPeerSnapshot>();
        foreach (var t in _transports)
            all.AddRange(t.GetPeerSnapshots());
        return all;
    }

    public async Task StartAllAsync()
    {
        foreach (var t in _transports)
        {
            try { await t.StartAsync(); }
            catch (Exception ex) { Log($"Start {t.GetType().Name} failed: {ex.Message}"); }
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var t in _transports)
        {
            try { await t.StopAsync(); }
            catch { }
        }
    }

    public event Action<TransportEvent>? OnTransportEvent;

    public void WireEvents()
    {
        foreach (var t in _transports)
        {
            var transport = t;
            transport.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.MessageID != null)
                    _outbox.MarkDelivered(evt.MessageID);
                OnTransportEvent?.Invoke(evt);
            };
        }
    }

    private void Log(string msg) => OnLog?.Invoke($"[Router] {msg}");
}
