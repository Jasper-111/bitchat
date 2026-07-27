using System.Security.Cryptography;
using System.Text;
using BitChat.Core.Crypto;

namespace BitChat.Core.Nostr;

public class NostrIdentity
{
    public byte[] PrivateKey { get; }
    public byte[] PublicKey { get; }
    public string Npub { get; }
    public string PublicKeyHex => Convert.ToHexString(PublicKey).ToLowerInvariant();

    public NostrIdentity(byte[] privateKey, byte[] publicKey, string npub)
    {
        PrivateKey = privateKey;
        PublicKey = publicKey;
        Npub = npub;
    }

    public static NostrIdentity Generate()
    {
        var (priv, pub) = Secp256k1Helper.GenerateKeyPair();
        var npub = Bech32.Encode("npub", pub);
        return new NostrIdentity(priv, pub, npub);
    }

    public static NostrIdentity FromPrivateKey(byte[] privateKey)
    {
        var key = new NBitcoin.Key(privateKey);
        var pub = key.PubKey.ToBytes();
        var xonly = new byte[32];
        System.Buffer.BlockCopy(pub, 1, xonly, 0, 32);
        var npub = Bech32.Encode("npub", xonly);
        return new NostrIdentity(privateKey, xonly, npub);
    }
}
