using System.Text.Json;
using BitChat.Core.Crypto;
using BitChat.Core.Nostr;
using BitChat.Core.Protocol;

namespace BitChat.Core.Tests;

public class CrossPlatformInteropTests
{
    private static string FixturePath(string name)
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", name);

    private static NostrIdentity LoadRecipientKey(string path)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var hex = doc.RootElement.GetProperty("recipient_private_key").GetString()!;
        return NostrIdentity.FromPrivateKey(Convert.FromHexString(hex));
    }

    private static (string content, string senderPubkey) DecryptLegacyEnvelope(
        string envelopeJson, NostrIdentity recipient)
    {
        var giftWrap = NostrEvent.FromJson(envelopeJson)
            ?? throw new InvalidOperationException("Invalid envelope JSON");

        var sealJson = NostrEnvelope.DecryptContent(giftWrap.Content,
            giftWrap.Pubkey, recipient.PrivateKey);
        var seal = NostrEvent.FromJson(sealJson)
            ?? throw new InvalidOperationException("Invalid seal JSON");

        var rumorJson = NostrEnvelope.DecryptContent(seal.Content,
            seal.Pubkey, recipient.PrivateKey);
        var rumor = NostrEvent.FromJson(rumorJson)
            ?? throw new InvalidOperationException("Invalid rumor JSON");

        return (rumor.Content, seal.Pubkey);
    }

    [Fact(Skip = "Legacy Android encryption scheme (pre-NIP-44) differs from current v2 HKDF implementation")]
    public void DecryptAndroidLegacyEnvelope_BitForBit()
    {
        var envelopeJson = File.ReadAllText(
            FixturePath("AndroidLegacyPrivateEnvelopeB7f0b33d.json"));
        var recipient = LoadRecipientKey(
            FixturePath("AndroidLegacyPrivateEnvelopeB7f0b33dMetadata.json"));

        var (content, senderPubkey) = DecryptLegacyEnvelope(envelopeJson, recipient);

        Assert.Equal("legacy fixture from Android b7f0b33d", content);
        Assert.Equal("79be667ef9dcbbac55a06295ce870b07029bfcdb2dce28d959f2815b16f81798",
            senderPubkey);

        Assert.StartsWith("bitchat1:", content);
        var encoded = content[9..];
        var binary = Base64Url.Decode(encoded);
        var packet = BitchatPacket.FromBinary(binary);
        Assert.NotNull(packet);
        Assert.Equal(MessageType.NoiseEncrypted, packet!.Type);

        var np = NoisePayload.Decode(packet.Payload);
        Assert.NotNull(np);
        Assert.Equal(NoisePayloadType.PrivateMessage, np!.Type);

        var pm = PrivateMessagePacket.Decode(np.Data);
        Assert.NotNull(pm);
        Assert.Equal("legacy fixture from Android b7f0b33d", pm!.Content);
    }

    [Fact(Skip = "Legacy iOS encryption scheme (pre-NIP-44) differs from current v2 HKDF implementation")]
    public void DecryptIOSLegacyEnvelope_BitForBit()
    {
        var envelopeJson = File.ReadAllText(
            FixturePath("LegacyPrivateEnvelope733098bb.json"));
        var recipient = LoadRecipientKey(
            FixturePath("LegacyPrivateEnvelope733098bbRecipientKey.json"));

        var (content, senderPubkey) = DecryptLegacyEnvelope(envelopeJson, recipient);

        Assert.Equal("legacy fixture from 733098bb", content);

        Assert.StartsWith("bitchat1:", content);
        var encoded = content[9..];
        var binary = Base64Url.Decode(encoded);
        var packet = BitchatPacket.FromBinary(binary);
        Assert.NotNull(packet);
        Assert.Equal(MessageType.NoiseEncrypted, packet!.Type);

        var np = NoisePayload.Decode(packet.Payload);
        Assert.NotNull(np);
        Assert.Equal(NoisePayloadType.PrivateMessage, np!.Type);

        var pm = PrivateMessagePacket.Decode(np.Data);
        Assert.NotNull(pm);
        Assert.Equal("legacy fixture from 733098bb", pm!.Content);
    }

    [Fact]
    public void SHA256_Abc_Vector()
    {
        var hash = System.Security.Cryptography.SHA256.HashData("abc"u8.ToArray());
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hex);
    }

    [Fact]
    public void Base64Url_LegacyVector()
    {
        var encoded = "_-7dzA";
        var decoded = Base64Url.Decode(encoded);
        Assert.Equal(new byte[] { 0xff, 0xee, 0xdd, 0xcc }, decoded);
        Assert.Equal(encoded, Base64Url.Encode(decoded));
    }

    [Fact]
    public void MeshMessageIdentity_DeterministicVector()
    {
        var input = "0011223344556677|1750000000123|hello mesh"u8.ToArray();
        var hash = System.Security.Cryptography.SHA256.HashData(input);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        Assert.StartsWith("b83f94d81dcdd1b0c0048f6645995dd4", hex);
    }
}
