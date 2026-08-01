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
    public void NostrEnvelope_CrossEngineInterop()
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

    [Fact]
    public async Task MiniRelay_RestartAndReconnect()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var round1Relay = new MiniRelayServer(0);
        round1Relay.Start();
        var url1 = $"ws://localhost:{round1Relay.Port}";

        var alice = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [url1], alicePeer);
        var bob = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [url1], bobPeer);

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        var signal1 = WaitSignalAsync<TransportEvent>(onResult =>
            bob.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "before-restart")
                    onResult(evt);
            });

        await alice.SendPrivateMessage("before-restart", bobPeer);
        var r1 = await signal1;
        Assert.Equal("before-restart", r1.Content);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
        round1Relay.Dispose();

        // Round 2: new relay on a different port, new transports
        var round2Relay = new MiniRelayServer(0);
        round2Relay.Start();
        var url2 = $"ws://localhost:{round2Relay.Port}";

        var alice2 = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [url2], alicePeer);
        var bob2 = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [url2], bobPeer);

        alice2.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob2.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice2.StartAsync();
        await bob2.StartAsync();

        var signal2 = WaitSignalAsync<TransportEvent>(onResult =>
            bob2.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "after-restart")
                    onResult(evt);
            });

        await alice2.SendPrivateMessage("after-restart", bobPeer);
        var r2 = await signal2;
        Assert.Equal("after-restart", r2.Content);

        await Task.WhenAll(alice2.StopAsync(), bob2.StopAsync());
        round2Relay.Dispose();
    }

    [Fact]
    public async Task MiniRelay_ThreeClients_CrossTalk()
    {
        var a = NostrIdentity.Generate();
        var b = NostrIdentity.Generate();
        var c = NostrIdentity.Generate();
        var aPeer = new PeerID(Convert.FromHexString(a.PublicKeyHex[..16]));
        var bPeer = new PeerID(Convert.FromHexString(b.PublicKeyHex[..16]));
        var cPeer = new PeerID(Convert.FromHexString(c.PublicKeyHex[..16]));

        var clientA = new NostrTransport(a, new DefaultNostrRelayFactory(), [RelayUrl()], aPeer);
        var clientB = new NostrTransport(b, new DefaultNostrRelayFactory(), [RelayUrl()], bPeer);
        var clientC = new NostrTransport(c, new DefaultNostrRelayFactory(), [RelayUrl()], cPeer);

        clientA.RegisterPeer(bPeer, b.PublicKeyHex);
        clientA.RegisterPeer(cPeer, c.PublicKeyHex);
        clientB.RegisterPeer(aPeer, a.PublicKeyHex);
        clientC.RegisterPeer(aPeer, a.PublicKeyHex);
        clientB.RegisterPeer(cPeer, c.PublicKeyHex);
        clientC.RegisterPeer(bPeer, b.PublicKeyHex);

        var bSignal = WaitSignalAsync<TransportEvent>(onResult =>
            clientB.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "msg-B")
                    onResult(evt);
            });

        var cSignal = WaitSignalAsync<TransportEvent>(onResult =>
            clientC.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "msg-C")
                    onResult(evt);
            });

        await clientA.StartAsync();
        await clientB.StartAsync();
        await clientC.StartAsync();

        await clientA.SendPrivateMessage("msg-B", bPeer);
        await clientA.SendPrivateMessage("msg-C", cPeer);

        var bGot = await bSignal;
        var cGot = await cSignal;
        Assert.Equal("msg-B", bGot.Content);
        Assert.Equal("msg-C", cGot.Content);

        // B → C direct
        var cFromB = WaitSignalAsync<TransportEvent>(onResult =>
            clientC.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "B-to-C")
                    onResult(evt);
            });

        await clientB.SendPrivateMessage("B-to-C", cPeer);
        var cFromBGot = await cFromB;
        Assert.Equal("B-to-C", cFromBGot.Content);

        await Task.WhenAll(clientA.StopAsync(), clientB.StopAsync(), clientC.StopAsync());
    }

    [Fact]
    public async Task MiniRelay_MaxSizeContent()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var alice = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [RelayUrl()], alicePeer);
        var bob = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [RelayUrl()], bobPeer);

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        var large = new string('X', 255);
        var signal = WaitSignalAsync<TransportEvent>(onResult =>
            bob.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content?.Length >= 200)
                    onResult(evt);
            });

        await alice.SendPrivateMessage(large, bobPeer);

        var got = await signal;
        Assert.Equal(255, got.Content?.Length);
        Assert.Equal(large, got.Content);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public async Task MiniRelay_MultipleSubscriptions_EventRouting()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));

        var alice = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [RelayUrl()], alicePeer);
        var bob1 = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [RelayUrl()],
            new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16])));
        // Two transports with same identity — should both receive the same event
        var bob2 = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [RelayUrl()],
            new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16])));

        bob1.RegisterPeer(alicePeer, aliceId.PublicKeyHex);
        bob2.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        var bob1Signal = WaitSignalAsync<TransportEvent>(onResult =>
            bob1.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        var bob2Signal = WaitSignalAsync<TransportEvent>(onResult =>
            bob2.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        await alice.StartAsync();
        await bob1.StartAsync();
        await bob2.StartAsync();

        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));
        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        await alice.SendPrivateMessage("fanout test", bobPeer);

        var got1 = await bob1Signal;
        var got2 = await bob2Signal;
        Assert.Equal("fanout test", got1.Content);
        Assert.Equal("fanout test", got2.Content);

        await Task.WhenAll(alice.StopAsync(), bob1.StopAsync(), bob2.StopAsync());
    }

    [Fact]
    public async Task MiniRelay_EmptyPayload_StillDelivered()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var alice = new NostrTransport(aliceId, new DefaultNostrRelayFactory(), [RelayUrl()], alicePeer);
        var bob = new NostrTransport(bobId, new DefaultNostrRelayFactory(), [RelayUrl()], bobPeer);

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        var signal = WaitSignalAsync<TransportEvent>(onResult =>
            bob.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        await alice.SendPrivateMessage("", bobPeer);

        var got = await signal;
        Assert.Equal("", got.Content);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public void NostrEnvelope_Decode_WithBitchatPrefix_MultipleMessages()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var codec = new MessageCodec(alice);

        for (int i = 0; i < 5; i++)
        {
            var text = $"interop-msg-{i}";
            var (envelope, msgId) = codec.Encode(text, bob.PublicKeyHex);
            var decoded = codec.Decode(envelope, bob);

            Assert.NotNull(decoded);
            Assert.Equal(text, decoded!.Content);
            Assert.Equal(msgId, decoded.MessageID);
            Assert.NotEmpty(msgId);
        }
    }
}
