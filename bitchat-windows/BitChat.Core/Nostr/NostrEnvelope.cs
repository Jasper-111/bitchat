using System.Security.Cryptography;
using BitChat.Core.Crypto;

namespace BitChat.Core.Nostr;

public static class NostrEnvelope
{
    private const int MaxCiphertextBytes = 64 * 1024;

    public static NostrEvent CreatePrivateMessage(string content, string recipientPubkeyHex, NostrIdentity senderIdentity)
    {
        var rumor = new NostrEvent(
            senderIdentity.PublicKeyHex,
            NostrEventKind.Dm,
            [],
            content
        );

        var seal = CreateSeal(rumor, recipientPubkeyHex, senderIdentity);
        var giftWrap = CreateGiftWrap(seal, recipientPubkeyHex);

        return giftWrap;
    }

    public static (string content, string senderPubkey, int timestamp) DecryptPrivateMessage(
        NostrEvent giftWrap, NostrIdentity recipientIdentity)
    {
        if (giftWrap.Content.Length > MaxCiphertextBytes)
            throw new InvalidOperationException("Ciphertext too large");
        if (giftWrap.Kind != NostrEventKind.GiftWrap)
            throw new InvalidOperationException("Not a gift wrap");
        if (!giftWrap.VerifySignature())
            throw new InvalidOperationException("Invalid signature");

        var seal = UnwrapGiftWrap(giftWrap, recipientIdentity);
        if (seal.Kind != NostrEventKind.Seal || seal.Tags.Count != 0)
            throw new InvalidOperationException("Invalid seal");
        if (!seal.VerifySignature())
            throw new InvalidOperationException("Invalid seal signature");
        if (seal.Pubkey != seal.Pubkey)
            throw new InvalidOperationException("Seal not signed by original key");

        var rumor = OpenSeal(seal, recipientIdentity);
        if (rumor.Kind != NostrEventKind.Dm)
            throw new InvalidOperationException("Invalid rumor kind");
        if (rumor.Pubkey != seal.Pubkey)
            throw new InvalidOperationException("Sender mismatch");

        return (rumor.Content, seal.Pubkey, rumor.CreatedAt);
    }

    private static NostrEvent CreateSeal(NostrEvent rumor, string recipientPubkeyHex, NostrIdentity senderIdentity)
    {
        var rumorJson = rumor.ToJson();
        var encrypted = EncryptContent(rumorJson, recipientPubkeyHex, senderIdentity.PrivateKey);

        var seal = new NostrEvent(
            senderIdentity.PublicKeyHex,
            NostrEventKind.Seal,
            [],
            encrypted
        );
        seal.Sign(senderIdentity);
        return seal;
    }

    private static NostrEvent CreateGiftWrap(NostrEvent seal, string recipientPubkeyHex)
    {
        var wrapIdentity = NostrIdentity.Generate();
        var sealJson = seal.ToJson();
        var encrypted = EncryptContent(sealJson, recipientPubkeyHex, wrapIdentity.PrivateKey);

        var giftWrap = new NostrEvent(
            wrapIdentity.PublicKeyHex,
            NostrEventKind.GiftWrap,
            [new[] { "p", recipientPubkeyHex }],
            encrypted
        );
        giftWrap.Sign(wrapIdentity);
        return giftWrap;
    }

    private static NostrEvent UnwrapGiftWrap(NostrEvent giftWrap, NostrIdentity recipientIdentity)
    {
        var decrypted = DecryptContent(giftWrap.Content, giftWrap.Pubkey, recipientIdentity.PrivateKey);
        return NostrEvent.FromJson(decrypted)
            ?? throw new InvalidOperationException("Invalid seal JSON");
    }

    private static NostrEvent OpenSeal(NostrEvent seal, NostrIdentity recipientIdentity)
    {
        var decrypted = DecryptContent(seal.Content, seal.Pubkey, recipientIdentity.PrivateKey);
        return NostrEvent.FromJson(decrypted)
            ?? throw new InvalidOperationException("Invalid rumor JSON");
    }

    internal static string EncryptContent(string plaintext, string recipientPubkeyHex, byte[] senderPrivateKey)
    {
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var recipientPubkey = Convert.FromHexString(recipientPubkeyHex);

        var sharedSecret = Secp256k1Helper.TryEcdhWithParity(senderPrivateKey, recipientPubkey);
        var key = DeriveEnvelopeKey(sharedSecret);

        var nonce24 = new byte[24];
        RandomNumberGenerator.Fill(nonce24);

        var (ciphertext, tag) = XChaCha20Poly1305.Encrypt(plaintextBytes, key, nonce24);

        var combined = new byte[24 + ciphertext.Length + 16];
        Buffer.BlockCopy(nonce24, 0, combined, 0, 24);
        Buffer.BlockCopy(ciphertext, 0, combined, 24, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, combined, 24 + ciphertext.Length, 16);

        return "v2:" + Base64Url.Encode(combined);
    }

    internal static string DecryptContent(string ciphertext, string senderPubkeyHex, byte[] recipientPrivateKey)
    {
        if (!ciphertext.StartsWith("v2:"))
            throw new InvalidOperationException("Invalid ciphertext format");

        var encoded = ciphertext[3..];
        var data = Base64Url.Decode(encoded);

        if (data.Length < 24 + 16)
            throw new InvalidOperationException("Ciphertext too short");

        var nonce24 = data[..24];
        var tag = data[^16..];
        var ct = data[24..^16];

        var senderPubkey32 = Convert.FromHexString(senderPubkeyHex);
        var sharedSecret = Secp256k1Helper.TryEcdhWithParity(recipientPrivateKey, senderPubkey32);
        var key = DeriveEnvelopeKey(sharedSecret);

        return System.Text.Encoding.UTF8.GetString(
            XChaCha20Poly1305.Decrypt(ct, tag, key, nonce24));
    }

    private static byte[] DeriveEnvelopeKey(byte[] sharedSecret)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            sharedSecret,
            outputLength: 32,
            salt: [],
            info: System.Text.Encoding.UTF8.GetBytes("nip44-v2"));
    }
}

public static class NostrEventKind
{
    public const int Dm = 14;
    public const int Seal = 13;
    public const int GiftWrap = 1059;
    public const int EphemeralEvent = 20000;
    public const int GeohashPresence = 20001;
    public const int CourierDrop = 1401;
}
