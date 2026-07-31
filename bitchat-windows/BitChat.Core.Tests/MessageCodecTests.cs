using BitChat.Core.Nostr;
using BitChat.Core.Protocol;

namespace BitChat.Core.Tests;

public class MessageCodecTests
{
    [Fact]
    public void Encode_Decode_Roundtrip()
    {
        var identity = NostrIdentity.Generate();
        var codec = new MessageCodec(identity);

        var (envelope, msgId) = codec.Encode("Hello World", identity.PublicKeyHex);
        Assert.NotNull(envelope);
        Assert.NotEmpty(msgId);

        var decoded = codec.Decode(envelope, identity);
        Assert.NotNull(decoded);
        Assert.Equal("Hello World", decoded!.Content);
        Assert.Equal(msgId, decoded.MessageID);
        Assert.Equal(identity.PublicKeyHex, decoded.SenderPubkey);
        Assert.Equal(NoisePayloadType.PrivateMessage, decoded.Type);
    }

    [Fact]
    public void Encode_Decode_SelfMessage()
    {
        var identity = NostrIdentity.Generate();
        var codec = new MessageCodec(identity);

        var (envelope, _) = codec.Encode("self-test", identity.PublicKeyHex);
        var decoded = codec.Decode(envelope, identity);

        Assert.NotNull(decoded);
        Assert.Equal("self-test", decoded!.Content);
    }

    [Fact]
    public void Encode_Decode_CrossIdentity()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var aliceCodec = new MessageCodec(alice);
        var bobCodec = new MessageCodec(bob);

        var (envelope, msgId) = aliceCodec.Encode("Hello from Alice", bob.PublicKeyHex);
        var decoded = bobCodec.Decode(envelope, bob);

        Assert.NotNull(decoded);
        Assert.Equal("Hello from Alice", decoded!.Content);
        Assert.Equal(msgId, decoded.MessageID);
        Assert.Equal(alice.PublicKeyHex, decoded.SenderPubkey);
    }

    [Fact]
    public void Decode_NonGiftWrap_ReturnsNull()
    {
        var identity = NostrIdentity.Generate();
        var codec = new MessageCodec(identity);

        var nonGiftWrap = new NostrEvent(
            identity.PublicKeyHex,
            1,
            [],
            "not a gift wrap"
        );
        nonGiftWrap.Sign(identity);

        var decoded = codec.Decode(nonGiftWrap, identity);
        Assert.Null(decoded);
    }

    [Fact]
    public void EncodeReceipt_Decode_ProducesDataReceived()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var aliceCodec = new MessageCodec(alice);
        var bobCodec = new MessageCodec(bob);

        var (envelope, originalId) = aliceCodec.EncodeReceipt(
            NoisePayloadType.Delivered, "test-message-id-12345", bob.PublicKeyHex);

        var decoded = bobCodec.Decode(envelope, bob);
        Assert.NotNull(decoded);
        Assert.Equal(NoisePayloadType.Delivered, decoded!.Type);
        Assert.Equal("test-message-id-12345", decoded.MessageID);
        Assert.Equal("delivered", decoded.Content);
    }

    [Fact]
    public void EncodeReceipt_ReadReceipt()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var aliceCodec = new MessageCodec(alice);
        var bobCodec = new MessageCodec(bob);

        var (envelope, _) = aliceCodec.EncodeReceipt(
            NoisePayloadType.ReadReceipt, "msg-read-001", bob.PublicKeyHex);

        var decoded = bobCodec.Decode(envelope, bob);
        Assert.NotNull(decoded);
        Assert.Equal(NoisePayloadType.ReadReceipt, decoded!.Type);
        Assert.Equal("read", decoded.Content);
    }

    [Fact]
    public void Decode_WrongRecipient_ThrowsException()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var carol = NostrIdentity.Generate();

        var aliceCodec = new MessageCodec(alice);

        var (envelope, _) = aliceCodec.Encode("Hello Bob", bob.PublicKeyHex);

        Assert.Throws<InvalidOperationException>(() =>
            aliceCodec.Decode(envelope, carol));
    }

    [Fact]
    public void Decode_TamperedEnvelope_ThrowsException()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var aliceCodec = new MessageCodec(alice);
        var bobCodec = new MessageCodec(bob);

        var (envelope, _) = aliceCodec.Encode("tamper test", bob.PublicKeyHex);

        envelope.Content = "v2:AAAA" + envelope.Content[7..];

        Assert.Throws<InvalidOperationException>(() =>
            bobCodec.Decode(envelope, bob));
    }

    [Fact]
    public void Encode_ProducesDeterministicMessageId()
    {
        var identity = NostrIdentity.Generate();
        var codec = new MessageCodec(identity);

        var (envelope, msgId) = codec.Encode("test", identity.PublicKeyHex, "fixed-id-12345678");
        Assert.Equal("fixed-id-12345678", msgId);

        var decoded = codec.Decode(envelope, identity);
        Assert.NotNull(decoded);
        Assert.Equal("fixed-id-12345678", decoded!.MessageID);
    }

    [Fact]
    public void Encode_UnicodeContent_Roundtrip()
    {
        var identity = NostrIdentity.Generate();
        var codec = new MessageCodec(identity);

        var unicodeText = "Hello 世界 \u2764\ufe0f \u00e9\u00e8\u00fc";
        var (envelope, _) = codec.Encode(unicodeText, identity.PublicKeyHex);
        var decoded = codec.Decode(envelope, identity);

        Assert.NotNull(decoded);
        Assert.Equal(unicodeText, decoded!.Content);
    }

    [Fact]
    public void Encode_EmptyContent_Roundtrip()
    {
        var identity = NostrIdentity.Generate();
        var codec = new MessageCodec(identity);

        var (envelope, _) = codec.Encode("", identity.PublicKeyHex);
        var decoded = codec.Decode(envelope, identity);

        Assert.NotNull(decoded);
        Assert.Equal("", decoded!.Content);
    }
}
