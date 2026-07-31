using BitChat.Core.Nostr;

namespace BitChat.Core.Services;

public interface IIdentityStore
{
    Task<NostrIdentity?> LoadAsync();
    Task SaveAsync(NostrIdentity identity);
    bool Exists();
}
