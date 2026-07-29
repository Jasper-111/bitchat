using System.Buffers.Binary;
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

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        AeadEncrypt(plaintext, ciphertext, tag, subkey, nonce12);
        return (ciphertext, tag);
    }

    public static (byte[] ciphertext, byte[] tag) NoiseEncrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ulong noiseNonce,
        ReadOnlySpan<byte> ad = default)
    {
        if (key.Length != 32) throw new ArgumentException(null, nameof(key));
        var nonce12 = new byte[12];
        nonce12[0] = (byte)(noiseNonce);
        nonce12[1] = (byte)(noiseNonce >> 8);
        nonce12[2] = (byte)(noiseNonce >> 16);
        nonce12[3] = (byte)(noiseNonce >> 24);
        nonce12[4] = (byte)(noiseNonce >> 32);
        nonce12[5] = (byte)(noiseNonce >> 40);
        nonce12[6] = (byte)(noiseNonce >> 48);
        nonce12[7] = (byte)(noiseNonce >> 56);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        AeadEncrypt(plaintext, ciphertext, tag, key, nonce12, ad);
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

        var plaintext = new byte[ciphertext.Length];
        AeadDecrypt(ciphertext, tag, plaintext, subkey, nonce12);
        return plaintext;
    }

    public static byte[] NoiseDecrypt(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> key,
        ulong noiseNonce,
        ReadOnlySpan<byte> ad = default)
    {
        if (key.Length != 32) throw new ArgumentException(null, nameof(key));
        if (tag.Length != 16) throw new ArgumentException(null, nameof(tag));

        var nonce12 = new byte[12];
        nonce12[0] = (byte)(noiseNonce);
        nonce12[1] = (byte)(noiseNonce >> 8);
        nonce12[2] = (byte)(noiseNonce >> 16);
        nonce12[3] = (byte)(noiseNonce >> 24);
        nonce12[4] = (byte)(noiseNonce >> 32);
        nonce12[5] = (byte)(noiseNonce >> 40);
        nonce12[6] = (byte)(noiseNonce >> 48);
        nonce12[7] = (byte)(noiseNonce >> 56);

        var plaintext = new byte[ciphertext.Length];
        AeadDecrypt(ciphertext, tag, plaintext, key, nonce12, ad);
        return plaintext;
    }

    private static void AeadEncrypt(
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertext,
        Span<byte> tag,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> aad = default)
    {
        Span<byte> block = stackalloc byte[64];
        ChaCha20Block(key, 0, nonce, block);
        Span<byte> polyKey = block[..32];

        Span<byte> keystream = stackalloc byte[64];
        uint counter = 1;
        int pos = 0;
        int remaining = plaintext.Length;
        while (remaining > 0)
        {
            ChaCha20Block(key, counter, nonce, keystream);
            int chunk = Math.Min(remaining, 64);
            for (int i = 0; i < chunk; i++)
                ciphertext[pos + i] = (byte)(plaintext[pos + i] ^ keystream[i]);
            pos += chunk;
            remaining -= chunk;
            counter++;
        }

        ComputePoly1305Tag(tag, aad, ciphertext, polyKey);
    }

    private static void AeadDecrypt(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> aad = default)
    {
        Span<byte> block = stackalloc byte[64];
        ChaCha20Block(key, 0, nonce, block);
        Span<byte> polyKey = block[..32];

        var computedTag = (stackalloc byte[16]);
        ComputePoly1305Tag(computedTag, aad, ciphertext, polyKey);
        if (!CryptographicOperations.FixedTimeEquals(computedTag, tag))
            throw new CryptographicException("Invalid authentication tag");

        Span<byte> keystream = stackalloc byte[64];
        uint counter = 1;
        int pos = 0;
        int remaining = ciphertext.Length;
        while (remaining > 0)
        {
            ChaCha20Block(key, counter, nonce, keystream);
            int chunk = Math.Min(remaining, 64);
            for (int i = 0; i < chunk; i++)
                plaintext[pos + i] = (byte)(ciphertext[pos + i] ^ keystream[i]);
            pos += chunk;
            remaining -= chunk;
            counter++;
        }
    }

    private static void ComputePoly1305Tag(
        Span<byte> tag,
        ReadOnlySpan<byte> aad,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> key)
    {
        Span<uint> r = stackalloc uint[5];
        r[0] = LE32(key[..4]) & 0x03FFFFFF;
        r[1] = (LE32(key[3..7]) >> 2) & 0x03FFFF03;
        r[2] = (LE32(key[6..10]) >> 4) & 0x03FFC0FF;
        r[3] = (LE32(key[9..13]) >> 6) & 0x03F03FFF;
        r[4] = (LE32(key[12..16]) >> 8) & 0x000FFFFF;

        Span<ulong> h = stackalloc ulong[5];
        h.Clear();

        Poly1305Blocks(h, r, aad);
        Poly1305Blocks(h, r, ciphertext);

        Span<byte> lens = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(lens, (ulong)aad.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(lens[8..], (ulong)ciphertext.Length);
        Poly1305Block(h, r, lens, last: true);

        ulong s0 = LE32(key[16..20]);
        ulong s1 = LE32(key[20..24]);
        ulong s2 = LE32(key[24..28]);
        ulong s3 = LE32(key[28..32]);

        h[0] += s0; h[1] += s1; h[2] += s2; h[3] += s3;

        ulong c = h[0] >> 32; h[0] &= 0xFFFFFFFF;
        h[1] += c; c = h[1] >> 32; h[1] &= 0xFFFFFFFF;
        h[2] += c; c = h[2] >> 32; h[2] &= 0xFFFFFFFF;
        h[3] += c; c = h[3] >> 32; h[3] &= 0xFFFFFFFF;
        h[4] += c;

        ulong g0 = h[0] + 5; c = g0 >> 32; g0 &= 0xFFFFFFFF;
        ulong g1 = h[1] + c; c = g1 >> 32; g1 &= 0xFFFFFFFF;
        ulong g2 = h[2] + c; c = g2 >> 32; g2 &= 0xFFFFFFFF;
        ulong g3 = h[3] + c; c = g3 >> 32; g3 &= 0xFFFFFFFF;
        ulong g4 = h[4] + c - (1uL << 32);

        ulong mask = (ulong)((long)(g4 >> 63) - 1);
        h[0] = (h[0] & ~mask) | (g0 & mask);
        h[1] = (h[1] & ~mask) | (g1 & mask);
        h[2] = (h[2] & ~mask) | (g2 & mask);
        h[3] = (h[3] & ~mask) | (g3 & mask);

        StLE32(tag[..4], (uint)h[0]);
        StLE32(tag[4..8], (uint)(h[0] >> 32));
        StLE32(tag[8..12], (uint)h[1]);
        StLE32(tag[12..16], (uint)(h[1] >> 32));
    }

    private static void Poly1305Blocks(Span<ulong> h, Span<uint> r, ReadOnlySpan<byte> input)
    {
        Span<byte> buf = stackalloc byte[16];
        int pos = 0;
        while (pos + 16 <= input.Length)
        {
            Poly1305Block(h, r, input.Slice(pos, 16), last: false);
            pos += 16;
        }
        int rem = input.Length - pos;
        if (rem > 0)
        {
            buf.Clear();
            input[pos..].CopyTo(buf);
            Poly1305Block(h, r, buf, last: false);
        }
    }

    private static void Poly1305Block(Span<ulong> h, Span<uint> r, ReadOnlySpan<byte> block, bool last)
    {
        ulong m0 = LE32(block[..4]);
        ulong m1 = LE32(block[4..8]);
        ulong m2 = LE32(block[8..12]);
        ulong m3 = LE32(block[12..16]);

        if (last) m3 |= 0x100000000;

        h[0] = (h[0] + m0) & 0xFFFFFFFF;
        h[1] = (h[1] + m1 + (h[0] >> 32)) & 0xFFFFFFFF;
        h[0] &= 0xFFFFFFFF;
        h[2] = (h[2] + m2 + (h[1] >> 32)) & 0xFFFFFFFF;
        h[1] &= 0xFFFFFFFF;
        h[3] = (h[3] + m3 + (h[2] >> 32)) & 0xFFFFFFFF;
        h[2] &= 0xFFFFFFFF;
        h[4] = (h[4] + (h[3] >> 32)) & 0xFFFFFFFF;
        h[3] &= 0xFFFFFFFF;

        ulong a0 = h[0] * r[0];
        ulong a1 = h[1] * r[0] + h[0] * r[1];
        ulong a2 = h[2] * r[0] + h[1] * r[1] + h[0] * r[2];
        ulong a3 = h[3] * r[0] + h[2] * r[1] + h[1] * r[2] + h[0] * r[3];
        ulong a4 = h[4] * r[0] + h[3] * r[1] + h[2] * r[2] + h[1] * r[3];
        ulong a5 = h[4] * r[1] + h[3] * r[2] + h[2] * r[3];
        ulong a6 = h[4] * r[2] + h[3] * r[3];
        ulong a7 = h[4] * r[3];

        ulong t0 = h[0] * r[4] * 5;
        ulong t1 = h[1] * r[4] * 5;
        ulong t2 = h[2] * r[4] * 5;
        ulong t3 = h[3] * r[4] * 5;
        ulong t4 = h[4] * r[4] * 5;

        a4 += t0;
        a5 += t1;
        a6 += t2;
        a7 += t3;

        h[0] = a0 + t4 - (a4 & 0xFFFFFFFF) * 5;
        ulong carry;
        h[1] = a1 + t0 - (a4 >> 32) + (carry = h[0] >> 32); h[0] &= 0xFFFFFFFF;
        carry = h[1] >> 32; h[1] &= 0xFFFFFFFF;
        h[2] = a2 + t1 + carry; carry = h[2] >> 32; h[2] &= 0xFFFFFFFF;
        h[3] = a3 + t2 + carry; carry = h[3] >> 32; h[3] &= 0xFFFFFFFF;
        h[4] = a5 + t3 + carry; carry = h[4] >> 32; h[4] &= 0xFFFFFFFF;
        h[0] += carry * 5; carry = h[0] >> 32; h[0] &= 0xFFFFFFFF;
        h[1] += carry;
    }

    private static void ChaCha20Block(ReadOnlySpan<byte> key, uint counter, ReadOnlySpan<byte> nonce, Span<byte> output)
    {
        Span<uint> s = stackalloc uint[16];
        s[0] = 0x61707865; s[1] = 0x3320646e; s[2] = 0x79622d32; s[3] = 0x6b206574;
        s[4] = LE32(key[..4]); s[5] = LE32(key[4..8]); s[6] = LE32(key[8..12]); s[7] = LE32(key[12..16]);
        s[8] = LE32(key[16..20]); s[9] = LE32(key[20..24]); s[10] = LE32(key[24..28]); s[11] = LE32(key[28..32]);
        s[12] = counter;
        s[13] = LE32(nonce[..4]); s[14] = LE32(nonce[4..8]); s[15] = LE32(nonce[8..12]);

        Span<uint> w = stackalloc uint[16];
        s.CopyTo(w);

        for (int i = 0; i < 10; i++)
        {
            QR(ref w, 0, 4, 8, 12); QR(ref w, 1, 5, 9, 13);
            QR(ref w, 2, 6, 10, 14); QR(ref w, 3, 7, 11, 15);
            QR(ref w, 0, 5, 10, 15); QR(ref w, 1, 6, 11, 12);
            QR(ref w, 2, 7, 8, 13); QR(ref w, 3, 4, 9, 14);
        }

        for (int i = 0; i < 16; i++) w[i] += s[i];

        StLE32(output[..4], w[0]); StLE32(output[4..8], w[1]);
        StLE32(output[8..12], w[2]); StLE32(output[12..16], w[3]);
        StLE32(output[16..20], w[4]); StLE32(output[20..24], w[5]);
        StLE32(output[24..28], w[6]); StLE32(output[28..32], w[7]);
        StLE32(output[32..36], w[8]); StLE32(output[36..40], w[9]);
        StLE32(output[40..44], w[10]); StLE32(output[44..48], w[11]);
        StLE32(output[48..52], w[12]); StLE32(output[52..56], w[13]);
        StLE32(output[56..60], w[14]); StLE32(output[60..64], w[15]);
    }

    private static void QR(ref Span<uint> s, int a, int b, int c, int d)
    {
        s[a] += s[b]; s[d] ^= s[a]; s[d] = (s[d] << 16) | (s[d] >> 16);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = (s[b] << 12) | (s[b] >> 20);
        s[a] += s[b]; s[d] ^= s[a]; s[d] = (s[d] << 8) | (s[d] >> 24);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = (s[b] << 7) | (s[b] >> 25);
    }

    private static uint LE32(ReadOnlySpan<byte> b) =>
        (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));

    private static void StLE32(Span<byte> span, uint v)
    {
        span[0] = (byte)v;
        span[1] = (byte)(v >> 8);
        span[2] = (byte)(v >> 16);
        span[3] = (byte)(v >> 24);
    }
}
