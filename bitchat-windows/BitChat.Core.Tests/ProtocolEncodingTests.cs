using BitChat.Core.Crypto;
using BitChat.Core.Nostr;
using BitChat.Core.Protocol;

namespace BitChat.Core.Tests;

public class ProtocolEncodingTests
{
    [Fact]
    public void BitchatPacket_Roundtrip()
    {
        var senderId = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var recipientId = new byte[] { 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18 };
        var payload = "Hello, BitChat!"u8.ToArray();

        var original = new BitchatPacket
        {
            Version = 1,
            Type = MessageType.NoiseEncrypted,
            TTL = 7,
            Timestamp = 1234567890UL,
            SenderID = senderId,
            RecipientID = recipientId,
            Payload = payload
        };

        var binary = original.ToBinary(false);
        Assert.NotNull(binary);

        var decoded = BitchatPacket.FromBinary(binary!);
        Assert.NotNull(decoded);
        Assert.Equal(original.Version, decoded!.Version);
        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.TTL, decoded.TTL);
        Assert.Equal(original.Timestamp, decoded.Timestamp);
        Assert.Equal(original.SenderID, decoded.SenderID);
        Assert.Equal(original.RecipientID, decoded.RecipientID);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact]
    public void BitchatPacket_Roundtrip_WithPadding()
    {
        var senderId = Convert.FromHexString("aabbccddeeff0011");
        var payload = "test"u8.ToArray();

        var original = new BitchatPacket
        {
            Version = 1,
            Type = MessageType.NoiseEncrypted,
            TTL = 7,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SenderID = senderId,
            Payload = payload
        };

        var binary = original.ToBinary(true);
        Assert.NotNull(binary);
        Assert.True(binary!.Length >= 256);

        var decoded = BitchatPacket.FromBinary(binary);
        Assert.NotNull(decoded);
        Assert.Equal(payload, decoded!.Payload);
    }

    [Fact]
    public void PrivateMessagePacket_Roundtrip()
    {
        var original = new PrivateMessagePacket("msg123", "Hello world");
        var encoded = original.Encode();

        var decoded = PrivateMessagePacket.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal("msg123", decoded!.MessageID);
        Assert.Equal("Hello world", decoded.Content);
    }

    [Fact]
    public void PrivateMessagePacket_EmptyContent()
    {
        var original = new PrivateMessagePacket("empty", "");
        var encoded = original.Encode();

        var decoded = PrivateMessagePacket.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal("empty", decoded!.MessageID);
        Assert.Equal("", decoded.Content);
    }

    [Fact]
    public void NoisePayload_Roundtrip_PrivateMessage()
    {
        var pm = new PrivateMessagePacket("id1", "test");
        var pmData = pm.Encode();

        var original = new NoisePayload(NoisePayloadType.PrivateMessage, pmData);
        var encoded = original.Encode();

        var decoded = NoisePayload.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal(NoisePayloadType.PrivateMessage, decoded!.Type);

        var inner = PrivateMessagePacket.Decode(decoded.Data);
        Assert.NotNull(inner);
        Assert.Equal("id1", inner!.MessageID);
        Assert.Equal("test", inner.Content);
    }

    [Fact]
    public void NoisePayload_Roundtrip_Delivered()
    {
        var receiptData = System.Text.Encoding.UTF8.GetBytes("msg-abc-123");
        var original = new NoisePayload(NoisePayloadType.Delivered, receiptData);
        var encoded = original.Encode();

        var decoded = NoisePayload.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal(NoisePayloadType.Delivered, decoded!.Type);
        Assert.Equal("msg-abc-123", System.Text.Encoding.UTF8.GetString(decoded.Data));
    }

    [Fact]
    public void NoisePayload_Roundtrip_ReadReceipt()
    {
        var receiptData = System.Text.Encoding.UTF8.GetBytes("msg-xyz-789");
        var original = new NoisePayload(NoisePayloadType.ReadReceipt, receiptData);
        var encoded = original.Encode();

        var decoded = NoisePayload.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal(NoisePayloadType.ReadReceipt, decoded!.Type);
        Assert.Equal("msg-xyz-789", System.Text.Encoding.UTF8.GetString(decoded.Data));
    }

    [Fact]
    public void FullProtocolPipeline_Roundtrip()
    {
        var senderHex = "aabbccddeeff0011aabbccddeeff0022aabbccdd";
        var recipientHex = "1122334455667788112233445566778811223344";
        var messageId = Guid.NewGuid().ToString("N")[..16];
        var text = "This is a multi-layer protocol test";

        var pm = new PrivateMessagePacket(messageId, text);
        var pmData = pm.Encode();

        var np = new NoisePayload(NoisePayloadType.PrivateMessage, pmData);
        var npData = np.Encode();

        var packet = new BitchatPacket
        {
            Version = 1,
            Type = MessageType.NoiseEncrypted,
            TTL = 7,
            Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SenderID = Convert.FromHexString(senderHex[..16]),
            RecipientID = Convert.FromHexString(recipientHex[..16]),
            Payload = npData
        };
        var binary = packet.ToBinary(false);
        Assert.NotNull(binary);

        var encoded = "bitchat1:" + Base64Url.Encode(binary!);
        Assert.StartsWith("bitchat1:", encoded);

        var decoded = Base64Url.Decode(encoded[9..]);
        var decodedPacket = BitchatPacket.FromBinary(decoded);
        Assert.NotNull(decodedPacket);
        Assert.Equal(MessageType.NoiseEncrypted, decodedPacket!.Type);

        var decodedNp = NoisePayload.Decode(decodedPacket.Payload);
        Assert.NotNull(decodedNp);
        Assert.Equal(NoisePayloadType.PrivateMessage, decodedNp!.Type);

        var decodedPm = PrivateMessagePacket.Decode(decodedNp.Data);
        Assert.NotNull(decodedPm);
        Assert.Equal(messageId, decodedPm!.MessageID);
        Assert.Equal(text, decodedPm.Content);
    }

    [Fact]
    public void MessagePadding_Deterministic()
    {
        var data = "hello"u8.ToArray();
        var bs = MessagePadding.OptimalBlockSize(data.Length);
        var padded1 = MessagePadding.Pad(data, bs);
        var padded2 = MessagePadding.Pad(data, bs);
        Assert.Equal(padded1.Length, padded2.Length);
    }

    [Fact]
    public void MessagePadding_Roundtrip()
    {
        var testCases = new[]
        {
            ""u8.ToArray(),
            "a"u8.ToArray(),
            "hello world"u8.ToArray(),
            new string('x', 200).Select(c => (byte)c).ToArray(),
            new string('y', 500).Select(c => (byte)c).ToArray()
        };

        foreach (var data in testCases)
        {
            var bs = MessagePadding.OptimalBlockSize(data.Length + 1);
            var padded = MessagePadding.Pad(data, bs);
            Assert.True(padded.Length >= data.Length);

            var unpadded = MessagePadding.Unpad(padded);
            Assert.Equal(data, unpadded);
        }
    }

    [Fact]
    public void Base64Url_Roundtrip()
    {
        var testCases = new[]
        {
            Array.Empty<byte>(),
            new byte[] { 0x00 },
            "hello world"u8.ToArray(),
            new byte[256],
            Enumerable.Range(0, 1000).Select(i => (byte)(i % 256)).ToArray()
        };

        foreach (var data in testCases)
        {
            var encoded = Base64Url.Encode(data);
            var decoded = Base64Url.Decode(encoded);
            Assert.Equal(data, decoded);
        }
    }
}
