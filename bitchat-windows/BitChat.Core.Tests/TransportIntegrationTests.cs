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

    private static async Task<T?> WaitFor<T>(Func<T?> check, int timeoutMs = 5000)
        where T : class
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var result = check();
            if (result != null) return result;
            await Task.Delay(50);
        }
        return null;
    }

    private static async Task<bool> WaitForTrue(Func<bool> check, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (check()) return true;
            await Task.Delay(50);
        }
        return false;
    }

    [Fact]
    public async Task TwoEngines_CanSendAndReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        Message? received = null;
        bob.OnMessageReceived += msg => received = msg;

        await alice.ConnectAsync([RelayUrl()]);
        await bob.ConnectAsync([RelayUrl()]);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "Hello from Alice");

        var found = await WaitFor(() => received);
        Assert.NotNull(found);
        Assert.Equal("Hello from Alice", found.Content);

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task TwoEngines_AutoReply()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        Message? aliceReceived = null;
        alice.OnMessageReceived += msg => aliceReceived = msg;

        bob.OnMessageReceived += async msg =>
        {
            await bob.SendMessageAsync(msg.SenderPubkey, "Echo: " + msg.Content);
        };

        await alice.ConnectAsync([RelayUrl()]);
        await bob.ConnectAsync([RelayUrl()]);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "ping");

        var found = await WaitFor(() => aliceReceived, 10000);
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

        string? aliceReceiptType = null;
        alice.OnReceiptReceived += (sender, msgId, type) => aliceReceiptType = type;

        await alice.ConnectAsync([RelayUrl()]);
        await bob.ConnectAsync([RelayUrl()]);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "test receipt");

        var gotReceipt = await WaitForTrue(() => aliceReceiptType != null, 8000);
        Assert.True(gotReceipt);
        Assert.Equal("delivered", aliceReceiptType);

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

        TransportEvent bobReceived = default;
        bob.OnEvent += evt =>
        {
            if (evt.Type == TransportEventType.PrivateMessageReceived)
                bobReceived = evt;
        };

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        await alice.SendPrivateMessage("Nostr transport test", bobPeer);

        var got = await WaitForTrue(() => bobReceived.Type == TransportEventType.PrivateMessageReceived);
        Assert.True(got);
        Assert.Equal("Nostr transport test", bobReceived.Content);

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

        var deliveryEvents = new List<TransportEvent>();
        alice.OnEvent += evt =>
        {
            if (evt.Type == TransportEventType.DataReceived && evt.Content == "delivered")
                deliveryEvents.Add(evt);
        };

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        await alice.SendPrivateMessage("test delivery", bobPeer);

        var found = await WaitForTrue(() => deliveryEvents.Count > 0, 8000);
        Assert.True(found);

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

        TransportEvent bobReceived = default;
        bobNostr.OnEvent += evt =>
        {
            if (evt.Type == TransportEventType.PrivateMessageReceived)
                bobReceived = evt;
        };

        var router = new MessageRouter(aliceNostr);
        router.WireEvents();

        await aliceNostr.StartAsync();
        await bobNostr.StartAsync();

        await router.SendPrivateMessage("router test", bobPeer);

        var got = await WaitForTrue(() => bobReceived.Type == TransportEventType.PrivateMessageReceived);
        Assert.True(got);
        Assert.Equal("router test", bobReceived.Content);

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

        await aliceNostr.StartAsync();
        await bobNostr.StartAsync();

        await router.SendPrivateMessage("delivery test", bobPeer);

        var delivered = await WaitForTrue(() => router.Outbox.PendingCount == 0, 8000);
        Assert.True(delivered);

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

        client.OnMessageReceived += msg =>
        {
            lock (receivedByClient) receivedByClient.Add(msg);
        };
        bot.OnMessageReceived += async msg =>
        {
            await bot.SendMessageAsync(msg.SenderPubkey, "BOT: " + msg.Content);
        };

        await client.ConnectAsync([RelayUrl()]);
        await bot.ConnectAsync([RelayUrl()]);

        await client.SendMessageAsync(botId.PublicKeyHex, "msg1");
        await client.SendMessageAsync(botId.PublicKeyHex, "msg2");

        var gotTwo = await WaitForTrue(() =>
        {
            lock (receivedByClient) return receivedByClient.Count >= 2;
        }, 10000);
        Assert.True(gotTwo);
        Assert.Equal("BOT: msg1", receivedByClient[0].Content);
        Assert.Equal("BOT: msg2", receivedByClient[1].Content);

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
