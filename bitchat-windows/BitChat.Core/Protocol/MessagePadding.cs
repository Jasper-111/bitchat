namespace BitChat.Core.Protocol;

public static class MessagePadding
{
    private static readonly int[] BlockSizes = [256, 512, 1024, 2048];

    public static int OptimalBlockSize(int dataLength)
    {
        foreach (var bs in BlockSizes)
            if (dataLength <= bs) return bs;
        return dataLength;
    }

    public static byte[] Pad(byte[] data, int blockSize)
    {
        var needed = blockSize - (data.Length % blockSize);
        if (needed == 0 || needed > 255) return data;

        var result = new byte[data.Length + needed];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        for (int i = data.Length; i < result.Length; i++)
            result[i] = (byte)needed;
        return result;
    }

    public static byte[] Unpad(byte[] data)
    {
        if (data.Length == 0) return data;
        var padLen = data[^1];
        if (padLen == 0 || padLen > 255 || padLen > data.Length)
            return data;
        for (int i = data.Length - (int)padLen; i < data.Length; i++)
            if (data[i] != padLen) return data;
        var result = new byte[data.Length - (int)padLen];
        Buffer.BlockCopy(data, 0, result, 0, result.Length);
        return result;
    }
}
