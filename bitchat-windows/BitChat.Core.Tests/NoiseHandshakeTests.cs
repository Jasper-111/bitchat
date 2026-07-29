using System.Security.Cryptography;
using BitChat.Core.Crypto;
using BitChat.Core.Crypto.Noise;

namespace BitChat.Core.Tests;

public class NoiseHandshakeTests
{
    [Fact]
    public void RandomKeys_FullRoundtrip()
    {
        var (initStatic, _) = Curve25519.GenerateKeyPair();
        var (respStatic, _) = Curve25519.GenerateKeyPair();

        var init = new HandshakeState(true, initStatic);
        var resp = new HandshakeState(false, respStatic);

        // Handshake
        var msg0 = init.WriteMessage("hello from init"u8.ToArray());
        var r0 = resp.ReadMessage(msg0);
        Assert.Equal("hello from init"u8.ToArray(), r0);

        var msg1 = resp.WriteMessage("hello from resp"u8.ToArray());
        var r1 = init.ReadMessage(msg1);
        Assert.Equal("hello from resp"u8.ToArray(), r1);

        var msg2 = init.WriteMessage("final handshake"u8.ToArray());
        var r2 = resp.ReadMessage(msg2);
        Assert.Equal("final handshake"u8.ToArray(), r2);

        var h1 = init.GetHandshakeHash();
        var h2 = resp.GetHandshakeHash();
        Assert.Equal(h1, h2);

        var (initSend, initRecv) = init.GetCipherStates();
        var (respSend, respRecv) = resp.GetCipherStates();

        for (int i = 0; i < 10; i++)
        {
            var plaintext = System.Text.Encoding.UTF8.GetBytes($"transport msg {i}");
            var ct = initSend.EncryptWithAd([], plaintext);
            var pt = respRecv.DecryptWithAd([], ct);
            Assert.Equal(plaintext, pt);

            var reply = System.Text.Encoding.UTF8.GetBytes($"reply {i}");
            var ct2 = respSend.EncryptWithAd([], reply);
            var pt2 = initRecv.DecryptWithAd([], ct2);
            Assert.Equal(reply, pt2);
        }
    }

    [Fact]
    public void Cacophony_Vector1_HandshakeHash()
    {
        var initStatic = Convert.FromHexString(
            "e61ef9919cde45dd5f82166404bd08e38bceb5dfdfded0a34c8df7ed542214d1");
        var initEphPriv = Convert.FromHexString(
            "893e28b9dc6ca8d611ab664754b8ceb7bac5117349a4439a6b0569da977c464a");
        var respStatic = Convert.FromHexString(
            "4a3acbfdb163dec651dfa3194dece676d437029c62a408b4c5ea9114246e4893");
        var respEphPriv = Convert.FromHexString(
            "bbdb4cdbd309f1a1f2e1456967fe288cadd6f712d65dc7b7793d5e63da6b375b");
        var prologue = Convert.FromHexString("4a6f686e2047616c74");

        var init = new HandshakeState(true, initStatic, prologue);
        init.SetEphemeral(initEphPriv);
        var resp = new HandshakeState(false, respStatic, prologue);
        resp.SetEphemeral(respEphPriv);

        var msg0 = init.WriteMessage("Ludwig von Mises"u8.ToArray());
        var r0 = resp.ReadMessage(msg0);
        Assert.Equal("Ludwig von Mises"u8.ToArray(), r0);

        var msg1 = resp.WriteMessage("Murray Rothbard"u8.ToArray());
        var r1 = init.ReadMessage(msg1);
        Assert.Equal("Murray Rothbard"u8.ToArray(), r1);

        var msg2 = init.WriteMessage("F. A. Hayek"u8.ToArray());
        var r2 = resp.ReadMessage(msg2);
        Assert.Equal("F. A. Hayek"u8.ToArray(), r2);

        var expectedHash = Convert.FromHexString(
            "c8e5f64e846193be2a834104c2a009868d6c9f3bd3c186299888b488b2f1f58e");
        Assert.Equal(expectedHash, init.GetHandshakeHash());
        Assert.Equal(expectedHash, resp.GetHandshakeHash());
    }
}
