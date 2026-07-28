using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace BitChat.Core.Crypto;

public static class Ed25519
{
    public static (byte[] privateKey, byte[] publicKey) GenerateKeyPair()
    {
        var gen = new Org.BouncyCastle.Crypto.Generators.Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(new Org.BouncyCastle.Security.SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var priv = ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded();
        var pub = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
        return (priv, pub);
    }

    public static byte[] DerivePublicKey(byte[] privateKey32)
    {
        var priv = new Ed25519PrivateKeyParameters(privateKey32, 0);
        var pub = priv.GeneratePublicKey();
        return pub.GetEncoded();
    }

    public static byte[] Sign(byte[] privateKey32, byte[] message)
    {
        var signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(privateKey32, 0));
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    public static bool Verify(byte[] publicKey32, byte[] message, byte[] signature64)
    {
        if (publicKey32.Length != 32 || signature64.Length != 64)
            return false;
        try
        {
            var signer = new Org.BouncyCastle.Crypto.Signers.Ed25519Signer();
            signer.Init(false, new Ed25519PublicKeyParameters(publicKey32, 0));
            signer.BlockUpdate(message, 0, message.Length);
            return signer.VerifySignature(signature64);
        }
        catch
        {
            return false;
        }
    }
}
