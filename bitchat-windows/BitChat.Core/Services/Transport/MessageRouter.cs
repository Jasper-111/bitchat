using BitChat.Core.Services.Transport;

namespace BitChat.Core.Services.Transport;

/// <summary>
/// Selects the best transport for each message using a priority cascade:
/// Tier 1: connected + canDeliverSecurely → direct send
/// Tier 2: connected + !secure → send + courier deposit
/// Tier 3: reachable, not connected → send (conditional courier)
/// Tier 4: unreachable → queue-only + courier deposit
///
/// Priority order: first transport in list wins on tie.
/// Typical configuration: [BleTransport, NostrTransport] (BLE first).
/// </summary>
public sealed class MessageRouter
{
    private readonly ITransport[] _transports;

    public event Action<string>? OnLog;

    public MessageRouter(params ITransport[] transports)
    {
        _transports = transports;
    }

    public IReadOnlyList<ITransport> Transports => _transports;

    // ═══════════════════════════════════════════════════════════
    //  Transport selection
    // ═══════════════════════════════════════════════════════════

    private ITransport? ConnectedTransportFor(PeerID peer)
    {
        return Array.Find(_transports, t => t.IsPeerConnected(peer));
    }

    private ITransport? ReachableTransportFor(PeerID peer)
    {
        return Array.Find(_transports, t => t.IsPeerReachable(peer));
    }

    // ═══════════════════════════════════════════════════════════
    //  Send
    // ═══════════════════════════════════════════════════════════

    public async Task SendPrivateMessage(string content, PeerID to, string? messageID = null)
    {
        // Tier 1: Connected + secure session
        var connected = ConnectedTransportFor(to);
        if (connected != null && connected.CanDeliverSecurely(to))
        {
            Log($"Route: direct-secure via {connected.GetType().Name}");
            await connected.SendPrivateMessage(content, to, messageID);
            return;
        }

        // Tier 2: Connected but no secure session (link binding forgeable)
        if (connected != null)
        {
            Log($"Route: direct-unsecured via {connected.GetType().Name}");
            await connected.SendPrivateMessage(content, to, messageID);
            return;
        }

        // Tier 3: Reachable but not connected
        var reachable = ReachableTransportFor(to);
        if (reachable != null)
        {
            Log($"Route: reachable via {reachable.GetType().Name}");
            await reachable.SendPrivateMessage(content, to, messageID);
            return;
        }

        // Tier 4: No transport available
        Log($"Route: queued (no transport for peer {to})");
        // In full impl: queue for retry + courier deposit
    }

    // ═══════════════════════════════════════════════════════════
    //  Queries (union across all transports)
    // ═══════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════
    //  Lifecycle
    // ═══════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════
    //  Events (relay from all transports)
    // ═══════════════════════════════════════════════════════════

    public event Action<TransportEvent>? OnTransportEvent;

    public void WireEvents()
    {
        foreach (var t in _transports)
        {
            var transport = t; // capture for closure
            transport.OnEvent += evt => OnTransportEvent?.Invoke(evt);
        }
    }

    // ═══════════════════════════════════════════════════════════

    private void Log(string msg) => OnLog?.Invoke($"[Router] {msg}");
}
