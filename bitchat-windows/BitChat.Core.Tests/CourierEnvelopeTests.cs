using BitChat.Core.Services.Courier;

namespace BitChat.Core.Tests;

public class CourierEnvelopeTests
{
    [Fact]
    public void Envelope_Roundtrip()
    {
        var recipientTag = new byte[16];
        new Random(42).NextBytes(recipientTag);
        var expiry = (ulong)DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var ciphertext = "encrypted message content"u8.ToArray();
        var copies = (byte)3;
        uint? prekeyID = 42;

        var original = new CourierEnvelope(recipientTag, expiry, ciphertext, copies, prekeyID);
        var encoded = original.Encode();

        var decoded = CourierEnvelope.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal(recipientTag, decoded!.RecipientTag);
        Assert.Equal(expiry, decoded.Expiry);
        Assert.Equal(ciphertext, decoded.Ciphertext);
        Assert.Equal(copies, decoded.Copies);
        Assert.Equal(prekeyID, decoded.PrekeyID);
    }

    [Fact]
    public void Envelope_Roundtrip_NoPrekey()
    {
        var recipientTag = new byte[16];
        new Random(99).NextBytes(recipientTag);
        var expiry = (ulong)DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
        var ciphertext = "test"u8.ToArray();

        var original = new CourierEnvelope(recipientTag, expiry, ciphertext);
        var encoded = original.Encode();

        var decoded = CourierEnvelope.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Null(decoded!.PrekeyID);
        Assert.Equal((byte)1, decoded.Copies);
    }

    [Fact]
    public void DeriveRecipientTag_Deterministic()
    {
        var staticKey = new byte[32];
        new Random(1).NextBytes(staticKey);

        var epochDay = 20000L;
        var tag1 = CourierEnvelope.DeriveRecipientTag(staticKey, epochDay);
        var tag2 = CourierEnvelope.DeriveRecipientTag(staticKey, epochDay);

        Assert.Equal(16, tag1.Length);
        Assert.Equal(tag1, tag2);
    }

    [Fact]
    public void DeriveRecipientTag_DifferentDays()
    {
        var staticKey = new byte[32];
        new Random(2).NextBytes(staticKey);

        var tag1 = CourierEnvelope.DeriveRecipientTag(staticKey, 20000);
        var tag2 = CourierEnvelope.DeriveRecipientTag(staticKey, 20001);

        Assert.NotEqual(tag1, tag2);
    }

    [Fact]
    public void CandidateTags_ThreeDays()
    {
        var staticKey = new byte[32];
        new Random(3).NextBytes(staticKey);

        var tags = CourierEnvelope.CandidateTags(staticKey, 20000).ToList();
        Assert.Equal(3, tags.Count);
        Assert.Contains(CourierEnvelope.DeriveRecipientTag(staticKey, 19999), tags);
        Assert.Contains(CourierEnvelope.DeriveRecipientTag(staticKey, 20000), tags);
        Assert.Contains(CourierEnvelope.DeriveRecipientTag(staticKey, 20001), tags);
    }

    [Fact]
    public void Envelope_EmptyPayload_Decodes()
    {
        var recipientTag = new byte[16];
        var expiry = (ulong)DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds();
        var ciphertext = Array.Empty<byte>();

        var original = new CourierEnvelope(recipientTag, expiry, ciphertext);
        var encoded = original.Encode();

        var decoded = CourierEnvelope.Decode(encoded);
        Assert.NotNull(decoded);
        Assert.Empty(decoded!.Ciphertext);
    }

    [Fact]
    public void CourierStore_DepositAndFetch()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), "bitchat-test-courier-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new CourierStore(storeDir);
            var staticKey = new byte[32];
            new Random(42).NextBytes(staticKey);

            var epochDay = CourierEnvelope.CurrentEpochDay();
            var tag = CourierEnvelope.DeriveRecipientTag(staticKey, epochDay);
            var expiry = (ulong)DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
            var ciphertext = "test message"u8.ToArray();
            var messageId = "feedbeeffeedbeef"u8.ToArray();

            var envelope = new CourierEnvelope(tag, expiry, ciphertext);
            store.Deposit(envelope, messageId);

            var fetched = store.Fetch(staticKey);
            Assert.Single(fetched);
            Assert.Equal(ciphertext, fetched[0].Envelope.Ciphertext);
            Assert.Equal(messageId, fetched[0].MessageId);
        }
        finally
        {
            if (Directory.Exists(storeDir))
                Directory.Delete(storeDir, true);
        }
    }

    [Fact]
    public void CourierStore_WrongKey_ReturnsEmpty()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), "bitchat-test-courier2-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new CourierStore(storeDir);
            var staticKey = new byte[32];
            new Random(1).NextBytes(staticKey);

            var epochDay = CourierEnvelope.CurrentEpochDay();
            var tag = CourierEnvelope.DeriveRecipientTag(staticKey, epochDay);
            var envelope = new CourierEnvelope(tag,
                (ulong)DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                "test"u8.ToArray());
            store.Deposit(envelope, "abcdef0123456789"u8.ToArray());

            var wrongKey = new byte[32];
            new Random(99).NextBytes(wrongKey);

            var fetched = store.Fetch(wrongKey);
            Assert.Empty(fetched);
        }
        finally
        {
            if (Directory.Exists(storeDir))
                Directory.Delete(storeDir, true);
        }
    }

    [Fact]
    public void CourierStore_DifferentDayKeysWork()
    {
        var storeDir = Path.Combine(Path.GetTempPath(), "bitchat-test-courier3-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new CourierStore(storeDir);
            var staticKey = new byte[32];
            new Random(5).NextBytes(staticKey);

            var epochDay = CourierEnvelope.CurrentEpochDay();
            var yesterdayTag = CourierEnvelope.DeriveRecipientTag(staticKey, epochDay - 1);
            var envelope = new CourierEnvelope(yesterdayTag,
                (ulong)DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                "yesterday"u8.ToArray());
            store.Deposit(envelope, "yesterday01abcdef"u8.ToArray());

            var fetched = store.Fetch(staticKey);
            Assert.Single(fetched);
            Assert.Equal("yesterday"u8.ToArray(), fetched[0].Envelope.Ciphertext);
        }
        finally
        {
            if (Directory.Exists(storeDir))
                Directory.Delete(storeDir, true);
        }
    }

    [Fact]
    public void Envelope_ExpiryValidation()
    {
        var recipientTag = new byte[16];
        var tooFar = (ulong)DateTimeOffset.UtcNow.AddHours(25).ToUnixTimeMilliseconds();
        Assert.Throws<ArgumentException>(() =>
            new CourierEnvelope(recipientTag, tooFar, "test"u8.ToArray()));
    }

    [Fact]
    public void Envelope_MaxCiphertextValidation()
    {
        var recipientTag = new byte[16];
        var expiry = (ulong)DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        var tooLarge = new byte[17 * 1024];
        Assert.Throws<ArgumentException>(() =>
            new CourierEnvelope(recipientTag, expiry, tooLarge));
    }
}
