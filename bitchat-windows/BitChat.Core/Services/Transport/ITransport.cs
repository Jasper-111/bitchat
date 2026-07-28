namespace BitChat.Core.Services.Transport;

/// <summary>
/// Abstract transport interface.
/// Transport implementations (BLE, Nostr) conform to this so MessageRouter
/// can select the best transport for each message.
/// </summary>
public interface ITransport
{
    // ── Identity ─────────────────────────────────────────────

    PeerID MyPeerID { get; }
    string MyNickname { get; }

    // ── Lifecycle ────────────────────────────────────────────

    Task StartAsync();
    Task StopAsync();

    // ── Connectivity queries (used by MessageRouter) ─────────

    bool IsPeerConnected(PeerID peer);
    bool IsPeerReachable(PeerID peer);
    bool CanDeliverPromptly(PeerID peer);
    bool CanDeliverSecurely(PeerID peer);

    // ── Messaging ────────────────────────────────────────────

    Task SendPrivateMessage(string content, PeerID to, string? messageID = null);

    // ── Peer snapshots ───────────────────────────────────────

    IReadOnlyList<TransportPeerSnapshot> GetPeerSnapshots();

    // ── Events ───────────────────────────────────────────────

    event Action<TransportEvent>? OnEvent;
}
