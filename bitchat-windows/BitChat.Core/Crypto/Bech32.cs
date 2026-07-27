using System.Text;

namespace BitChat.Core.Crypto;

public static class Bech32
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private static readonly int[] Generator = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3];

    public static string Encode(string hrp, byte[] data)
    {
        var values = ConvertBits(data, 8, 5, true);
        var checksum = CreateChecksum(hrp, values);
        var combined = values.Concat(checksum).ToArray();
        var sb = new StringBuilder();
        sb.Append(hrp);
        sb.Append('1');
        foreach (var v in combined)
            sb.Append(Charset[v]);
        return sb.ToString();
    }

    public static (string hrp, byte[] data) Decode(string bech32)
    {
        var sepIdx = bech32.LastIndexOf('1');
        if (sepIdx < 1) throw new FormatException("Invalid Bech32 format");

        var hrp = bech32[..sepIdx];
        var dataStr = bech32[(sepIdx + 1)..];

        var values = new List<byte>();
        foreach (var c in dataStr)
        {
            var idx = Charset.IndexOf(c);
            if (idx < 0) throw new FormatException("Invalid Bech32 character");
            values.Add((byte)idx);
        }

        if (values.Count < 6) throw new FormatException("Invalid Bech32 checksum");

        var payload = values.Take(values.Count - 6).ToArray();
        var checksum = values.Skip(values.Count - 6).ToArray();
        var expected = CreateChecksum(hrp, payload);
        if (!checksum.SequenceEqual(expected))
            throw new FormatException("Invalid Bech32 checksum");

        return (hrp, ConvertBits(payload, 5, 8, false));
    }

    private static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
    {
        int acc = 0, bits = 0;
        var result = new List<byte>();
        var mask = (1 << toBits) - 1;
        foreach (var value in data)
        {
            acc = (acc << fromBits) | value;
            bits += fromBits;
            while (bits >= toBits)
            {
                bits -= toBits;
                result.Add((byte)((acc >> bits) & mask));
            }
        }
        if (pad && bits > 0)
            result.Add((byte)((acc << (toBits - bits)) & mask));
        return result.ToArray();
    }

    private static byte[] CreateChecksum(string hrp, byte[] data)
    {
        var expanded = HrpExpand(hrp);
        var values = expanded.Concat(data).Concat(new byte[] { 0, 0, 0, 0, 0, 0 }).ToArray();
        var polymod = Polymod(values) ^ 1;
        var checksum = new byte[6];
        for (int i = 0; i < 6; i++)
            checksum[i] = (byte)((polymod >> (5 * (5 - i))) & 31);
        return checksum;
    }

    private static byte[] HrpExpand(string hrp)
    {
        var result = new byte[hrp.Length * 2 + 1];
        for (int i = 0; i < hrp.Length; i++)
            result[i] = (byte)(hrp[i] >> 5);
        result[hrp.Length] = 0;
        for (int i = 0; i < hrp.Length; i++)
            result[hrp.Length + 1 + i] = (byte)(hrp[i] & 31);
        return result;
    }

    private static int Polymod(byte[] values)
    {
        int chk = 1;
        foreach (var v in values)
        {
            var b = chk >> 25;
            chk = ((chk & 0x1ffffff) << 5) ^ v;
            for (int i = 0; i < 5; i++)
                if ((b >> i & 1) == 1)
                    chk ^= Generator[i];
        }
        return chk;
    }
}
