namespace BitChat.Core.Crypto;

internal static class HChaCha20
{
    private const string Sigma = "expand 32-byte k";

    public static byte[] DeriveSubkey(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce16)
    {
        if (key.Length != 32) throw new ArgumentException("Key must be 32 bytes", nameof(key));
        if (nonce16.Length != 16) throw new ArgumentException("Nonce must be 16 bytes", nameof(nonce16));

        Span<uint> state = stackalloc uint[16];
        state[0] = 0x61707865;
        state[1] = 0x3320646e;
        state[2] = 0x79622d32;
        state[3] = 0x6b206574;

        state[4] = LoadLE32(key[..4]);
        state[5] = LoadLE32(key[4..8]);
        state[6] = LoadLE32(key[8..12]);
        state[7] = LoadLE32(key[12..16]);
        state[8] = LoadLE32(key[16..20]);
        state[9] = LoadLE32(key[20..24]);
        state[10] = LoadLE32(key[24..28]);
        state[11] = LoadLE32(key[28..32]);

        state[12] = LoadLE32(nonce16[..4]);
        state[13] = LoadLE32(nonce16[4..8]);
        state[14] = LoadLE32(nonce16[8..12]);
        state[15] = LoadLE32(nonce16[12..16]);

        Span<uint> work = stackalloc uint[16];
        state.CopyTo(work);

        for (int i = 0; i < 10; i++)
        {
            QuarterRound(ref work, 0, 4, 8, 12);
            QuarterRound(ref work, 1, 5, 9, 13);
            QuarterRound(ref work, 2, 6, 10, 14);
            QuarterRound(ref work, 3, 7, 11, 15);
            QuarterRound(ref work, 0, 5, 10, 15);
            QuarterRound(ref work, 1, 6, 11, 12);
            QuarterRound(ref work, 2, 7, 8, 13);
            QuarterRound(ref work, 3, 4, 9, 14);
        }

        var result = new byte[32];
        StoreLE32(result.AsSpan(0, 4), work[0]);
        StoreLE32(result.AsSpan(4, 4), work[1]);
        StoreLE32(result.AsSpan(8, 4), work[2]);
        StoreLE32(result.AsSpan(12, 4), work[3]);
        StoreLE32(result.AsSpan(16, 4), work[12]);
        StoreLE32(result.AsSpan(20, 4), work[13]);
        StoreLE32(result.AsSpan(24, 4), work[14]);
        StoreLE32(result.AsSpan(28, 4), work[15]);

        return result;
    }

    private static void QuarterRound(ref Span<uint> s, int a, int b, int c, int d)
    {
        s[a] += s[b]; s[d] ^= s[a]; s[d] = (s[d] << 16) | (s[d] >> 16);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = (s[b] << 12) | (s[b] >> 20);
        s[a] += s[b]; s[d] ^= s[a]; s[d] = (s[d] << 8) | (s[d] >> 24);
        s[c] += s[d]; s[b] ^= s[c]; s[b] = (s[b] << 7) | (s[b] >> 25);
    }

    private static uint LoadLE32(ReadOnlySpan<byte> b) =>
        (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));

    private static void StoreLE32(Span<byte> span, uint v)
    {
        span[0] = (byte)v;
        span[1] = (byte)(v >> 8);
        span[2] = (byte)(v >> 16);
        span[3] = (byte)(v >> 24);
    }
}
