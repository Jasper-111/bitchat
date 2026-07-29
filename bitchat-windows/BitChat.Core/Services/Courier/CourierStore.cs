using System.Collections.Concurrent;

namespace BitChat.Core.Services.Courier;

public sealed class CourierStore
{
    private readonly string _storeDir;
    private readonly ConcurrentDictionary<string, StoredEnvelope> _envelopes = [];

    public event Action<string>? OnLog;

    public CourierStore(string? storeDir = null)
    {
        _storeDir = storeDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BitChat", "courier");
        Directory.CreateDirectory(_storeDir);
    }

    public void Deposit(CourierEnvelope envelope, byte[] messageId)
    {
        var key = Convert.ToHexString(messageId).ToLowerInvariant();
        var stored = new StoredEnvelope(envelope, messageId, DateTime.UtcNow);
        _envelopes[key] = stored;

        var filePath = Path.Combine(_storeDir, key + ".bin");
        try { File.WriteAllBytes(filePath, envelope.Encode()); }
        catch (Exception ex) { Log($"Deposit failed: {ex.Message}"); }
    }

    public IReadOnlyList<(CourierEnvelope Envelope, byte[] MessageId)> Fetch(byte[] recipientStaticKey)
    {
        var epochDay = CourierEnvelope.CurrentEpochDay();
        var candidateTags = new HashSet<string>(
            CourierEnvelope.CandidateTags(recipientStaticKey, epochDay)
                .Select(t => Convert.ToHexString(t).ToLowerInvariant()));

        var matches = new List<(CourierEnvelope, byte[])>();

        foreach (var (key, stored) in _envelopes)
        {
            var tag = Convert.ToHexString(stored.Envelope.RecipientTag).ToLowerInvariant();
            if (candidateTags.Contains(tag))
                matches.Add((stored.Envelope, stored.MessageId));
        }

        foreach (var file in Directory.EnumerateFiles(_storeDir, "*.bin"))
        {
            try
            {
                var key = Path.GetFileNameWithoutExtension(file);
                if (_envelopes.ContainsKey(key)) continue;

                var data = File.ReadAllBytes(file);
                var env = CourierEnvelope.Decode(data);
                if (env != null)
                {
                    var tag = Convert.ToHexString(env.RecipientTag).ToLowerInvariant();
                    if (candidateTags.Contains(tag))
                    {
                        var msgId = Convert.FromHexString(key);
                        _envelopes[key] = new StoredEnvelope(env, msgId, DateTime.UtcNow);
                        matches.Add((env, msgId));
                    }
                }
            }
            catch { }
        }

        return matches;
    }

    public void PruneExpired()
    {
        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expired = new List<string>();

        foreach (var (key, stored) in _envelopes)
        {
            if (stored.Envelope.Expiry < now)
                expired.Add(key);
        }

        foreach (var key in expired)
        {
            _envelopes.TryRemove(key, out _);
            try { File.Delete(Path.Combine(_storeDir, key + ".bin")); }
            catch { }
        }

        if (expired.Count > 0) Log($"Pruned {expired.Count} expired envelopes");
    }

    public int Count => _envelopes.Count;

    private void Log(string msg) => OnLog?.Invoke($"[CourierStore] {msg}");

    private sealed record StoredEnvelope(CourierEnvelope Envelope, byte[] MessageId, DateTime DepositedAt);
}
