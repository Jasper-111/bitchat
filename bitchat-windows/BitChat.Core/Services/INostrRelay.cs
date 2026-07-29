using BitChat.Core.Nostr;

namespace BitChat.Core.Services;

public interface INostrRelay : IDisposable
{
    Uri Url { get; }
    bool IsConnected { get; }
    event Action<NostrEvent>? OnEvent;
    event Action<string>? OnNotice;
    event Action<string, bool, string>? OnOk;
    Task ConnectAsync();
    Task Subscribe(string subId, NostrFilter filter, Action<NostrEvent> handler);
    Task Unsubscribe(string subId);
    Task PublishEvent(NostrEvent evt);
    Task DisconnectAsync();
}
