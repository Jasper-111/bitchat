using System.Security.Cryptography;

namespace BitChat.Core.Crypto.Noise;

public sealed class HandshakeState
{
    private readonly SymmetricState _ss = new();
    private byte[] _s;
    private byte[] _e = [];
    private byte[]? _rs;
    private byte[]? _re;
    private readonly bool _initiator;
    private int _step;
    private bool _done;

    private static readonly string ProtocolName = "Noise_XX_25519_ChaChaPoly_SHA256";

    private static readonly string[][] PatternXX = new string[][]
    {
        new string[] { "e" },
        new string[] { "e", "ee", "s", "es" },
        new string[] { "s", "se" },
    };

    public bool IsDone => _done;

    internal void SetEphemeral(byte[] privateKey) { _e = privateKey; }

    public HandshakeState(bool initiator, byte[] staticPrivate, byte[]? prologue = null)
    {
        _initiator = initiator;
        _s = (byte[])staticPrivate.Clone();
        _ss.InitializeSymmetric(ProtocolName);
        _ss.MixHash(prologue ?? []);
    }

    public byte[] WriteMessage(byte[]? payload)
    {
        if (_done) throw new InvalidOperationException("Handshake complete");
        var tokens = PatternXX[_step];
        var buffer = new List<byte>();
        foreach (var token in tokens)
            ProcessToken(token, buffer);
        var encPayload = _ss.EncryptAndHash(payload ?? []);
        buffer.AddRange(encPayload);
        _step++;
        if (_step >= PatternXX.Length) _done = true;
        return buffer.ToArray();
    }

    public byte[] ReadMessage(byte[] message)
    {
        if (_done) throw new InvalidOperationException("Handshake complete");
        var tokens = PatternXX[_step];
        var offset = 0;
        foreach (var token in tokens)
            offset += ReadToken(token, message, offset);
        var payload = _ss.DecryptAndHash(message[offset..]);
        _step++;
        if (_step >= PatternXX.Length) _done = true;
        return payload;
    }

    public (CipherState send, CipherState recv) GetCipherStates()
    {
        if (!_done) throw new InvalidOperationException("Handshake not complete");
        (var c1, var c2) = _ss.Split();
        return _initiator ? (c1, c2) : (c2, c1);
    }

    public byte[] GetHandshakeHash() => _ss.GetHandshakeHash();

    private void ProcessToken(string token, List<byte> buffer)
    {
        switch (token)
        {
            case "e":
            {
                byte[] pubKey;
                if (_e.Length == 0)
                {
                    (_e, pubKey) = Curve25519.GenerateKeyPair();
                }
                else
                {
                    pubKey = Curve25519.DerivePublicKey(_e);
                }
                buffer.AddRange(pubKey);
                _ss.MixHash(pubKey);
                break;
            }
            case "s":
            {
                var sPub = Curve25519.DerivePublicKey(_s);
                buffer.AddRange(_ss.EncryptAndHash(sPub));
                break;
            }
            case "ee":
                _ss.MixKey(Curve25519.ComputeSharedSecret(_e, _re!));
                break;
            case "es":
                _ss.MixKey(_initiator
                    ? Curve25519.ComputeSharedSecret(_e, _rs!)
                    : Curve25519.ComputeSharedSecret(_s, _re!));
                break;
            case "se":
                _ss.MixKey(_initiator
                    ? Curve25519.ComputeSharedSecret(_s, _re!)
                    : Curve25519.ComputeSharedSecret(_e, _rs!));
                break;
        }
    }

    private int ReadToken(string token, byte[] message, int offset)
    {
        switch (token)
        {
            case "e":
                var eData = message[offset..(offset + 32)];
                _re = eData;
                _ss.MixHash(eData);
                return 32;
            case "s":
                var sData = message[offset..(offset + 48)];
                _rs = _ss.DecryptAndHash(sData);
                return 48;
            case "ee":
                _ss.MixKey(Curve25519.ComputeSharedSecret(_e, _re!));
                return 0;
            case "es":
                _ss.MixKey(_initiator
                    ? Curve25519.ComputeSharedSecret(_e, _rs!)
                    : Curve25519.ComputeSharedSecret(_s, _re!));
                return 0;
            case "se":
                _ss.MixKey(_initiator
                    ? Curve25519.ComputeSharedSecret(_s, _re!)
                    : Curve25519.ComputeSharedSecret(_e, _rs!));
                return 0;
        }
        return 0;
    }
}
