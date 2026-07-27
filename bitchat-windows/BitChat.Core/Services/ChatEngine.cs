using BitChat.Core.Crypto;
using BitChat.Core.Nostr;
using BitChat.Core.Protocol;

namespace BitChat.Core.Services;

public class ChatEngine
{
    private NostrIdentity _identity;
    private readonly List<NostrRelayClient> _relays = [];
    private readonly HashSet<string> _processedEvents = [];
    private readonly HashSet<string> _knownPeers = [];

    public NostrIdentity Identity => _identity;
    public IReadOnlySet<string> KnownPeers => _knownPeers;

    public event Action<NostrIdentity>? OnConnected;
    public event Action<Message>? OnMessageReceived;
    public event Action<string, string>? OnLog;

    public ChatEngine(NostrIdentity identity)
    {
        _identity = identity;
    }

    public async Task ConnectAsync(string[] relayUrls)
    {
        foreach (var url in relayUrls)
        {
            try
            {
                var uri = new Uri(url);
                var client = new NostrRelayClient(uri);
                client.OnNotice += msg => Log($"[{uri.Host}] NOTICE: {msg}");
                await client.ConnectAsync();
                _relays.Add(client);
                Log($"Connected to {uri.Host}");

                var since = (int)(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
                var filter = NostrFilter.GiftWrapsFor(_identity.PublicKeyHex, since);
                await client.Subscribe("dm", filter, OnGiftWrapReceived);
                Log($"Subscribed to DMs on {uri.Host}");
            }
            catch (Exception ex)
            {
                Log($"Failed to connect to {url}: {ex.Message}");
            }
        }

        if (_relays.Count > 0) OnConnected?.Invoke(_identity);
    }

    public async Task SendMessageAsync(string recipientPubkeyHex, string text)
    {
        var messageId = Guid.NewGuid().ToString("N")[..16];

        // 1. Encode PrivateMessagePacket
        var pm = new PrivateMessagePacket(messageId, text);
        var pmData = pm.Encode();

        // 2. Wrap in NoisePayload
        var noisePayload = new NoisePayload(NoisePayloadType.PrivateMessage, pmData);
        var noiseData = noisePayload.Encode();

        // 3. Derive senderPeerID (first 16 hex chars of pubkey = 8 bytes)
        var senderHex = _identity.PublicKeyHex;
        var senderID = Convert.FromHexString(senderHex[..16]); // 8 bytes
        var recipientID = Convert.FromHexString(recipientPubkeyHex[..16]); // 8 bytes

        // 4. Encode BinaryProtocol
        var packet = new BitchatPacket
        {
            Version = 1,
            Type = MessageType.NoiseEncrypted,
            TTL = 7,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SenderID = senderID,
            RecipientID = recipientID,
            Payload = noiseData
        };
        var binaryData = packet.ToBinary(true);
        if (binaryData == null) return;

        // 5. Base64url + prefix
        var encoded = "bitchat1:" + Base64Url.Encode(binaryData);

        // 6. Nostr envelope
        var evt = NostrEnvelope.CreatePrivateMessage(encoded, recipientPubkeyHex, _identity);

        // 7. Publish to all relays
        foreach (var relay in _relays)
        {
            try { await relay.PublishEvent(evt); }
            catch { }
        }

        Log($"Sent: {text[..Math.Min(text.Length, 40)]}...");
    }

    private void OnGiftWrapReceived(NostrEvent giftWrap)
    {
        try
        {
            if (_processedEvents.Contains(giftWrap.Id)) return;
            _ = ProcessGiftWrapAsync(giftWrap);
        }
        catch { }
    }

    private async Task ProcessGiftWrapAsync(NostrEvent giftWrap)
    {
        await Task.Yield();

        var (content, senderPubkey, _) = NostrEnvelope.DecryptPrivateMessage(giftWrap, _identity);
        _processedEvents.Add(giftWrap.Id);
        _knownPeers.Add(senderPubkey);

        if (!content.StartsWith("bitchat1:")) return;

        var encoded = content[9..]; // remove "bitchat1:" prefix
        var binary = Base64Url.Decode(encoded);
        var packet = BitchatPacket.FromBinary(binary);
        if (packet == null) return;
        if (packet.Type != MessageType.NoiseEncrypted) return;

        var np = NoisePayload.Decode(packet.Payload);
        if (np == null) return;
        if (np.Type != NoisePayloadType.PrivateMessage) return;

        var pm = PrivateMessagePacket.Decode(np.Data);
        if (pm == null) return;

        var msg = new Message
        {
            Id = pm.MessageID,
            SenderPubkey = senderPubkey,
            Content = pm.Content,
            Timestamp = DateTimeOffset.UtcNow
        };

        Log($"Received from {senderPubkey[..8]}...: {pm.Content[..Math.Min(pm.Content.Length, 40)]}");
        OnMessageReceived?.Invoke(msg);
    }

    public async Task DisconnectAsync()
    {
        foreach (var relay in _relays)
            try { await relay.DisconnectAsync(); }
            catch { }
        _relays.Clear();
    }

    private void Log(string msg) => OnLog?.Invoke(DateTime.Now.ToString("HH:mm:ss"), msg);
}

public class Message
{
    public string Id { get; init; } = "";
    public string SenderPubkey { get; init; } = "";
    public string Content { get; init; } = "";
    public DateTimeOffset Timestamp { get; init; }
}
