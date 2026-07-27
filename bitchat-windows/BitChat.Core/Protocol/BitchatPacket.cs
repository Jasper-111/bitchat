namespace BitChat.Core.Protocol;

public class BitchatPacket
{
    public byte Version { get; set; } = 1;
    public byte Type { get; set; }
    public byte TTL { get; set; } = 7;
    public ulong Timestamp { get; set; }
    public byte[] SenderID { get; set; } = [];
    public byte[]? RecipientID { get; set; }
    public byte[] Payload { get; set; } = [];
    public byte[]? Signature { get; set; }
    public List<byte[]>? Route { get; set; }
    public bool IsRSR { get; set; }

    public static BitchatPacket? FromBinary(byte[] data) => BinaryProtocol.Decode(data);
    public byte[]? ToBinary(bool padding = true) => BinaryProtocol.Encode(this, padding);
}
