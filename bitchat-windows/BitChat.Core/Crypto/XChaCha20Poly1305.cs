using System.Security.Cryptography;

namespace BitChat.Core.Crypto;

public static class XChaCha20Poly1305
{
    public static (byte[] ciphertext, byte[] tag) Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce24)
    {
        if (key.Length != 32) throw new ArgumentException(null, nameof(key));
        if (nonce24.Length != 24) throw new ArgumentException(null, nameof(nonce24));

        var subkey = HChaCha20.DeriveSubkey(key, nonce16: nonce24[..16]);
        var nonce12 = new byte[12];
        nonce24[^8..].CopyTo(nonce12.AsSpan(4));

        using var aead = new ChaCha20Poly1305(subkey);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        aead.Encrypt(nonce12, plaintext, ciphertext, tag);
        return (ciphertext, tag);
    }

    public static byte[] Decrypt(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce24)
    {
        if (key.Length != 32) throw new ArgumentException(null, nameof(key));
        if (nonce24.Length != 24) throw new ArgumentException(null, nameof(nonce24));
        if (tag.Length != 16) throw new ArgumentException(null, nameof(tag));

        var subkey = HChaCha20.DeriveSubkey(key, nonce16: nonce24[..16]);
        var nonce12 = new byte[12];
        nonce24[^8..].CopyTo(nonce12.AsSpan(4));

        using var aead = new ChaCha20Poly1305(subkey);
        var plaintext = new byte[ciphertext.Length];
        aead.Decrypt(nonce12, ciphertext, tag, plaintext);
        return plaintext;
    }
}
