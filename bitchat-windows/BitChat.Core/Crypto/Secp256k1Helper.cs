using NBitcoin;
using NBitcoin.Crypto;

namespace BitChat.Core.Crypto;

public static class Secp256k1Helper
{
    public static (byte[] privateKey, byte[] xonlyPublicKey) GenerateKeyPair()
    {
        var key = new Key();
        var xonly = new byte[32];
        Buffer.BlockCopy(key.PubKey.ToBytes(), 1, xonly, 0, 32);
        return (key.ToBytes(), xonly);
    }

    public static byte[] SchnorrSign(byte[] privateKeyBytes, byte[] messageHash)
    {
        var key = new Key(privateKeyBytes);
        var hash = new uint256(messageHash);
        var sig = key.SignTaprootKeySpend(hash, TaprootSigHash.Default);
        return sig.ToBytes();
    }

    public static bool SchnorrVerify(byte[] xonlyPubKey, byte[] messageHash, byte[] signature)
    {
        if (xonlyPubKey.Length != 32 || signature.Length != 64) return false;
        try
        {
            if (!TaprootInternalPubKey.TryCreate(xonlyPubKey, out var internalKey))
                return false;
            var sig = new SchnorrSignature(signature);
            var hash = new uint256(messageHash);
            return internalKey.VerifyTaproot(hash, null, sig);
        }
        catch
        {
            return false;
        }
    }

    public static byte[] Ecdh(byte[] privateKeyBytes, byte[] compressedPubKey33)
    {
        var key = new Key(privateKeyBytes);
        var pub = new PubKey(compressedPubKey33);
        var shared = pub.GetSharedPubkey(key);
        var xonly = new byte[32];
        Buffer.BlockCopy(shared.ToBytes(), 1, xonly, 0, 32);
        return xonly;
    }

    public static byte[] TryEcdhWithParity(byte[] privateKeyBytes, byte[] xonlyPubKey32)
    {
        var key = new Key(privateKeyBytes);
        // Try even Y (0x02)
        try
        {
            var evenPub = new byte[33];
            evenPub[0] = 0x02;
            Buffer.BlockCopy(xonlyPubKey32, 0, evenPub, 1, 32);
            var pub = new PubKey(evenPub);
            var shared = pub.GetSharedPubkey(key);
            var xonly = new byte[32];
            Buffer.BlockCopy(shared.ToBytes(), 1, xonly, 0, 32);
            return xonly;
        }
        catch
        {
            // Try odd Y (0x03)
            var oddPub = new byte[33];
            oddPub[0] = 0x03;
            Buffer.BlockCopy(xonlyPubKey32, 0, oddPub, 1, 32);
            var pub = new PubKey(oddPub);
            var shared = pub.GetSharedPubkey(key);
            var xonly = new byte[32];
            Buffer.BlockCopy(shared.ToBytes(), 1, xonly, 0, 32);
            return xonly;
        }
    }
}
