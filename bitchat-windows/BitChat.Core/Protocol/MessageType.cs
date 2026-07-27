namespace BitChat.Core.Protocol;

public static class MessageType
{
    public const byte Announce = 0x01;
    public const byte Message = 0x02;
    public const byte Leave = 0x03;
    public const byte CourierEnvelope = 0x04;
    public const byte NoiseHandshake = 0x10;
    public const byte NoiseEncrypted = 0x11;
    public const byte Fragment = 0x20;
    public const byte RequestSync = 0x21;
    public const byte FileTransfer = 0x22;
    public const byte BoardPost = 0x23;
    public const byte PrekeyBundle = 0x24;
    public const byte GroupMessage = 0x25;
    public const byte Ping = 0x26;
    public const byte Pong = 0x27;
    public const byte NostrCarrier = 0x28;
    public const byte VoiceFrame = 0x29;
}

public static class NoisePayloadType
{
    public const byte PrivateMessage = 0x01;
    public const byte ReadReceipt = 0x02;
    public const byte Delivered = 0x03;
    public const byte GroupInvite = 0x06;
    public const byte GroupKeyUpdate = 0x07;
    public const byte VoiceFrame = 0x08;
    public const byte PrivateFile = 0x09; // legacy, decode-only
    public const byte PrivateFileCanonical = 0x20;
    public const byte VerifyChallenge = 0x10;
    public const byte VerifyResponse = 0x11;
    public const byte Vouch = 0x12;
    public const byte AuthenticatedPeerState = 0x21;

    public static byte DecodedRawValue(byte raw)
    {
        if (raw == PrivateFile) return PrivateFileCanonical;
        return raw;
    }

    public static bool IsKnown(byte raw) => raw switch
    {
        PrivateMessage => true,
        ReadReceipt => true,
        Delivered => true,
        PrivateFile => true,
        PrivateFileCanonical => true,
        _ => false
    };
}
