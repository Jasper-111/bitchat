using BitChat.Core.Nostr;
using BitChat.Core.Protocol;
using BitChat.Core.Services;

namespace BitChat.Core.Tests;

public class NostrProtocolIntegrationTests
{
    // ═══════════════════════════════════════════════════════════
    //  Full pipeline: ChatEngine → NostrEnvelope → relay → ChatEngine
    //  Uses InProcessRelay (no network, no WebSocket)
    // ═══════════════════════════════════════════════════════════

    private static async Task<T?> WaitFor<T>(Func<T?> check, int timeoutMs = 8000)
        where T : class
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var result = check();
            if (result != null) return result;
            await Task.Delay(30);
        }
        return null;
    }

    private static async Task<bool> WaitForTrue(Func<bool> check, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (check()) return true;
            await Task.Delay(30);
        }
        return false;
    }

    [Fact]
    public async Task TwoEngines_SendAndReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var (relayA, relayB) = InProcessRelayClient.CreatePair();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        Message? received = null;
        bob.OnMessageReceived += msg => received = msg;

        await alice.ConnectToRelay(relayA);
        await bob.ConnectToRelay(relayB);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "Hello from Alice");

        var found = await WaitFor(() => received);
        Assert.NotNull(found);
        Assert.Equal("Hello from Alice", found!.Content);
        Assert.Equal(aliceId.PublicKeyHex, found.SenderPubkey);
        Assert.NotEmpty(found.Id);

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task TwoEngines_UnicodeAndLongMessages()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var (relayA, relayB) = InProcessRelayClient.CreatePair();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        var messages = new List<Message>();
        bob.OnMessageReceived += msg => { lock (messages) messages.Add(msg); };

        await alice.ConnectToRelay(relayA);
        await bob.ConnectToRelay(relayB);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "Hello 世界 \u2764\ufe0f");
        await alice.SendMessageAsync(bobId.PublicKeyHex, new string('A', 200));
        await alice.SendMessageAsync(bobId.PublicKeyHex, "");

        var got = await WaitForTrue(() =>
        {
            lock (messages) return messages.Count >= 3;
        }, 10000);
        Assert.True(got);

        Assert.Equal("Hello 世界 ❤️", messages[0].Content);
        Assert.Equal(200, messages[1].Content.Length);
        Assert.Equal("", messages[2].Content);

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task EchoBot_Conversation()
    {
        var aliceId = NostrIdentity.Generate();
        var botId = NostrIdentity.Generate();
        var (relayA, relayB) = InProcessRelayClient.CreatePair();

        var alice = new ChatEngine(aliceId);
        var bot = new ChatEngine(botId);

        var aliceMessages = new List<Message>();
        alice.OnMessageReceived += msg => { lock (aliceMessages) aliceMessages.Add(msg); };

        bot.OnMessageReceived += async msg =>
        {
            await bot.SendMessageAsync(msg.SenderPubkey, "Echo: " + msg.Content);
        };

        await alice.ConnectToRelay(relayA);
        await bot.ConnectToRelay(relayB);

        await alice.SendMessageAsync(botId.PublicKeyHex, "ping");
        await alice.SendMessageAsync(botId.PublicKeyHex, "pong");

        var got = await WaitForTrue(() =>
        {
            lock (aliceMessages) return aliceMessages.Count >= 2;
        }, 10000);
        Assert.True(got);

        lock (aliceMessages)
        {
            Assert.Equal("Echo: ping", aliceMessages[0].Content);
            Assert.Equal("Echo: pong", aliceMessages[1].Content);
        }

        await Task.WhenAll(alice.DisconnectAsync(), bot.DisconnectAsync());
    }

    [Fact]
    public async Task DeliveryReceipt_GeneratedOnReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var (relayA, relayB) = InProcessRelayClient.CreatePair();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        string? receiptType = null;
        string? receiptSender = null;
        alice.OnReceiptReceived += (sender, msgId, type) =>
        {
            receiptSender = sender;
            receiptType = type;
        };

        await alice.ConnectToRelay(relayA);
        await bob.ConnectToRelay(relayB);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "test receipt");

        var got = await WaitForTrue(() => receiptType != null, 8000);
        Assert.True(got);
        Assert.Equal("delivered", receiptType);
        Assert.Equal(bobId.PublicKeyHex, receiptSender);

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task MultipleEngines_FanOut()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var carol = NostrIdentity.Generate();

        var hub = new InProcessRelayHub();
        var relayA = new InProcessRelayClient(hub, "A");
        var relayB = new InProcessRelayClient(hub, "B");
        var relayC = new InProcessRelayClient(hub, "C");

        var engineA = new ChatEngine(alice);
        var engineB = new ChatEngine(bob);
        var engineC = new ChatEngine(carol);

        Message? bobReceived = null;
        Message? carolReceived = null;
        engineB.OnMessageReceived += msg => bobReceived = msg;
        engineC.OnMessageReceived += msg => carolReceived = msg;

        await engineA.ConnectToRelay(relayA);
        await engineB.ConnectToRelay(relayB);
        await engineC.ConnectToRelay(relayC);

        await engineA.SendMessageAsync(bob.PublicKeyHex, "Secret for Bob");
        await engineA.SendMessageAsync(carol.PublicKeyHex, "Secret for Carol");

        var bobGot = await WaitFor(() => bobReceived, 10000);
        var carolGot = await WaitFor(() => carolReceived, 10000);

        Assert.NotNull(bobGot);
        Assert.Equal("Secret for Bob", bobGot!.Content);
        Assert.NotNull(carolGot);
        Assert.Equal("Secret for Carol", carolGot!.Content);

        await Task.WhenAll(
            engineA.DisconnectAsync(), engineB.DisconnectAsync(), engineC.DisconnectAsync());
    }

    [Fact]
    public async Task FullPipeline_BinaryRoundtrip()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var (relayA, relayB) = InProcessRelayClient.CreatePair();

        var engineA = new ChatEngine(alice);
        var engineB = new ChatEngine(bob);

        Message? received = null;
        engineB.OnMessageReceived += msg => received = msg;

        await engineA.ConnectToRelay(relayA);
        await engineB.ConnectToRelay(relayB);

        var text = "Binary pipeline test with special chars: \x00\x01\x02\x7F\x80\xFF";
        await engineA.SendMessageAsync(bob.PublicKeyHex, text);

        var found = await WaitFor(() => received);
        Assert.NotNull(found);
        Assert.Equal(text, found!.Content);
        Assert.NotEmpty(found.Id);

        await Task.WhenAll(engineA.DisconnectAsync(), engineB.DisconnectAsync());
    }

    [Fact]
    public async Task SendBeforeRecipientOnline_QueuedThenDelivered()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var (relayA, relayB) = InProcessRelayClient.CreatePair();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        // Alice connects, sends, then Bob connects later
        await alice.ConnectToRelay(relayA);

        Message? bobReceived = null;
        bob.OnMessageReceived += msg => bobReceived = msg;

        await alice.SendMessageAsync(bobId.PublicKeyHex, "Message while offline");

        // Bob joins later
        await bob.ConnectToRelay(relayB);

        var found = await WaitFor(() => bobReceived, 10000);
        Assert.NotNull(found);
        Assert.Equal("Message while offline", found!.Content);

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task ConcurrentSends_NoMessageLoss()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var (relayA, relayB) = InProcessRelayClient.CreatePair();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        var received = new List<Message>();
        bob.OnMessageReceived += msg => { lock (received) received.Add(msg); };

        await alice.ConnectToRelay(relayA);
        await bob.ConnectToRelay(relayB);

        var tasks = Enumerable.Range(0, 20).Select(i =>
            alice.SendMessageAsync(bobId.PublicKeyHex, $"msg-{i}"));
        await Task.WhenAll(tasks);

        var got = await WaitForTrue(() =>
        {
            lock (received) return received.Count >= 20;
        }, 15000);
        Assert.True(got);

        lock (received)
        {
            var contents = received.Select(m => m.Content).OrderBy(s => s).ToList();
            for (int i = 0; i < 20; i++)
                Assert.Contains($"msg-{i}", contents);
        }

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task KnownPeers_PopulatedOnReceive()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var (relayA, relayB) = InProcessRelayClient.CreatePair();

        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        await alice.ConnectToRelay(relayA);
        await bob.ConnectToRelay(relayB);

        await alice.SendMessageAsync(bobId.PublicKeyHex, "hello");

        var found = await WaitFor(() =>
            bob.KnownPeers.Contains(aliceId.PublicKeyHex) ? bob : null);
        Assert.NotNull(found);

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());
    }

    [Fact]
    public async Task DisconnectAndReconnect_CleansUpAndRestores()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();

        // Round 1
        var (relayA1, relayB1) = InProcessRelayClient.CreatePair();
        var alice = new ChatEngine(aliceId);
        var bob = new ChatEngine(bobId);

        Message? msg1 = null;
        bob.OnMessageReceived += m => msg1 = m;

        await alice.ConnectToRelay(relayA1);
        await bob.ConnectToRelay(relayB1);
        await alice.SendMessageAsync(bobId.PublicKeyHex, "round1");
        Assert.NotNull(await WaitFor(() => msg1));

        await Task.WhenAll(alice.DisconnectAsync(), bob.DisconnectAsync());

        // Round 2 — fresh connections
        var (relayA2, relayB2) = InProcessRelayClient.CreatePair();
        var alice2 = new ChatEngine(aliceId);
        var bob2 = new ChatEngine(bobId);

        Message? msg2 = null;
        bob2.OnMessageReceived += m => msg2 = m;

        await alice2.ConnectToRelay(relayA2);
        await bob2.ConnectToRelay(relayB2);
        await alice2.SendMessageAsync(bobId.PublicKeyHex, "round2");
        Assert.NotNull(await WaitFor(() => msg2));
        Assert.Equal("round2", msg2!.Content);

        await Task.WhenAll(alice2.DisconnectAsync(), bob2.DisconnectAsync());
    }

    [Fact]
    public async Task MiniRelay_ThreeClients_AllCanExchange()
    {
        var a = NostrIdentity.Generate();
        var b = NostrIdentity.Generate();
        var c = NostrIdentity.Generate();

        var hub = new InProcessRelayHub();
        var relayA = new InProcessRelayClient(hub, "A");
        var relayB = new InProcessRelayClient(hub, "B");
        var relayC = new InProcessRelayClient(hub, "C");

        var engineA = new ChatEngine(a);
        var engineB = new ChatEngine(b);
        var engineC = new ChatEngine(c);

        var receivedByB = new List<Message>();
        var receivedByC = new List<Message>();
        engineB.OnMessageReceived += msg => { lock (receivedByB) receivedByB.Add(msg); };
        engineC.OnMessageReceived += msg => { lock (receivedByC) receivedByC.Add(msg); };

        await engineA.ConnectToRelay(relayA);
        await engineB.ConnectToRelay(relayB);
        await engineC.ConnectToRelay(relayC);

        await engineA.SendMessageAsync(b.PublicKeyHex, "to B");
        await engineA.SendMessageAsync(c.PublicKeyHex, "to C");

        var bGot = await WaitForTrue(() =>
        {
            lock (receivedByB) return receivedByB.Count >= 1;
        }, 10000);
        var cGot = await WaitForTrue(() =>
        {
            lock (receivedByC) return receivedByC.Count >= 1;
        }, 10000);

        Assert.True(bGot);
        Assert.True(cGot);

        await Task.WhenAll(
            engineA.DisconnectAsync(), engineB.DisconnectAsync(), engineC.DisconnectAsync());
    }

    [Fact]
    public async Task CrossEngine_SelfMessage_Roundtrips()
    {
        var identity = NostrIdentity.Generate();

        var engine = new ChatEngine(identity);

        // Self-message at NostrEnvelope layer — send and decrypt with same key
        var content = "self-test";
        var giftWrap = NostrEnvelope.CreatePrivateMessage(content, identity.PublicKeyHex, identity);
        var (decrypted, sender, _) = NostrEnvelope.DecryptPrivateMessage(giftWrap, identity);

        Assert.Equal(content, decrypted);
        Assert.Equal(identity.PublicKeyHex, sender);

        await engine.DisconnectAsync();
    }

    [Fact]
    public void PrivateMessagePacket_Max255Bytes_TlvLimit()
    {
        var valid = new PrivateMessagePacket("id", new string('x', 255));
        var encoded = valid.Encode();
        var decoded = PrivateMessagePacket.Decode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal(new string('x', 255), decoded!.Content);

        // Over 255 bytes: the TLV length field overflows (byte cast)
        var tooLong = new PrivateMessagePacket("id", new string('y', 256));
        var encodedLong = tooLong.Encode();
        var decodedLong = PrivateMessagePacket.Decode(encodedLong);

        Assert.NotNull(decodedLong);
        Assert.NotEqual(new string('y', 256), decodedLong!.Content);
    }
}
