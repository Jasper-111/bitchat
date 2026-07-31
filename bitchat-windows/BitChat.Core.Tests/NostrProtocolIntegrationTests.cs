using BitChat.Core.Nostr;
using BitChat.Core.Protocol;
using BitChat.Core.Services;
using BitChat.Core.Services.Transport;

namespace BitChat.Core.Tests;

public class NostrProtocolIntegrationTests
{
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
    public async Task TwoTransports_SendAndReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();
        var aliceFactory = new InProcessRelayFactory(hub);
        var bobFactory = new InProcessRelayFactory(hub);

        var alice = new NostrTransport(aliceId, aliceFactory, ["inproc://hub"], alicePeer);
        var bob = new NostrTransport(bobId, bobFactory, ["inproc://hub"], bobPeer);

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

        await alice.SendPrivateMessage("Hello from Alice", bobPeer);

        var found = await signal;
        Assert.Equal(TransportEventType.PrivateMessageReceived, found.Type);
        Assert.Equal("Hello from Alice", found.Content);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public async Task EchoBot_Conversation()
    {
        var aliceId = NostrIdentity.Generate();
        var botId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var botPeer = new PeerID(Convert.FromHexString(botId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();
        var aliceFactory = new InProcessRelayFactory(hub);
        var botFactory = new InProcessRelayFactory(hub);

        var alice = new NostrTransport(aliceId, aliceFactory, ["inproc://hub"], alicePeer);
        var bot = new NostrTransport(botId, botFactory, ["inproc://hub"], botPeer);

        var received = new List<TransportEvent>();
        object receivedLock = new();
        var tcs = new TaskCompletionSource();
        alice.OnEvent += evt =>
        {
            if (evt.Type == TransportEventType.PrivateMessageReceived)
            {
                lock (receivedLock) received.Add(evt);
                if (received.Count >= 2) tcs.TrySetResult();
            }
        };

        bot.OnEvent += async evt =>
        {
            if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content != null)
                await bot.SendPrivateMessage("Echo: " + evt.Content, alicePeer);
        };

        alice.RegisterPeer(botPeer, botId.PublicKeyHex);
        bot.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bot.StartAsync();

        await alice.SendPrivateMessage("ping", botPeer);
        await alice.SendPrivateMessage("pong", botPeer);

        using var cts = new CancellationTokenSource(10000);
        cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
        await tcs.Task;

        lock (receivedLock)
        {
            Assert.Equal("Echo: ping", received[0].Content);
            Assert.Equal("Echo: pong", received[1].Content);
        }

        await Task.WhenAll(alice.StopAsync(), bot.StopAsync());
    }

    [Fact]
    public async Task DeliveryReceipt_GeneratedOnReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();
        var aliceFactory = new InProcessRelayFactory(hub);
        var bobFactory = new InProcessRelayFactory(hub);

        var alice = new NostrTransport(aliceId, aliceFactory, ["inproc://hub"], alicePeer);
        var bob = new NostrTransport(bobId, bobFactory, ["inproc://hub"], bobPeer);

        var signal = WaitSignalAsync(onResult =>
            alice.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.DataReceived && evt.Content == "delivered")
                    onResult();
            });

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        await alice.SendPrivateMessage("test receipt", bobPeer);

        await signal;

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public async Task MultipleTransports_FanOut()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var carol = NostrIdentity.Generate();

        var alicePeer = new PeerID(Convert.FromHexString(alice.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bob.PublicKeyHex[..16]));
        var carolPeer = new PeerID(Convert.FromHexString(carol.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();

        var aliceNostr = new NostrTransport(alice, new InProcessRelayFactory(hub), ["inproc://hub"], alicePeer);
        var bobNostr = new NostrTransport(bob, new InProcessRelayFactory(hub), ["inproc://hub"], bobPeer);
        var carolNostr = new NostrTransport(carol, new InProcessRelayFactory(hub), ["inproc://hub"], carolPeer);

        var bobSignal = WaitSignalAsync<TransportEvent>(onResult =>
            bobNostr.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "Secret for Bob")
                    onResult(evt);
            });

        var carolSignal = WaitSignalAsync<TransportEvent>(onResult =>
            carolNostr.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "Secret for Carol")
                    onResult(evt);
            });

        aliceNostr.RegisterPeer(bobPeer, bob.PublicKeyHex);
        aliceNostr.RegisterPeer(carolPeer, carol.PublicKeyHex);
        bobNostr.RegisterPeer(alicePeer, alice.PublicKeyHex);
        carolNostr.RegisterPeer(alicePeer, alice.PublicKeyHex);

        await aliceNostr.StartAsync();
        await bobNostr.StartAsync();
        await carolNostr.StartAsync();

        await aliceNostr.SendPrivateMessage("Secret for Bob", bobPeer);
        await aliceNostr.SendPrivateMessage("Secret for Carol", carolPeer);

        var bobGot = await bobSignal;
        var carolGot = await carolSignal;

        Assert.Equal("Secret for Bob", bobGot.Content);
        Assert.Equal("Secret for Carol", carolGot.Content);

        await Task.WhenAll(
            aliceNostr.StopAsync(), bobNostr.StopAsync(), carolNostr.StopAsync());
    }

    [Fact]
    public void PrivateMessagePacket_Max255Bytes_TlvLimit()
    {
        var valid = new PrivateMessagePacket("id", new string('x', 255));
        var encoded = valid.Encode();
        var decoded = PrivateMessagePacket.Decode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(new string('x', 255), decoded!.Content);

        var tooLong = new PrivateMessagePacket("id", new string('y', 256));
        var encodedLong = tooLong.Encode();
        var decodedLong = PrivateMessagePacket.Decode(encodedLong);

        Assert.NotNull(decodedLong);
        Assert.NotEqual(new string('y', 256), decodedLong!.Content);
    }

    [Fact]
    public async Task KnownPeers_PopulatedOnReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();

        var alice = new NostrTransport(aliceId, new InProcessRelayFactory(hub), ["inproc://hub"], alicePeer);
        var bob = new NostrTransport(bobId, new InProcessRelayFactory(hub), ["inproc://hub"], bobPeer);

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        var bobSignal = WaitSignalAsync<TransportEvent>(onResult =>
            bob.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        await alice.SendPrivateMessage("hello", bobPeer);

        var found = await bobSignal;
        Assert.Equal(alicePeer, found.PeerID);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public async Task MessageRouter_WithInProcessRelay()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();
        var aliceFactory = new InProcessRelayFactory(hub);
        var bobFactory = new InProcessRelayFactory(hub);

        var aliceNostr = new NostrTransport(aliceId, aliceFactory, ["inproc://hub"], alicePeer);
        var bobNostr = new NostrTransport(bobId, bobFactory, ["inproc://hub"], bobPeer);

        aliceNostr.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bobNostr.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        var bobSignal = WaitSignalAsync<TransportEvent>(onResult =>
            bobNostr.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        var router = new MessageRouter([aliceNostr]);
        router.WireEvents();

        await aliceNostr.StartAsync();
        await bobNostr.StartAsync();

        await router.SendPrivateMessage("router inproc test", bobPeer);

        var found = await bobSignal;
        Assert.Equal("router inproc test", found.Content);

        await router.StopAllAsync();
        await bobNostr.StopAsync();
    }

    [Fact]
    public async Task ConcurrentSends_NoMessageLoss()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();

        var alice = new NostrTransport(aliceId, new InProcessRelayFactory(hub), ["inproc://hub"], alicePeer);
        var bob = new NostrTransport(bobId, new InProcessRelayFactory(hub), ["inproc://hub"], bobPeer);

        var received = new List<TransportEvent>();
        object receivedLock = new();
        var tcs = new TaskCompletionSource();
        bob.OnEvent += evt =>
        {
            if (evt.Type == TransportEventType.PrivateMessageReceived)
            {
                lock (receivedLock)
                {
                    received.Add(evt);
                    if (received.Count >= 20) tcs.TrySetResult();
                }
            }
        };

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        var tasks = Enumerable.Range(0, 20).Select(i =>
            alice.SendPrivateMessage($"msg-{i}", bobPeer));
        await Task.WhenAll(tasks);

        using var cts = new CancellationTokenSource(15000);
        cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
        await tcs.Task;

        lock (receivedLock)
        {
            var contents = received.Select(m => m.Content).OrderBy(s => s).ToList();
            for (int i = 0; i < 20; i++)
                Assert.Contains($"msg-{i}", contents);
        }

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }
}
