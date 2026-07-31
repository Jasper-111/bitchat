using BitChat.Core.Crypto;
using BitChat.Core.Nostr;

namespace BitChat.Core.Protocol;

public class MessageCodec
{
    private readonly NostrIdentity _identity;

    public MessageCodec(NostrIdentity identity)
    {
        _identity = identity;
    }

    public (NostrEvent envelope, string messageId) Encode(string content, string recipientPubkeyHex, string? messageId = null)
    {
        var msgId = messageId ?? Guid.NewGuid().ToString("N")[..16];

        var pm = new PrivateMessagePacket(msgId, content);
        var noisePayload = new NoisePayload(NoisePayloadType.PrivateMessage, pm.Encode());
        var packet = BuildBitchatPacket(noisePayload.Encode(), recipientPubkeyHex);
        var binary = packet.ToBinary(true);
        if (binary == null)
            throw new InvalidOperationException("Failed to encode packet");

        var encoded = "bitchat1:" + Base64Url.Encode(binary);
        var evt = NostrEnvelope.CreatePrivateMessage(encoded, recipientPubkeyHex, _identity);

        return (evt, msgId);
    }

    public (NostrEvent envelope, string messageId) EncodeReceipt(byte receiptType, string originalMessageId, string recipientPubkeyHex)
    {
        var receiptBytes = System.Text.Encoding.UTF8.GetBytes(originalMessageId);
        var noisePayload = new NoisePayload(receiptType, receiptBytes);
        var packet = BuildBitchatPacket(noisePayload.Encode(), recipientPubkeyHex);
        var binary = packet.ToBinary(true);
        if (binary == null)
            throw new InvalidOperationException("Failed to encode receipt packet");

        var encoded = "bitchat1:" + Base64Url.Encode(binary);
        var evt = NostrEnvelope.CreatePrivateMessage(encoded, recipientPubkeyHex, _identity);

        return (evt, originalMessageId);
    }

    public DecodedMessage? Decode(NostrEvent giftWrap, NostrIdentity recipientIdentity)
    {
        string content;
        string senderPubkey;
        int timestamp;
        try
        {
            (content, senderPubkey, timestamp) = NostrEnvelope.DecryptPrivateMessage(giftWrap, recipientIdentity);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }

        if (!content.StartsWith("bitchat1:"))
            return null;

        var encoded = content[9..];
        var binary = Base64Url.Decode(encoded);
        var packet = BitchatPacket.FromBinary(binary);
        if (packet == null || packet.Type != MessageType.NoiseEncrypted)
            return null;

        var np = NoisePayload.Decode(packet.Payload);
        if (np == null)
            return null;

        if (np.Type == NoisePayloadType.PrivateMessage)
        {
            var pm = PrivateMessagePacket.Decode(np.Data);
            if (pm == null) return null;
            return new DecodedMessage
            {
                Type = np.Type,
                MessageID = pm.MessageID,
                Content = pm.Content,
                SenderPubkey = senderPubkey,
                Timestamp = timestamp
            };
        }

        return new DecodedMessage
        {
            Type = np.Type,
            MessageID = System.Text.Encoding.UTF8.GetString(np.Data),
            Content = np.Type switch
            {
                NoisePayloadType.Delivered => "delivered",
                NoisePayloadType.ReadReceipt => "read",
                _ => ""
            },
            SenderPubkey = senderPubkey,
            Timestamp = timestamp
        };
    }

    private BitchatPacket BuildBitchatPacket(byte[] noiseData, string recipientPubkeyHex)
    {
        var senderHex = _identity.PublicKeyHex;
        return new BitchatPacket
        {
            Version = 1,
            Type = MessageType.NoiseEncrypted,
            TTL = 7,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SenderID = Convert.FromHexString(senderHex[..16]),
            RecipientID = Convert.FromHexString(recipientPubkeyHex[..16]),
            Payload = noiseData
        };
    }
}

public sealed class DecodedMessage
{
    public byte Type { get; init; }
    public string MessageID { get; init; } = "";
    public string Content { get; init; } = "";
    public string SenderPubkey { get; init; } = "";
    public int Timestamp { get; init; }
}
