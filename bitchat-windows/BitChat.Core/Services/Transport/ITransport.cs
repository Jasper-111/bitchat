namespace BitChat.Core.Services.Transport;

public interface ITransport
{
    PeerID MyPeerID { get; }

    Task StartAsync();
    Task StopAsync();

    bool IsPeerReachable(PeerID peer);
    bool CanDeliverSecurely(PeerID peer);

    Task SendPrivateMessage(string content, PeerID to, string? messageID = null);

    IReadOnlyList<TransportPeerSnapshot> GetPeerSnapshots();

    event Action<TransportEvent>? OnEvent;
    event Action<string>? OnLog;
}
