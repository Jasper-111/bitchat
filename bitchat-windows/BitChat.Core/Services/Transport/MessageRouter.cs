namespace BitChat.Core.Services.Transport;

public sealed class MessageRouter
{
    private readonly ITransport[] _transports;
    private readonly OutboxQueue _outbox;
    private readonly object _lock = new();
    private bool _eventWired;

    public event Action<TransportEvent>? OnTransportEvent;
    public event Action<string>? OnLog;

    public MessageRouter(ITransport[] transports, OutboxQueue? outbox = null)
    {
        _transports = transports;
        _outbox = outbox ?? new OutboxQueue();
        _outbox.OnRetry += OnOutboxRetry;
        _outbox.OnLog += msg => Log(msg);
    }

    public IReadOnlyList<ITransport> Transports => _transports;
    public OutboxQueue Outbox => _outbox;

    public async Task SendPrivateMessage(string content, PeerID to, string? messageID = null)
    {
        var msgId = messageID ?? Guid.NewGuid().ToString("N")[..16];

        // Tier 1: Connected + secure (currently Nostr is always this tier)
        foreach (var t in _transports)
        {
            if (t.CanDeliverSecurely(to))
            {
                Log($"Route: secure via {t.GetType().Name}");
                await t.SendPrivateMessage(content, to, msgId);
                return;
            }
        }

        // Tier 2: Reachable but not connected (e.g. Nostr relay)
        foreach (var t in _transports)
        {
            if (t.IsPeerReachable(to))
            {
                Log($"Route: reachable via {t.GetType().Name}");
                await t.SendPrivateMessage(content, to, msgId);
                return;
            }
        }

        // Tier 3: No transport available — queue for retry
        Log($"Queued: {msgId} for peer {to}");
        _outbox.Enqueue(msgId, to, content);
        _outbox.Start();
    }

    public void MarkDelivered(string messageID) => _outbox.MarkDelivered(messageID);

    private async void OnOutboxRetry(string msgId, PeerID to, byte[]? msgIdBytes)
    {
        foreach (var t in _transports)
        {
            if (t.IsPeerReachable(to))
            {
                await t.SendPrivateMessage("", to, msgId);
                return;
            }
        }
    }

    public bool IsPeerReachable(PeerID peer)
    {
        foreach (var t in _transports)
        {
            if (t.IsPeerReachable(peer)) return true;
        }
        return false;
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

    public void WireEvents()
    {
        lock (_lock)
        {
            if (_eventWired) return;
            _eventWired = true;

            foreach (var t in _transports)
            {
                var transport = t;
                transport.OnEvent += evt =>
                {
                    if (evt.Type == TransportEventType.PrivateMessageReceived && evt.MessageID != null)
                        _outbox.MarkDelivered(evt.MessageID);
                    OnTransportEvent?.Invoke(evt);
                };
                transport.OnLog += msg => Log(msg);
            }
        }
    }

    private void Log(string msg) => OnLog?.Invoke($"[Router] {msg}");
}
