using System.Security.Cryptography;

namespace BitChat.Core.Crypto.Noise;

public sealed class SymmetricState
{
    private byte[] _ck = [];
    private byte[] _h = [];
    private readonly CipherState _cipher = new();

    private const int HashLen = 32;

    public void InitializeSymmetric(string protocolName)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(protocolName);
        if (nameBytes.Length <= HashLen)
        {
            _h = new byte[HashLen];
            Buffer.BlockCopy(nameBytes, 0, _h, 0, nameBytes.Length);
        }
        else
        {
            _h = SHA256.HashData(nameBytes);
        }
        _ck = (byte[])_h.Clone();
        MixHash([]);
    }

    public void MixKey(byte[] ikm)
    {
        var (ck, k) = Hkdf(_ck, ikm, 2);
        _ck = ck;
        if (k.Length >= 32) k = k[..32];
        _cipher.InitializeKey(k);
    }

    public void MixHash(byte[] data)
    {
        var combined = new byte[_h.Length + data.Length];
        Buffer.BlockCopy(_h, 0, combined, 0, _h.Length);
        Buffer.BlockCopy(data, 0, combined, _h.Length, data.Length);
        _h = SHA256.HashData(combined);
    }

    public byte[] EncryptAndHash(byte[] plaintext)
    {
        var ct = _cipher.EncryptWithAd(_h, plaintext);
        MixHash(ct);
        return ct;
    }

    public byte[] DecryptAndHash(byte[] ciphertext)
    {
        var pt = _cipher.DecryptWithAd(_h, ciphertext);
        MixHash(ciphertext);
        return pt;
    }

    public (CipherState send, CipherState recv) Split()
    {
        var (k1, k2) = Hkdf(_ck, [], 2);
        var c1 = new CipherState();
        var c2 = new CipherState();
        c1.InitializeKey(k1.Length >= 32 ? k1[..32] : k1);
        c2.InitializeKey(k2.Length >= 32 ? k2[..32] : k2);
        return (c1, c2);
    }

    public byte[] GetHandshakeHash() => (byte[])_h.Clone();

    private static (byte[], byte[]) Hkdf(byte[] chainingKey, byte[] ikm, int outputs)
    {
        var temp = HMACSHA256.HashData(chainingKey, ikm);
        var o1 = HMACSHA256.HashData(temp, new byte[] { 0x01 });
        if (outputs == 1) return (o1, new byte[0]);
        var o2Input = new byte[o1.Length + 1];
        Buffer.BlockCopy(o1, 0, o2Input, 0, o1.Length);
        o2Input[o1.Length] = 0x02;
        var o2 = HMACSHA256.HashData(temp, o2Input);
        if (outputs == 2) return (o1, o2);
        var o3Input = new byte[o2.Length + 1];
        Buffer.BlockCopy(o2, 0, o3Input, 0, o2.Length);
        o3Input[o2.Length] = 0x03;
        var o3 = HMACSHA256.HashData(temp, o3Input);
        return (o1, o3);
    }
}
