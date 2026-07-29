namespace BitChat.Core.Crypto.Noise;

public class CipherState
{
    private byte[] _key = [];
    private ulong _nonce;

    public bool HasKey => _key.Length > 0;

    public void InitializeKey(byte[] key)
    {
        _key = (byte[])key.Clone();
        _nonce = 0;
    }

    public byte[] EncryptWithAd(byte[] ad, byte[] plaintext)
    {
        if (!HasKey)
            return (byte[])plaintext.Clone();

        var (ciphertext, tag) = XChaCha20Poly1305.NoiseEncrypt(plaintext, _key, _nonce);
        _nonce++;

        var result = new byte[ciphertext.Length + 16];
        Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, ciphertext.Length, 16);
        return result;
    }

    public byte[] DecryptWithAd(byte[] ad, byte[] ciphertextWithTag)
    {
        if (!HasKey)
            return (byte[])ciphertextWithTag.Clone();

        if (ciphertextWithTag.Length < 16)
            throw new System.Security.Cryptography.CryptographicException("Ciphertext too short");

        var ctLen = ciphertextWithTag.Length - 16;
        var tag = ciphertextWithTag[ctLen..];
        var plaintext = XChaCha20Poly1305.NoiseDecrypt(
            ciphertextWithTag[..ctLen], tag, _key, _nonce);
        _nonce++;
        return plaintext;
    }

    public byte[] Rekey()
    {
        _key = EncryptWithAd([], new byte[32]);
        if (_key.Length > 32) _key = _key[..32];
        return (byte[])_key.Clone();
    }
}
