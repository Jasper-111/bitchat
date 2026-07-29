using BitChat.Core.Crypto;
using BitChat.Core.Nostr;
using BitChat.Core.Protocol;

namespace BitChat.Core.Services;

public class ChatEngine
{
    private NostrIdentity _identity;
    private readonly List<INostrRelay> _relays = [];
    private readonly HashSet<string> _processedEvents = [];
    private readonly HashSet<string> _knownPeers = [];

    public NostrIdentity Identity => _identity;
    public IReadOnlySet<string> KnownPeers => _knownPeers;

    public event Action<NostrIdentity>? OnConnected;
    public event Action<Message>? OnMessageReceived;
    public event Action<string, string, string>? OnReceiptReceived;
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
                var relay = NostrRelayClient.Create(new Uri(url));
                await ConnectToRelay(relay);
            }
            catch (Exception ex)
            {
                Log($"Failed to connect to {url}: {ex.Message}");
            }
        }

        if (_relays.Count > 0) OnConnected?.Invoke(_identity);
    }

    public async Task ConnectToRelay(INostrRelay relay)
    {
        var host = relay.Url.Host;
        relay.OnNotice += msg => Log($"[{host}] NOTICE: {msg}");
        await relay.ConnectAsync();
        _relays.Add(relay);
        Log($"Connected to {host}");

        var since = (int)(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds());
        var filter = NostrFilter.GiftWrapsFor(_identity.PublicKeyHex, since);
        await relay.Subscribe("dm-" + Guid.NewGuid().ToString("N")[..6], filter, OnGiftWrapReceived);
        Log($"Subscribed to DMs on {host}");

        if (_relays.Count == 1) OnConnected?.Invoke(_identity);
    }

    public async Task SendMessageAsync(string recipientPubkeyHex, string text)
    {
        var messageId = Guid.NewGuid().ToString("N")[..16];
        await SendEncodedAsync(recipientPubkeyHex, text, NoisePayloadType.PrivateMessage, messageId);
    }

    public async Task SendReadReceipt(string recipientPubkeyHex, string originalMessageId)
    {
        var receiptBytes = System.Text.Encoding.UTF8.GetBytes(originalMessageId);
        var noisePayload = new NoisePayload(NoisePayloadType.ReadReceipt, receiptBytes);
        var noiseData = noisePayload.Encode();

        var senderHex = _identity.PublicKeyHex;
        var senderID = Convert.FromHexString(senderHex[..16]);
        var recipientID = Convert.FromHexString(recipientPubkeyHex[..16]);

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

        var encoded = "bitchat1:" + Base64Url.Encode(binaryData);
        var evt = NostrEnvelope.CreatePrivateMessage(encoded, recipientPubkeyHex, _identity);

        foreach (var relay in _relays)
        {
            try { await relay.PublishEvent(evt); }
            catch { }
        }
    }

    public async Task SendDeliveredReceipt(string recipientPubkeyHex, string originalMessageId)
    {
        var receiptBytes = System.Text.Encoding.UTF8.GetBytes(originalMessageId);
        var noisePayload = new NoisePayload(NoisePayloadType.Delivered, receiptBytes);
        var noiseData = noisePayload.Encode();

        var senderHex = _identity.PublicKeyHex;
        var senderID = Convert.FromHexString(senderHex[..16]);
        var recipientID = Convert.FromHexString(recipientPubkeyHex[..16]);

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

        var encoded = "bitchat1:" + Base64Url.Encode(binaryData);
        var evt = NostrEnvelope.CreatePrivateMessage(encoded, recipientPubkeyHex, _identity);

        foreach (var relay in _relays)
        {
            try { await relay.PublishEvent(evt); }
            catch { }
        }
    }

    private async Task SendEncodedAsync(string recipientPubkeyHex, string text, byte noiseType, string messageId)
    {
        byte[] pmData;
        if (noiseType == NoisePayloadType.PrivateMessage)
        {
            var pm = new PrivateMessagePacket(messageId, text);
            pmData = pm.Encode();
        }
        else
        {
            pmData = System.Text.Encoding.UTF8.GetBytes(text);
        }

        var noisePayload = new NoisePayload(noiseType, pmData);
        var noiseData = noisePayload.Encode();

        var senderHex = _identity.PublicKeyHex;
        var senderID = Convert.FromHexString(senderHex[..16]);
        var recipientID = Convert.FromHexString(recipientPubkeyHex[..16]);

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

        var encoded = "bitchat1:" + Base64Url.Encode(binaryData);
        var evt = NostrEnvelope.CreatePrivateMessage(encoded, recipientPubkeyHex, _identity);

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

        var encoded = content[9..];
        var binary = Base64Url.Decode(encoded);
        var packet = BitchatPacket.FromBinary(binary);
        if (packet == null) return;
        if (packet.Type != MessageType.NoiseEncrypted) return;

        var np = NoisePayload.Decode(packet.Payload);
        if (np == null) return;

        switch (np.Type)
        {
            case NoisePayloadType.PrivateMessage:
                var pm = PrivateMessagePacket.Decode(np.Data);
                if (pm == null) return;

                var msg = new Message
                {
                    Id = pm.MessageID,
                    SenderPubkey = senderPubkey,
                    Content = pm.Content,
                    Timestamp = DateTimeOffset.Now
                };

                Log($"Received from {senderPubkey[..8]}...: {pm.Content[..Math.Min(pm.Content.Length, 40)]}");
                OnMessageReceived?.Invoke(msg);

                await SendDeliveredReceipt(senderPubkey, pm.MessageID);
                break;

            case NoisePayloadType.Delivered:
                var deliveredMsgId = System.Text.Encoding.UTF8.GetString(np.Data);
                Log($"Delivered receipt for {deliveredMsgId[..Math.Min(deliveredMsgId.Length, 16)]}");
                OnReceiptReceived?.Invoke(senderPubkey, deliveredMsgId, "delivered");
                break;

            case NoisePayloadType.ReadReceipt:
                var readMsgId = System.Text.Encoding.UTF8.GetString(np.Data);
                Log($"Read receipt for {readMsgId[..Math.Min(readMsgId.Length, 16)]}");
                OnReceiptReceived?.Invoke(senderPubkey, readMsgId, "read");
                break;
        }
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
    public string? ReceiptType { get; init; }
}
