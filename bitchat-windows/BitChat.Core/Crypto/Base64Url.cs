namespace BitChat.Core.Crypto;

public static class Base64Url
{
    public static string Encode(byte[] data) =>
        Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    public static byte[] Decode(string encoded)
    {
        var base64 = encoded.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (base64.Length % 4)) % 4;
        if (padding > 0) base64 += new string('=', padding);
        return Convert.FromBase64String(base64);
    }
}
