using System.Text;

namespace BitChat.Core.Protocol;

public class NoisePayload
{
    public byte Type { get; }
    public byte[] Data { get; }

    public NoisePayload(byte type, byte[] data)
    {
        Type = type;
        Data = data;
    }

    public byte[] Encode()
    {
        var result = new byte[1 + Data.Length];
        result[0] = Type;
        Buffer.BlockCopy(Data, 0, result, 1, Data.Length);
        return result;
    }

    public static NoisePayload? Decode(byte[] encoded)
    {
        if (encoded.Length == 0) return null;
        var type = NoisePayloadType.DecodedRawValue(encoded[0]);
        if (!NoisePayloadType.IsKnown(encoded[0])) return null;
        var data = encoded.Length > 1 ? encoded[1..] : [];
        return new NoisePayload(type, data);
    }
}
