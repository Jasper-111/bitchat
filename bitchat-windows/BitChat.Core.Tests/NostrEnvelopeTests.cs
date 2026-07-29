using System.Security.Cryptography;
using BitChat.Core.Crypto;
using BitChat.Core.Nostr;

namespace BitChat.Core.Tests;

public class NostrEnvelopeTests
{
    // ═══════════════════════════════════════════════════════════
    //  Verifies the same key derivation used by EncryptContent / DecryptContent
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void EcdhWithParity_IsCommutative()
    {
        var (aPriv, aPub) = Secp256k1Helper.GenerateKeyPair();
        var (bPriv, bPub) = Secp256k1Helper.GenerateKeyPair();

        var sharedAB = Secp256k1Helper.TryEcdhWithParity(aPriv, bPub);
        var sharedBA = Secp256k1Helper.TryEcdhWithParity(bPriv, aPub);

        Assert.Equal(32, sharedAB.Length);
        Assert.Equal(32, sharedBA.Length);
        Assert.Equal(sharedAB, sharedBA);
    }

    // ═══════════════════════════════════════════════════════════
    //  Triple-wrap: CreatePrivateMessage → DecryptPrivateMessage
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void TripleWrap_BasicRoundtrip()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var content = "Hello from Alice";
        var giftWrap = NostrEnvelope.CreatePrivateMessage(content, bob.PublicKeyHex, alice);

        Assert.Equal(NostrEventKind.GiftWrap, giftWrap.Kind);
        Assert.Contains(new[] { "p", bob.PublicKeyHex }, giftWrap.Tags, new StringArrayComparer());
        Assert.True(giftWrap.VerifySignature());

        var (decrypted, senderPubkey, _) = NostrEnvelope.DecryptPrivateMessage(giftWrap, bob);

        Assert.Equal(content, decrypted);
        Assert.Equal(alice.PublicKeyHex, senderPubkey);
    }

    [Fact]
    public void TripleWrap_EmptyContent()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var giftWrap = NostrEnvelope.CreatePrivateMessage("", bob.PublicKeyHex, alice);
        var (decrypted, sender, _) = NostrEnvelope.DecryptPrivateMessage(giftWrap, bob);

        Assert.Equal("", decrypted);
        Assert.Equal(alice.PublicKeyHex, sender);
    }

    [Fact]
    public void TripleWrap_UnicodeContent()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var content = "Hello 世界 🌍 — emoji test \u2764\ufe0f \u00e9\u00e8\u00fc\u00f1";
        var giftWrap = NostrEnvelope.CreatePrivateMessage(content, bob.PublicKeyHex, alice);
        var (decrypted, _, _) = NostrEnvelope.DecryptPrivateMessage(giftWrap, bob);

        Assert.Equal(content, decrypted);
    }

    [Fact]
    public void TripleWrap_LongContent()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var content = new string('x', 16_384);
        var giftWrap = NostrEnvelope.CreatePrivateMessage(content, bob.PublicKeyHex, alice);
        var (decrypted, _, _) = NostrEnvelope.DecryptPrivateMessage(giftWrap, bob);

        Assert.Equal(content, decrypted);
    }

    [Fact]
    public void TripleWrap_DeterministicSameInput_DifferentCiphertext()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var wrap1 = NostrEnvelope.CreatePrivateMessage("hello", bob.PublicKeyHex, alice);
        var wrap2 = NostrEnvelope.CreatePrivateMessage("hello", bob.PublicKeyHex, alice);

        // Different random nonce each time → different ciphertexts
        Assert.NotEqual(wrap1.Content, wrap2.Content);
        Assert.NotEqual(wrap1.Pubkey, wrap2.Pubkey); // random ephemeral pubkey

        // Both should decrypt to same content
        var (d1, _, _) = NostrEnvelope.DecryptPrivateMessage(wrap1, bob);
        var (d2, _, _) = NostrEnvelope.DecryptPrivateMessage(wrap2, bob);
        Assert.Equal(d1, d2);
    }

    // ═══════════════════════════════════════════════════════════
    //  Layer structure: rumor(14) → seal(13) → giftwrap(1059)
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void GiftWrap_HasCorrectKindAndTag()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var wrap = NostrEnvelope.CreatePrivateMessage("test", bob.PublicKeyHex, alice);

        Assert.Equal(NostrEventKind.GiftWrap, wrap.Kind);
        Assert.Single(wrap.Tags);
        Assert.Equal("p", wrap.Tags[0][0]);
        Assert.Equal(bob.PublicKeyHex, wrap.Tags[0][1]);
    }

    [Fact]
    public void Seal_IsCreatedWithSenderPubkey()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var wrap = NostrEnvelope.CreatePrivateMessage("test", bob.PublicKeyHex, alice);

        var seal = UnwrapGiftWrapLayer(wrap, bob);
        Assert.Equal(NostrEventKind.Seal, seal.Kind);
        Assert.Equal(alice.PublicKeyHex, seal.Pubkey);
        Assert.True(seal.VerifySignature());
    }

    [Fact]
    public void Rumor_MatchesOriginalContent()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var content = "This is the rumor content";
        var wrap = NostrEnvelope.CreatePrivateMessage(content, bob.PublicKeyHex, alice);

        var seal = UnwrapGiftWrapLayer(wrap, bob);
        var decryptedRumor = NostrEnvelope.DecryptContent(seal.Content, seal.Pubkey, bob.PrivateKey);
        var rumor = NostrEvent.FromJson(decryptedRumor);

        Assert.NotNull(rumor);
        Assert.Equal(NostrEventKind.Dm, rumor!.Kind);
        Assert.Equal(content, rumor.Content);
        Assert.Equal(alice.PublicKeyHex, rumor.Pubkey);
    }

    // ═══════════════════════════════════════════════════════════
    //  Error cases: wrong recipient, tampered ciphertext / signature
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Decrypt_WrongRecipient_Throws()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var eve = NostrIdentity.Generate();

        var wrap = NostrEnvelope.CreatePrivateMessage("secret", bob.PublicKeyHex, alice);

        Assert.ThrowsAny<Exception>(() =>
            NostrEnvelope.DecryptPrivateMessage(wrap, eve));
    }

    [Fact]
    public void Decrypt_TamperedGiftWrapContent_Throws()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var wrap = NostrEnvelope.CreatePrivateMessage("test", bob.PublicKeyHex, alice);

        // Flip a bit in the ciphertext (after v2: prefix)
        var parts = wrap.Content.Split(':', 2);
        var data = Base64Url.Decode(parts[1]);
        data[24] ^= 0xFF; // corrupt first byte of ciphertext (post-nonce)
        wrap.Content = "v2:" + Base64Url.Encode(data);

        Assert.ThrowsAny<Exception>(() =>
            NostrEnvelope.DecryptPrivateMessage(wrap, bob));
    }

    [Fact]
    public void Decrypt_WrongKind_Throws()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var giftWrap = NostrEnvelope.CreatePrivateMessage("test", bob.PublicKeyHex, alice);

        giftWrap.Kind = NostrEventKind.Dm; // tamper kind from 1059 → 14

        Assert.ThrowsAny<Exception>(() =>
            NostrEnvelope.DecryptPrivateMessage(giftWrap, bob));
    }

    [Fact]
    public void Decrypt_TamperedGiftWrapSignature_Throws()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var wrap = NostrEnvelope.CreatePrivateMessage("test", bob.PublicKeyHex, alice);

        // Flip a bit in the signature
        var sig = Convert.FromHexString(wrap.Sig!);
        sig[0] ^= 1;
        wrap.Sig = Convert.ToHexString(sig).ToLowerInvariant();

        Assert.ThrowsAny<Exception>(() =>
            NostrEnvelope.DecryptPrivateMessage(wrap, bob));
    }

    [Fact]
    public void Decrypt_ContentTooLarge_Throws()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var wrap = NostrEnvelope.CreatePrivateMessage("test", bob.PublicKeyHex, alice);

        // Construct a huge ciphertext (over 64KB)
        wrap.Content = "v2:" + Base64Url.Encode(new byte[65_536]);

        Assert.ThrowsAny<Exception>(() =>
            NostrEnvelope.DecryptPrivateMessage(wrap, bob));
    }

    // ═══════════════════════════════════════════════════════════
    //  Sender mismatch detection
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Decrypt_SenderMismatchInSeal_Throws()
    {
        var alice = NostrIdentity.Generate();
        var eve = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();

        var rumor = new NostrEvent(eve.PublicKeyHex, NostrEventKind.Dm, [], "hijacked");
        var sealContent = NostrEnvelope.EncryptContent(rumor.ToJson(), bob.PublicKeyHex, alice.PrivateKey);
        var seal = new NostrEvent(alice.PublicKeyHex, NostrEventKind.Seal, [], sealContent);
        seal.Sign(alice);

        var wrapId = NostrIdentity.Generate();
        var wrapContent = NostrEnvelope.EncryptContent(seal.ToJson(), bob.PublicKeyHex, wrapId.PrivateKey);
        var giftWrap = new NostrEvent(wrapId.PublicKeyHex, NostrEventKind.GiftWrap,
            [new[] { "p", bob.PublicKeyHex }], wrapContent);
        giftWrap.Sign(wrapId);

        Assert.ThrowsAny<Exception>(() =>
            NostrEnvelope.DecryptPrivateMessage(giftWrap, bob));
    }

    // ═══════════════════════════════════════════════════════════
    //  Multiple identity pairs — cross-pair verification
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void TripleWrap_MultiplePairs_AllRoundtrip()
    {
        for (int i = 0; i < 10; i++)
        {
            var sender = NostrIdentity.Generate();
            var recipient = NostrIdentity.Generate();
            var content = $"Message {i} from {sender.PublicKeyHex[..8]}";

            var wrap = NostrEnvelope.CreatePrivateMessage(content, recipient.PublicKeyHex, sender);
            var (decrypted, senderPubkey, _) = NostrEnvelope.DecryptPrivateMessage(wrap, recipient);

            Assert.Equal(content, decrypted);
            Assert.Equal(sender.PublicKeyHex, senderPubkey);
        }
    }

    [Fact]
    public void TripleWrap_CrossIdentity_MessagesStayIsolated()
    {
        var alice = NostrIdentity.Generate();
        var bob = NostrIdentity.Generate();
        var carol = NostrIdentity.Generate();

        var toBob = NostrEnvelope.CreatePrivateMessage("for Bob only", bob.PublicKeyHex, alice);
        var toCarol = NostrEnvelope.CreatePrivateMessage("for Carol only", carol.PublicKeyHex, alice);

        var (fromBob, _, _) = NostrEnvelope.DecryptPrivateMessage(toBob, bob);
        var (fromCarol, _, _) = NostrEnvelope.DecryptPrivateMessage(toCarol, carol);

        Assert.Equal("for Bob only", fromBob);
        Assert.Equal("for Carol only", fromCarol);

        Assert.ThrowsAny<Exception>(() =>
            NostrEnvelope.DecryptPrivateMessage(toBob, carol));
        Assert.ThrowsAny<Exception>(() =>
            NostrEnvelope.DecryptPrivateMessage(toCarol, bob));
    }

    // ═══════════════════════════════════════════════════════════
    //  Helper — unwrap a single layer
    // ═══════════════════════════════════════════════════════════

    private static NostrEvent UnwrapGiftWrapLayer(NostrEvent giftWrap, NostrIdentity recipient)
    {
        var decrypted = NostrEnvelope.DecryptContent(
            giftWrap.Content, giftWrap.Pubkey, recipient.PrivateKey);
        return NostrEvent.FromJson(decrypted)
            ?? throw new InvalidOperationException("Invalid seal JSON");
    }

    private class StringArrayComparer : IEqualityComparer<string[]>
    {
        public bool Equals(string[]? x, string[]? y)
        {
            if (x == null || y == null) return x == y;
            return x.SequenceEqual(y);
        }
        public int GetHashCode(string[] obj) =>
            obj.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode()));
    }
}
