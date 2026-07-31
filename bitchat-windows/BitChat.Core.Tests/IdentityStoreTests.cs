using BitChat.Core.Nostr;
using BitChat.Core.Services;

namespace BitChat.Core.Tests;

public class IdentityStoreTests : IDisposable
{
    private readonly string _testDir;

    public IdentityStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "BitChatTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task SaveLoad_Roundtrip()
    {
        var store = new FileIdentityStore(_testDir);
        var identity = NostrIdentity.Generate();

        Assert.False(store.Exists());
        await store.SaveAsync(identity);
        Assert.True(store.Exists());

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(identity.PublicKeyHex, loaded!.PublicKeyHex);
        Assert.Equal(identity.Npub, loaded.Npub);
        Assert.Equal(
            Convert.ToHexString(identity.PrivateKey).ToLowerInvariant(),
            Convert.ToHexString(loaded.PrivateKey).ToLowerInvariant());
    }

    [Fact]
    public async Task Load_WhenFileNotExists_ReturnsNull()
    {
        var store = new FileIdentityStore(_testDir);
        var result = await store.LoadAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task Exists_ReturnsFalse_WhenNoFile()
    {
        var store = new FileIdentityStore(_testDir);
        Assert.False(store.Exists());
    }

    [Fact]
    public async Task Exists_ReturnsTrue_AfterSave()
    {
        var store = new FileIdentityStore(_testDir);
        await store.SaveAsync(NostrIdentity.Generate());
        Assert.True(store.Exists());
    }

    [Fact]
    public async Task Save_OverwritesPrevious()
    {
        var store = new FileIdentityStore(_testDir);
        var id1 = NostrIdentity.Generate();
        var id2 = NostrIdentity.Generate();

        await store.SaveAsync(id1);
        await store.SaveAsync(id2);

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(id2.PublicKeyHex, loaded!.PublicKeyHex);
    }

    [Fact]
    public async Task Load_ProducesValidIdentity_ForSigning()
    {
        var store = new FileIdentityStore(_testDir);
        var original = NostrIdentity.Generate();
        await store.SaveAsync(original);

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);

        var content = "test message";
        var envelope = NostrEnvelope.CreatePrivateMessage(content, loaded!.PublicKeyHex, loaded);
        var (decoded, sender, _) = NostrEnvelope.DecryptPrivateMessage(envelope, loaded);

        Assert.Equal(content, decoded);
        Assert.Equal(loaded.PublicKeyHex, sender);
    }

    [Fact]
    public async Task Load_SameIdentityProducesSameNpub()
    {
        var store = new FileIdentityStore(_testDir);
        var original = NostrIdentity.Generate();
        await store.SaveAsync(original);

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal(original.Npub, loaded!.Npub);
        Assert.Equal(original.PublicKeyHex, loaded.PublicKeyHex);
    }
}
