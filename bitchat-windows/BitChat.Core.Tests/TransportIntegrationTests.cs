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
    public async Task TwoEngines_CanSendAndReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        Message received = null!;
        var signal = WaitSignalAsync<Message>(onResult =>
            bob.OnMessageReceived += msg => onResult(msg));

        await alice.ConnectAsync([RelayUrl()]);
        await bob.ConnectAsync([RelayUrl()]);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "Hello from Alice");

        received = await signal;
        Assert.NotNull(received);
        Assert.Equal("Hello from Alice", received.Content);

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task TwoEngines_AutoReply()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        var signal = WaitSignalAsync<Message>(onResult =>
            alice.OnMessageReceived += msg => onResult(msg));

        bob.OnMessageReceived += async msg =>
        {
            await bob.SendMessageAsync(msg.SenderPubkey, "Echo: " + msg.Content);
        };

        await alice.ConnectAsync([RelayUrl()]);
        await bob.ConnectAsync([RelayUrl()]);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "ping");

        var found = await signal;
        Assert.NotNull(found);
        Assert.Equal("Echo: ping", found.Content);

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task DeliveryReceipt_SentAutomatically()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        var signal = WaitSignalAsync(onResult =>
            alice.OnReceiptReceived += (sender, msgId, type) => onResult());

        await alice.ConnectAsync([RelayUrl()]);
        await bob.ConnectAsync([RelayUrl()]);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "test receipt");

        await signal;

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task NostrTransport_SendAndReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var alice = new NostrTransport(aliceId, alicePeer, [RelayUrl()]);
        var bob = new NostrTransport(bobId, bobPeer, [RelayUrl()]);

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

        var alice = new NostrTransport(aliceId, alicePeer, [RelayUrl()]);
        var bob = new NostrTransport(bobId, bobPeer, [RelayUrl()]);

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

        var found = await signal;

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public async Task MessageRouter_SelectsTransport()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var aliceNostr = new NostrTransport(aliceId, alicePeer, [RelayUrl()]);
        var bobNostr = new NostrTransport(bobId, bobPeer, [RelayUrl()]);

        aliceNostr.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bobNostr.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        var signal = WaitSignalAsync<TransportEvent>(onResult =>
            bobNostr.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        var router = new MessageRouter(aliceNostr);
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

        var aliceNostr = new NostrTransport(aliceId, alicePeer, [RelayUrl()]);
        var router = new MessageRouter(aliceNostr);

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

        var aliceNostr = new NostrTransport(aliceId, alicePeer, [RelayUrl()]);
        var bobNostr = new NostrTransport(bobId, bobPeer, [RelayUrl()]);

        aliceNostr.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bobNostr.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        var router = new MessageRouter(aliceNostr);
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
    public async Task EndToEnd_BotPattern()
    {
        var clientId = NostrIdentity.Generate();
        var botId = NostrIdentity.Generate();

        var client = new ChatEngine(clientId);
        var bot = new ChatEngine(botId);

        var receivedByClient = new List<Message>();
        var tcs = new TaskCompletionSource();
        client.OnMessageReceived += msg =>
        {
            lock (receivedByClient)
            {
                receivedByClient.Add(msg);
                if (receivedByClient.Count >= 2) tcs.TrySetResult();
            }
        };

        bot.OnMessageReceived += async msg =>
        {
            await bot.SendMessageAsync(msg.SenderPubkey, "BOT: " + msg.Content);
        };

        await client.ConnectAsync([RelayUrl()]);
        await bot.ConnectAsync([RelayUrl()]);

        await client.SendMessageAsync(botId.PublicKeyHex, "msg1");
        await client.SendMessageAsync(botId.PublicKeyHex, "msg2");

        using var cts = new CancellationTokenSource(12000);
        cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
        await tcs.Task;

        lock (receivedByClient)
        {
            Assert.Equal("BOT: msg1", receivedByClient[0].Content);
            Assert.Equal("BOT: msg2", receivedByClient[1].Content);
        }

        await Task.WhenAll(client.DisconnectAsync(), bot.DisconnectAsync());
    }

    [Fact]
    public async Task NostrEnvelope_CrossEngineInterop()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();

        var content = "Cross-engine test";
        var payload = new NoisePayload(NoisePayloadType.PrivateMessage,
            new PrivateMessagePacket("interop1", content).Encode()).Encode();

        var packet = new BitchatPacket
        {
            Version = 1,
            Type = MessageType.NoiseEncrypted,
            TTL = 7,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SenderID = Convert.FromHexString(aliceId.PublicKeyHex[..16]),
            RecipientID = Convert.FromHexString(bobId.PublicKeyHex[..16]),
            Payload = payload
        };

        var encoded = "bitchat1:" + Crypto.Base64Url.Encode(packet.ToBinary(false)!);
        var evt = NostrEnvelope.CreatePrivateMessage(encoded, bobId.PublicKeyHex, aliceId);

        var (decrypted, sender, _) = NostrEnvelope.DecryptPrivateMessage(evt, bobId);
        Assert.Equal(encoded, decrypted);
        Assert.Equal(aliceId.PublicKeyHex, sender);
    }
}
