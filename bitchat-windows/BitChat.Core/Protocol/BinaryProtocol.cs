using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace BitChat.Core.Protocol;

public static class BinaryProtocol
{
    public const byte V1HeaderSize = 14;
    public const byte SenderIDSize = 8;
    public const byte RecipientIDSize = 8;
    public const byte SignatureSize = 64;

    public static class Flags
    {
        public const byte HasRecipient = 0x01;
        public const byte HasSignature = 0x02;
        public const byte IsCompressed = 0x04;
        public const byte HasRoute = 0x08;
        public const byte IsRSR = 0x10;
    }

    public static byte[]? Encode(BitchatPacket packet, bool padding = true)
    {
        byte version = packet.Version;
        if (version != 1 && version != 2) return null;

        byte flags = 0;
        if (packet.RecipientID != null) flags |= Flags.HasRecipient;
        if (packet.Signature != null) flags |= Flags.HasSignature;

        bool isCompressed = false;
        int originalPayloadSize = 0;
        byte[] payload = packet.Payload;

        // Compression disabled for Nostr path payloads (usually small)
        byte[] header = new byte[V1HeaderSize];
        int pos = 0;
        header[pos++] = version;
        header[pos++] = packet.Type;
        header[pos++] = packet.TTL;

        for (int s = 56; s >= 0; s -= 8)
            header[pos++] = (byte)((packet.Timestamp >> s) & 0xFF);

        header[pos++] = flags;

        ushort payloadLen = (ushort)(payload.Length + (isCompressed ? 2 : 0));
        header[pos++] = (byte)(payloadLen >> 8);
        header[pos++] = (byte)(payloadLen & 0xFF);

        int totalSize = header.Length + SenderIDSize
            + (packet.RecipientID != null ? RecipientIDSize : 0)
            + (isCompressed ? 2 : 0)
            + payload.Length
            + (packet.Signature != null ? SignatureSize : 0);

        var result = new byte[totalSize];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        pos = header.Length;

        var sid = packet.SenderID;
        if (sid.Length < SenderIDSize)
        {
            var padded = new byte[SenderIDSize];
            Buffer.BlockCopy(sid, 0, padded, 0, sid.Length);
            sid = padded;
        }
        Buffer.BlockCopy(sid, 0, result, pos, SenderIDSize);
        pos += SenderIDSize;

        if (packet.RecipientID != null)
        {
            var rid = packet.RecipientID;
            if (rid.Length < RecipientIDSize)
            {
                var padded = new byte[RecipientIDSize];
                Buffer.BlockCopy(rid, 0, padded, 0, rid.Length);
                rid = padded;
            }
            Buffer.BlockCopy(rid, 0, result, pos, RecipientIDSize);
            pos += RecipientIDSize;
        }

        if (isCompressed)
        {
            result[pos++] = (byte)(originalPayloadSize >> 8);
            result[pos++] = (byte)(originalPayloadSize & 0xFF);
        }

        Buffer.BlockCopy(payload, 0, result, pos, payload.Length);
        pos += payload.Length;

        if (packet.Signature != null)
        {
            Buffer.BlockCopy(packet.Signature, 0, result, pos, Math.Min(packet.Signature.Length, SignatureSize));
        }

        if (padding)
        {
            var optimal = MessagePadding.OptimalBlockSize(result.Length);
            return MessagePadding.Pad(result, optimal);
        }
        return result;
    }

    public static BitchatPacket? Decode(byte[] data)
    {
        if (data.Length < V1HeaderSize + SenderIDSize) return null;

        int pos = 0;
        var version = data[pos++];
        if (version != 1 && version != 2) return null;

        var type = data[pos++];
        var ttl = data[pos++];

        ulong timestamp = 0;
        for (int i = 0; i < 8; i++)
            timestamp = (timestamp << 8) | data[pos++];

        var flags = data[pos++];
        bool hasRecipient = (flags & Flags.HasRecipient) != 0;
        bool hasSignature = (flags & Flags.HasSignature) != 0;
        bool isCompressed = (flags & Flags.IsCompressed) != 0;

        int payloadLen = (data[pos++] << 8) | data[pos++];

        var senderID = new byte[SenderIDSize];
        Buffer.BlockCopy(data, pos, senderID, 0, SenderIDSize);
        pos += SenderIDSize;

        byte[]? recipientID = null;
        if (hasRecipient)
        {
            recipientID = new byte[RecipientIDSize];
            Buffer.BlockCopy(data, pos, recipientID, 0, RecipientIDSize);
            pos += RecipientIDSize;
        }

        byte[] payload;
        if (isCompressed)
        {
            int originalSize = (data[pos++] << 8) | data[pos++];
            int compressedLen = payloadLen - 2;
            payload = new byte[originalSize];
            DeflateStreamHelper.Decompress(data[pos..(pos + compressedLen)], payload);
            pos += compressedLen;
        }
        else
        {
            payload = new byte[payloadLen];
            Buffer.BlockCopy(data, pos, payload, 0, payloadLen);
            pos += payloadLen;
        }

        byte[]? signature = null;
        if (hasSignature && pos + SignatureSize <= data.Length)
        {
            signature = new byte[SignatureSize];
            Buffer.BlockCopy(data, pos, signature, 0, SignatureSize);
        }

        return new BitchatPacket
        {
            Version = version,
            Type = type,
            TTL = ttl,
            Timestamp = timestamp,
            SenderID = senderID,
            RecipientID = recipientID,
            Payload = payload,
            Signature = signature
        };
    }

    private static class DeflateStreamHelper
    {
        public static void Decompress(byte[] compressed, byte[] output)
        {
            using var ms = new MemoryStream(compressed);
            ms.ReadByte(); // skip zlib header
            ms.ReadByte();
            using var deflate = new DeflateStream(ms, CompressionMode.Decompress);
            deflate.ReadExactly(output);
        }
    }
}
