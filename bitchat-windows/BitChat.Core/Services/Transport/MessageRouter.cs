using BitChat.Core.Services.Courier;

namespace BitChat.Core.Services.Transport;

public sealed class MessageRouter
{
    private readonly ITransport[] _transports;
    private readonly OutboxQueue _outbox;
    private readonly CourierStore? _courierStore;
    private readonly object _lock = new();
    private bool _eventWired;

    public event Action<TransportEvent>? OnTransportEvent;
    public event Action<string>? OnLog;

    public MessageRouter(ITransport[] transports, OutboxQueue? outbox = null, CourierStore? courierStore = null)
    {
        _transports = transports;
        _outbox = outbox ?? new OutboxQueue();
        _courierStore = courierStore;
        _outbox.OnRetry += OnOutboxRetry;
        _outbox.OnCourierDeposit += OnCourierDeposit;
        _outbox.OnLog += msg => Log(msg);
    }

    public IReadOnlyList<ITransport> Transports => _transports;
    public OutboxQueue Outbox => _outbox;

    public async Task SendPrivateMessage(string content, PeerID to, string? messageID = null)
    {
        var msgId = messageID ?? Guid.NewGuid().ToString("N")[..16];

        foreach (var t in _transports)
        {
            if (t.CanDeliverSecurely(to))
            {
                Log($"Route: secure via {t.GetType().Name}");
                await t.SendPrivateMessage(content, to, msgId);
                return;
            }
        }

        foreach (var t in _transports)
        {
            if (t.IsPeerReachable(to))
            {
                Log($"Route: reachable via {t.GetType().Name}");
                await t.SendPrivateMessage(content, to, msgId);
                return;
            }
        }

        Log($"Queued: {msgId} for peer {to}");
        _outbox.Enqueue(msgId, to, content);
        _outbox.Start();
    }

    public void MarkDelivered(string messageID) => _outbox.MarkDelivered(messageID);

    private async void OnOutboxRetry(string msgId, PeerID to, byte[]? msgIdBytes, string content)
    {
        foreach (var t in _transports)
        {
            if (t.IsPeerReachable(to))
            {
                await t.SendPrivateMessage(content, to, msgId);
                return;
            }
        }
    }

    private void OnCourierDeposit(string msgId, PeerID to, byte[] msgIdBytes, string content)
    {
        if (_courierStore == null)
        {
            Log($"Courier deposit skipped (no store): {msgId}");
            return;
        }

        var contentBytes = System.Text.Encoding.UTF8.GetBytes(content);
        var expiry = (ulong)DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeMilliseconds();
        var envelope = new CourierEnvelope(msgIdBytes, expiry, contentBytes);

        try
        {
            _courierStore.Deposit(envelope, msgIdBytes);
            Log($"Courier deposited: {msgId}");
        }
        catch (Exception ex)
        {
            Log($"Courier deposit failed: {ex.Message}");
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
