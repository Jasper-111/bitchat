using System.Security.Cryptography;

namespace BitChat.Core.Services.Courier;

public sealed class CourierEnvelope
{
    private const int MaxCiphertextBytes = 16 * 1024;
    private const int MaxCopies = 8;
    private const int MaxLifetimeHours = 24;

    private const byte TagRecipientTag = 0x01;
    private const byte TagExpiry = 0x02;
    private const byte TagCiphertext = 0x03;
    private const byte TagCopies = 0x04;
    private const byte TagPrekeyID = 0x05;

    public byte[] RecipientTag { get; }
    public ulong Expiry { get; }
    public byte[] Ciphertext { get; }
    public byte Copies { get; }
    public uint? PrekeyID { get; }

    public CourierEnvelope(byte[] recipientTag, ulong expiry, byte[] ciphertext, byte copies = 1, uint? prekeyID = null)
    {
        if (recipientTag.Length != 16) throw new ArgumentException("Recipient tag must be 16 bytes");
        if (ciphertext.Length > MaxCiphertextBytes) throw new ArgumentException("Ciphertext too large");
        if (copies > MaxCopies) throw new ArgumentException("Too many copies");
        var maxExpiry = (ulong)DateTimeOffset.UtcNow.AddHours(MaxLifetimeHours).ToUnixTimeMilliseconds();
        if (expiry > maxExpiry) throw new ArgumentException("Expiry too far in future");
        RecipientTag = recipientTag;
        Expiry = expiry;
        Ciphertext = ciphertext;
        Copies = copies;
        PrekeyID = prekeyID;
    }

    public byte[] Encode()
    {
        var parts = new List<byte[]>
        {
            EncodeTLV(TagRecipientTag, RecipientTag),
            EncodeTLV(TagExpiry, BitConverter.GetBytes(Expiry)),
            EncodeTLV(TagCiphertext, Ciphertext),
            EncodeTLV(TagCopies, [Copies])
        };
        if (PrekeyID.HasValue)
            parts.Add(EncodeTLV(TagPrekeyID, BitConverter.GetBytes(PrekeyID.Value)));

        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }

    public static CourierEnvelope? Decode(byte[] data)
    {
        byte[]? recipientTag = null;
        ulong? expiry = null;
        byte[]? ciphertext = null;
        byte copies = 1;
        uint? prekeyID = null;

        var offset = 0;
        while (offset + 3 <= data.Length)
        {
            var tag = data[offset];
            var length = (ushort)((data[offset + 1] << 8) | data[offset + 2]);
            offset += 3;
            if (offset + length > data.Length) return null;

            var value = data[offset..(offset + length)];
            offset += length;

            switch (tag)
            {
                case TagRecipientTag:
                    recipientTag = value;
                    break;
                case TagExpiry:
                    if (value.Length == 8) expiry = BitConverter.ToUInt64(value);
                    break;
                case TagCiphertext:
                    ciphertext = value;
                    break;
                case TagCopies:
                    if (value.Length == 1) copies = value[0];
                    break;
                case TagPrekeyID:
                    if (value.Length == 4) prekeyID = BitConverter.ToUInt32(value);
                    break;
            }
        }

        if (recipientTag == null || expiry == null || ciphertext == null) return null;
        return new CourierEnvelope(recipientTag, expiry.Value, ciphertext, copies, prekeyID);
    }

    public static byte[] DeriveRecipientTag(byte[] recipientStaticKey, long epochDay)
    {
        var tagKey = "bitchat-courier-tag-v1"u8.ToArray();
        var epochBytes = BitConverter.GetBytes(epochDay);
        if (BitConverter.IsLittleEndian) Array.Reverse(epochBytes);

        var hmacData = new byte[tagKey.Length + 8];
        Buffer.BlockCopy(tagKey, 0, hmacData, 0, tagKey.Length);
        Buffer.BlockCopy(epochBytes, 0, hmacData, tagKey.Length, 8);

        var tag = HMACSHA256.HashData(recipientStaticKey, hmacData);
        return tag[..16];
    }

    public static IEnumerable<byte[]> CandidateTags(byte[] recipientStaticKey, long aroundEpochDay)
    {
        for (var d = aroundEpochDay - 1; d <= aroundEpochDay + 1; d++)
            yield return DeriveRecipientTag(recipientStaticKey, d);
    }

    public static long CurrentEpochDay()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
    }

    private static byte[] EncodeTLV(byte type, byte[] value)
    {
        var len = (ushort)value.Length;
        var result = new byte[3 + value.Length];
        result[0] = type;
        result[1] = (byte)(len >> 8);
        result[2] = (byte)(len & 0xFF);
        Buffer.BlockCopy(value, 0, result, 3, value.Length);
        return result;
    }
}
