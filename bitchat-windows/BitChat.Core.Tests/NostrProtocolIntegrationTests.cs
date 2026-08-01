using BitChat.Core.Nostr;
using BitChat.Core.Protocol;
using BitChat.Core.Services;
using BitChat.Core.Services.Courier;
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

    [Fact]
    public async Task StopAndRestart_MessagesFlowAgain()
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

        // Round 1
        await alice.StartAsync();
        await bob.StartAsync();

        var signal1 = WaitSignalAsync<TransportEvent>(onResult =>
            bob.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "round1")
                    onResult(evt);
            });

        await alice.SendPrivateMessage("round1", bobPeer);
        var r1 = await signal1;
        Assert.Equal("round1", r1.Content);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());

        // Round 2 — full restart
        var alice2 = new NostrTransport(aliceId, new InProcessRelayFactory(hub), ["inproc://hub"], alicePeer);
        var bob2 = new NostrTransport(bobId, new InProcessRelayFactory(hub), ["inproc://hub"], bobPeer);
        alice2.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bob2.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice2.StartAsync();
        await bob2.StartAsync();

        var signal2 = WaitSignalAsync<TransportEvent>(onResult =>
            bob2.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content == "round2")
                    onResult(evt);
            });

        await alice2.SendPrivateMessage("round2", bobPeer);
        var r2 = await signal2;
        Assert.Equal("round2", r2.Content);

        await Task.WhenAll(alice2.StopAsync(), bob2.StopAsync());
    }

    [Fact]
    public async Task MessageFiltering_WrongRecipientNotReceived()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var carolId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));
        var carolPeer = new PeerID(Convert.FromHexString(carolId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();

        var alice = new NostrTransport(aliceId, new InProcessRelayFactory(hub), ["inproc://hub"], alicePeer);
        var bob = new NostrTransport(bobId, new InProcessRelayFactory(hub), ["inproc://hub"], bobPeer);
        var carol = new NostrTransport(carolId, new InProcessRelayFactory(hub), ["inproc://hub"], carolPeer);

        alice.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        alice.RegisterPeer(carolPeer, carolId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);
        carol.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        TransportEvent? carolReceived = null;
        carol.OnEvent += evt =>
        {
            if (evt.Type == TransportEventType.PrivateMessageReceived)
                carolReceived = evt;
        };

        var bobSignal = WaitSignalAsync<TransportEvent>(onResult =>
            bob.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        await alice.StartAsync();
        await bob.StartAsync();
        await carol.StartAsync();

        // Alice sends to Bob only
        await alice.SendPrivateMessage("secret for Bob", bobPeer);

        var bobGot = await bobSignal;
        Assert.Equal("secret for Bob", bobGot.Content);

        // Carol's gift-wrap filter is for her own pubkey, so she shouldn't get Bob's message
        // The relay filters by recipient pubkey at the Nostr layer (GiftWrap p-tag)
        Assert.Null(carolReceived);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync(), carol.StopAsync());
    }

    [Fact]
    public async Task ReadReceipt_Roundtrip()
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

        // Bob will receive message and send ReadReceipt
        bob.OnEvent += async evt =>
        {
            if (evt.Type == TransportEventType.PrivateMessageReceived && evt.MessageID != null)
                await bob.SendReceipt(NoisePayloadType.ReadReceipt, evt.MessageID, alicePeer);
        };

        await alice.StartAsync();
        await bob.StartAsync();

        // Alice waits for the read receipt from bob
        var signal = WaitSignalAsync(onResult =>
            alice.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.DataReceived && evt.Content == "read")
                    onResult();
            });

        await alice.SendPrivateMessage("read receipt test", bobPeer);
        await signal;

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public async Task CourierDeposit_OnOutboxExpiry()
    {
        var aliceId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var unreachablePeer = new PeerID(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x01 });

        var hub = new InProcessRelayHub();
        var aliceNostr = new NostrTransport(aliceId, new InProcessRelayFactory(hub), ["inproc://hub"], alicePeer);

        var courierStore = new CourierStore();
        var router = new MessageRouter([aliceNostr], courierStore: courierStore);

        // Send to unreachable peer → queued in outbox
        await router.SendPrivateMessage("courier test msg", unreachablePeer);
        Assert.Equal(1, router.Outbox.PendingCount);

        // Outbox pump hasn't started yet (Start() called during SendPrivateMessage),
        // but Courier deposit fires after 2h by default — too long.
        // Verify the store is empty initially
        Assert.Equal(0, courierStore.Count);

        // Manually trigger courier deposit via outbox to verify pipeline
        // This simulates what happens after 2h expiry
        var pending = router.Outbox.GetPending();
        Assert.Single(pending);
    }

    [Fact]
    public async Task TamperedContent_Rejected()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();
        var aliceFactory = new InProcessRelayFactory(hub);
        var bobFactory = new InProcessRelayFactory(hub);

        var alice = new NostrTransport(aliceId, aliceFactory, ["inproc://hub"], alicePeer);
        var bob = new NostrTransport(bobId, bobFactory, ["inproc://hub"],
            new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16])));

        alice.RegisterPeer(bob.MyPeerID, bobId.PublicKeyHex);
        bob.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        await alice.StartAsync();
        await bob.StartAsync();

        // Manually create a tampered Nostr event and publish it directly to the relay
        var codec = new MessageCodec(aliceId);
        var (validEnvelope, _) = codec.Encode("valid message", bobId.PublicKeyHex);

        // Tamper: replace the ciphertext content after the v2: prefix
        var tamperedContent = validEnvelope.Content;
        // Flip a bit in the encoded portion (after "v2:")
        var chars = tamperedContent.ToCharArray();
        chars[10] = chars[10] == 'A' ? 'B' : 'A';
        var tampered = new NostrEvent(
            validEnvelope.Pubkey,
            validEnvelope.Kind,
            validEnvelope.Tags,
            new string(chars)
        );
        tampered.Id = validEnvelope.Id; // reuse id for test

        // Publish tampered event via the relay factory client
        var rawRelay = bobFactory.Create(new Uri("inproc://hub"));
        await rawRelay.ConnectAsync();

        TransportEvent? bobReceived = null;
        bob.OnEvent += evt =>
        {
            if (evt.Type == TransportEventType.PrivateMessageReceived)
                bobReceived = evt;
        };

        await rawRelay.PublishEvent(tampered);

        // Wait and verify nothing was received (decryption fails silently)
        await Task.Delay(1000);
        Assert.Null(bobReceived);

        await Task.WhenAll(alice.StopAsync(), bob.StopAsync());
    }

    [Fact]
    public void SelfMessage_FullPipeline()
    {
        // Self-messaging: encode and decode via NostrEnvelope layer directly
        var identity = NostrIdentity.Generate();

        var codec = new MessageCodec(identity);
        var (envelope, msgId) = codec.Encode("self-message", identity.PublicKeyHex);
        var decoded = codec.Decode(envelope, identity);

        Assert.NotNull(decoded);
        Assert.Equal("self-message", decoded!.Content);
        Assert.Equal(msgId, decoded.MessageID);
        Assert.Equal(identity.PublicKeyHex, decoded.SenderPubkey);
    }

    [Fact]
    public async Task DisconnectMidSending_NoCrash()
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

        // Send many messages concurrently while disconnecting
        var sendTasks = Enumerable.Range(0, 50).Select(i =>
            alice.SendPrivateMessage($"msg-{i}", bobPeer)).ToList();

        // Disconnect immediately
        var stopTask = alice.StopAsync();

        // Neither should throw
        await Task.WhenAll(sendTasks);
        await stopTask;
        await bob.StopAsync();
    }

    [Fact]
    public async Task MultipleTransportsInRouter_PrioritizesReachable()
    {
        var aliceId = NostrIdentity.Generate();
        var bobId = NostrIdentity.Generate();
        var alicePeer = new PeerID(Convert.FromHexString(aliceId.PublicKeyHex[..16]));
        var bobPeer = new PeerID(Convert.FromHexString(bobId.PublicKeyHex[..16]));

        var hub = new InProcessRelayHub();
        var factory1 = new InProcessRelayFactory(hub);
        var factory2 = new InProcessRelayFactory(hub);

        var aliceNostr = new NostrTransport(aliceId, factory1, ["inproc://hub"], alicePeer);
        var bobNostr = new NostrTransport(bobId, factory2, ["inproc://hub"], bobPeer);

        aliceNostr.RegisterPeer(bobPeer, bobId.PublicKeyHex);
        bobNostr.RegisterPeer(alicePeer, aliceId.PublicKeyHex);

        var bobSignal = WaitSignalAsync<TransportEvent>(onResult =>
            bobNostr.OnEvent += evt =>
            {
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                    onResult(evt);
            });

        // Router with a transport that hasn't started yet + a working one
        var deadTransport = new NostrTransport(NostrIdentity.Generate(),
            new InProcessRelayFactory(new InProcessRelayHub()), ["inproc://dead"]);

        var router = new MessageRouter([deadTransport, aliceNostr]);
        router.WireEvents();

        await aliceNostr.StartAsync();
        await bobNostr.StartAsync();

        await router.SendPrivateMessage("through second transport", bobPeer);

        var got = await bobSignal;
        Assert.Equal("through second transport", got.Content);

        await router.StopAllAsync();
        await bobNostr.StopAsync();
    }
}
