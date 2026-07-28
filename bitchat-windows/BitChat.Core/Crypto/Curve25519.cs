using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace BitChat.Core.Crypto;

public static class Curve25519
{
    public static (byte[] privateKey, byte[] publicKey) GenerateKeyPair()
    {
        var gen = new X25519KeyPairGenerator();
        gen.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        var priv = ((X25519PrivateKeyParameters)pair.Private).GetEncoded();
        var pub = ((X25519PublicKeyParameters)pair.Public).GetEncoded();
        return (priv, pub);
    }

    public static byte[] DerivePublicKey(byte[] privateKey32)
    {
        var priv = new X25519PrivateKeyParameters(privateKey32);
        var pub = priv.GeneratePublicKey();
        return pub.GetEncoded();
    }

    public static byte[] ComputeSharedSecret(byte[] privateKey32, byte[] publicKey32)
    {
        var priv = new X25519PrivateKeyParameters(privateKey32);
        var pub = new X25519PublicKeyParameters(publicKey32);
        var agreement = new X25519Agreement();
        agreement.Init(priv);
        var shared = new byte[32];
        agreement.CalculateAgreement(pub, shared);
        return shared;
    }
}
