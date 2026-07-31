using BitChat.Core.Nostr;
using BitChat.Core.Protocol;
using BitChat.Core.Services;
using BitChat.Core.Services.Transport;

namespace BitChat.Core.Tests;

public class TransportIntegrationTests : IDisposable
{
    private readonly MiniRelayServer _relay;

    public TransportIntegrationTests()
    {
        _relay = new MiniRelayServer(0);
        _relay.Start();
    }

    public void Dispose()
    {
        _relay.Dispose();
    }

    private string RelayUrl() => $"ws://localhost:{_relay.Port}";

    private static async Task<T> WaitSignalAsync<T>(Action<Action<T>> subscribe, int timeoutMs = 8000)
    {
        var tcs = new TaskCompletionSource<T>();
        using var cts = new CancellationTokenSource(timeoutMs);
        using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
        subscribe(result => tcs.TrySetResult(result));
        return await tcs.Task;
    }

    private static async Task WaitSignalAsync(Action<Action> subscribe, int timeoutMs = 8000)
    {
        var tcs = new TaskCompletionSource();
        using var cts = new CancellationTokenSource(timeoutMs);
        using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
        subscribe(() => tcs.TrySetResult());
        await tcs.Task;
    }

    [Fact]
    public async Task NostrTransport_SendAndReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var alice = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [RelayUrl()], alicePeer);
        var bob = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [RelayUrl()], bobPeer);

        var signal = WaitSignalAsync<TransportEvent>(onResult =>
            bob.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        await alice.SendPrivateMessage("Nostr transport test", bobPeer);

        var got = await signal;
        Assert.Equal(TransportEventType.PrivateMessageReceived, got.Type);
        Assert.Equal("Nostr transport test", got.Content);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public async Task NostrTransport_DeliveryReceipt()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var alice = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [RelayUrl()], alicePeer);
        var bob = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [RelayUrl()], bobPeer);

        var signal = WaitSignalAsync<TransportEvent>(onResult =>
            alice.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.DataReceived && evt.Content == "delivered")
                    onResult(evt);
            });

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        await alice.SendPrivateMessage("test delivery", bobPeer);

        await signal;

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public async Task MessageRouter_SelectsTransport()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var aliceNostr = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [RelayUrl()], alicePeer);
        var bobNostr = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [RelayUrl()], bobPeer);

        aliceNostr.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bobNostr.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        var signal = WaitSignalAsync<TransportEvent>(onResult =>
            bobNostr.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        var router = new MessageRouter([aliceNostr]);
        router.WireEvents();

        await aliceNostr.StartAsync();
        await bobNostr.StartAsync();

        await router.SendPrivateMessage("router test", bobPeer);

        var got = await signal;
        Assert.Equal(TransportEventType.PrivateMessageReceived, got.Type);
        Assert.Equal("router test", got.Content);

        await router.StopAllAsync();
        await bobNostr.StopAsync();
    }

    [Fact]
    public async Task MessageRouter_QueuesUnreachable()
    {
        var aliceId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var unreachablePeer = new PeerID(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x00, 0x00, 0x01 });

        var aliceNostr = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [RelayUrl()], alicePeer);
        var router = new MessageRouter([aliceNostr]);

        await router.SendPrivateMessage("should queue", unreachablePeer);

        Assert.Equal(1, router.Outbox.PendingCount);
        var pending = router.Outbox.GetPending();
        Assert.Single(pending);
        Assert.Equal("should queue", pending[0].Content);
    }

    [Fact]
    public async Task MessageRouter_MarksDelivered_OnReceipt()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var aliceNostr = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [RelayUrl()], alicePeer);
        var bobNostr = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [RelayUrl()], bobPeer);

        aliceNostr.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bobNostr.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        var router = new MessageRouter([aliceNostr]);
        router.WireEvents();

        var signal = WaitSignalAsync(onResult =>
            router.OnTransportEvent += evt =>
            {
                if (evt.Content == "delivered") onResult();
            });

        await aliceNostr.StartAsync();
        await bobNostr.StartAsync();

        await router.SendPrivateMessage("delivery test", bobPeer);

        await signal;
        Assert.Equal(0, router.Outbox.PendingCount);

        await router.StopAllAsync();
        await bobNostr.StopAsync();
    }

    [Fact]
    public async Task NostrEnvelope_CrossEngineInterop()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();

        var codec = new MessageCodec(aliceId);
        var (evt, _) = codec.Encode("Cross-engine test", bobId.PublicKeyHex);

        var decoded = codec.Decode(evt, bobId);
        Assert.NotNull(decoded);
        Assert.Equal("Cross-engine test", decoded!.Content);
        Assert.Equal(aliceId.PublicKeyHex, decoded.SenderPubkey);
    }
}
