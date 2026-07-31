using BitChat.Core.Nostr;

namespace BitChat.Core.Services;

public sealed class DefaultNostrRelayFactory : INostrRelayFactory
{
    public INostrRelay Create(Uri url) => new NostrRelayClient(url);
}
