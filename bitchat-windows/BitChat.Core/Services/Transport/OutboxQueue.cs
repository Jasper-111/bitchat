using System.Collections.Concurrent;

namespace BitChat.Core.Services.Transport;

public sealed class OutboxQueue : IDisposable
{
    private readonly ConcurrentDictionary<string, PendingMessage> _pending = [];
    private readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _maxAge = TimeSpan.FromHours(2);
    private readonly CancellationTokenSource _cts = new();
    private Task? _pumpTask;

    public event Action<string, PeerID, byte[]?>? OnRetry;
    public event Action<string, PeerID, byte[]>? OnCourierDeposit;
    public event Action<string>? OnLog;

    public int PendingCount => _pending.Count;

    public void Start()
    {
        _pumpTask = PumpLoop(_cts.Token);
    }

    public void Enqueue(string messageID, PeerID to, string content, byte[]? messageIdBytes = null)
    {
        var msgId = messageIdBytes ?? Convert.FromHexString(
            messageID.Length >= 32 ? messageID[..32] : messageID.PadRight(32, '0'));
        var pending = new PendingMessage(messageID, to, content, msgId, DateTime.UtcNow);
        _pending[messageID] = pending;
        Log($"Enqueued {messageID} for {to}");
    }

    public void MarkDelivered(string messageID)
    {
        if (_pending.TryRemove(messageID, out _))
            Log($"Delivered {messageID}");
    }

    public IReadOnlyList<PendingMessage> GetPending() => _pending.Values.ToList();

    private async Task PumpLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_retryInterval, ct); }
            catch (OperationCanceledException) { break; }

            var now = DateTime.UtcNow;
            var toRetry = new List<PendingMessage>();
            var toCourier = new List<PendingMessage>();

            foreach (var (_, msg) in _pending)
            {
                if (now - msg.EnqueuedAt > _maxAge)
                    toCourier.Add(msg);
                else if (now - msg.LastAttempt > _retryInterval)
                    toRetry.Add(msg);
            }

            foreach (var msg in toRetry)
            {
                if (_pending.TryGetValue(msg.MessageID, out var p))
                    p.LastAttempt = now;
                OnRetry?.Invoke(msg.MessageID, msg.To, msg.MessageIdBytes);
            }

            foreach (var msg in toCourier)
            {
                if (_pending.TryRemove(msg.MessageID, out _))
                {
                    OnCourierDeposit?.Invoke(msg.MessageID, msg.To, msg.MessageIdBytes);
                    Log($"Courier deposit: {msg.MessageID}");
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private void Log(string msg) => OnLog?.Invoke($"[Outbox] {msg}");

    public sealed class PendingMessage
    {
        public string MessageID { get; }
        public PeerID To { get; }
        public string Content { get; }
        public byte[] MessageIdBytes { get; }
        public DateTime EnqueuedAt { get; }
        public DateTime LastAttempt { get; set; }

        internal PendingMessage(string messageID, PeerID to, string content,
            byte[] messageIdBytes, DateTime enqueuedAt)
        {
            MessageID = messageID;
            To = to;
            Content = content;
            MessageIdBytes = messageIdBytes;
            EnqueuedAt = enqueuedAt;
            LastAttempt = enqueuedAt;
        }
    }
}
