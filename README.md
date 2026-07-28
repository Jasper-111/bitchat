# BitChat for Windows

> Windows native port of [bitchat](https://github.com/permissionlesstech/bitchat) ·
> .NET 8 + Avalonia UI · No accounts, no servers, just keys

## About this port

This is a **Windows-native, parallel implementation** of the [original bitchat](https://github.com/permissionlesstech/bitchat) project. The original is beautifully crafted for iOS and macOS. I use Windows as my daily driver and wanted to bring the same decentralized, privacy-first messaging experience to this platform.

This port is maintained independently. It is not affiliated with or endorsed by the original bitchat authors.

### What I'm doing

- Building a native Windows client in **.NET 8 + Avalonia UI**, targeting the same Nostr relay network and bitchat binary wire format as the original
- Implementing the full protocol stack — NIP-44 v2 encryption, XChaCha20-Poly1305 AEAD, secp256k1 Schnorr signing — in **pure C#** with zero external crypto dependencies (only NBitcoin for EC operations)
- Keeping the Nostr private-envelope format (kind 14 → 13 → 1059 triple-wrap) **bit-for-bit compatible** with the original clients
- Starting with the Nostr + Tor internet path; Windows BLE APIs lack mesh networking support, so the offline transport will follow a different strategy
- Bundling a **local relay server** and **auto-reply bot** so a single developer can test the full send/receive loop without external infrastructure
- **In-process Tor** via Arti (Rust), compiled for Windows — same approach as the original, just a different FFI binding layer

### What I'm not doing

- **Forking in bad faith.** This is a parallel port. The original Apple clients remain the reference implementation
- **Changing the wire protocol or encryption scheme.** The binary packet format, message type constants, and Nostr envelope construction are locked to the original specification
- **Claiming feature parity.** See the status table below for what's implemented and what's not
- **Shipping prebuilt binaries** until Tor integration is complete and the build is auditable
- **Building yet another Nostr client.** This talks to other bitchat instances exclusively. Nostr relays are transport infrastructure, not the application protocol

## Feature status

### Implemented

| Feature | Status |
|---------|--------|
| Nostr identity (secp256k1 key generation, npub encoding) | Done |
| Nostr relay client (WebSocket REQ/EVENT/CLOSE) | Done |
| NIP-44 v2 encryption (XChaCha20-Poly1305, `v2:` format) | Done |
| Schnorr signature / ECDH (via NBitcoin) | Done |
| Nostr gift-wrap (kind 14 → 13 → 1059 triple-wrap) | Done |
| Private DMs over Nostr relays (send + receive) | Done |
| Binary wire format (14-byte header + TLV payload) | Done |
| `bitchat1:` encoding (binary → base64url prefix) | Done |
| Local relay server (MiniRelayServer, `ws://localhost:4869`) | Done |
| Auto-reply bot (echo testing, same-protocol peer simulation) | Done |
| X25519 key agreement (Curve25519 DH, RFC 7748 verified) | Done |
| Ed25519 signatures (sign / verify / key gen, BouncyCastle) | Done |
| ChaCha20-Poly1305 AEAD (pure C#, existing roundtrip verified) | Done |
| HChaCha20 subkey derivation (XChaCha20 step 1) | Done |
| Automated crypto test suite (20 tests, xUnit, RFC 7748 vectors) | Done |

### Defined but not yet wired

| Feature | Status |
|---------|--------|
| BLE transport (SimpleBLE scan + WinRT peripheral) | Scaffold ready |
| BleTestViewModel (BLE/Nostr dual-transport test panel) | Scaffold ready |
| Noise XX handshake (Curve25519 DH + HKDF + ChaChaPoly AEAD) | Crypto primitives ready |
| Courier envelopes (store-and-forward) | Protocol constants defined |
| Prekey bundles (forward-secret Noise sealing) | Protocol constants defined |
| Board posts, group messages, fragments, file transfers | Protocol constants defined |
| Voice frames, read receipts, delivery receipts | Protocol constants defined |
| Geohash presence (kind 20001) | Nostr kind constant defined |

### Planned

| Feature | Target |
|---------|--------|
| Tor integration (Arti, in-process SOCKS5) | Next |
| Identity persistence (`%LOCALAPPDATA%\bitchat\identity.json`) | Next |
| Command-line bot mode (no GUI, auto-connect) | Next |
| Multiple relay management from config file | After Tor |
| Location channels over Nostr | After Tor |
| Offline transport (WiFi Direct or LAN multicast) | Future |

## Project structure

```
bitchat-windows/
├── BitChat.Core/              # Protocol engine (net8.0-windows10.0.19041.0)
│   ├── Crypto/                # Curve25519, Ed25519, XChaCha20-Poly1305, HChaCha20, secp256k1, Bech32, Base64Url
│   ├── Interop/
│   │   ├── SimpleBLE/         # P/Invoke bindings to simpleble_c.dll (cross-platform BLE C library)
│   │   └── WinRT/             # WinRT BLE peripheral (advertising + GATT server via Windows APIs)
│   ├── Nostr/                 # NIP-44 v2, gift-wrap, WebSocket relay client
│   ├── Protocol/              # Binary wire format, message types, packet encoding
│   └── Services/
│       ├── ChatEngine.cs      # Nostr DM send/receive orchestrator
│       ├── MiniRelayServer.cs # Embedded Nostr relay for local testing
│       ├── Store/             # Peer registry
│       └── Transport/         # ITransport, BleTransport, NostrTransport, MessageRouter
├── BitChat.Core.Tests/        # xUnit crypto test suite (20 tests)
│   └── CryptoInteropTests.cs  # RFC 7748 X25519, Noise XX vectors, Ed25519 roundtrip
├── BitChat.App/               # Avalonia GUI chat client
├── BitChat.Bot/               # Echo bot + BLE test panel (BleTestViewModel)
├── scripts/
│   └── build_simpleble.bat    # Clone + CMake-build SimpleBLE C library → simpleble_c.dll
└── launcher.bat               # Interactive menu (app / bot / build / clean)
```

## Building and running

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 x64 or later

### Quick start

```cmd
cd bitchat-windows
launcher.bat
```

This builds the solution and launches the GUI chat client. The app generates a fresh Nostr identity on first launch — your npub is shown in the header bar.

### Testing with the bot

The bot is a **second identity** running locally. It connects to the same Nostr relays and auto-replies to incoming DMs. This lets you test the full send/receive loop on a single machine:

```cmd
# Terminal 1: launch the GUI client
launcher.bat

# Terminal 2: launch the bot
dotnet run --project BitChat.Bot
```

1. Click **Connect** in both windows — they'll connect to public Nostr relays
2. Copy the bot's pubkey hex into the GUI client's recipient field
3. Send a message — the bot echoes it back

### Build commands

```cmd
# Run crypto tests (20 automated tests, xUnit)
dotnet test BitChat.Core.Tests

# Debug build
dotnet build BitChat.sln -c Debug

# Release build
dotnet build BitChat.sln -c Release

# Run GUI client directly (skip build)
start BitChat.App\bin\Release\net8.0-windows10.0.19041.0\BitChat.App.exe
```

### Communication layer status

The communication stack is built in layers from the bottom up:

```
Layer 0: Crypto primitives           ← COMPLETE
  X25519 DH (RFC 7748 verified) · Ed25519 (roundtrip verified)
  XChaCha20-Poly1305 · SHA-256 · HMAC-SHA256

Layer 1: Noise XX handshake          ← NEXT (crypto ready, state machine pending)
  HKDF key derivation · 3-step XX pattern · CipherState with replay protection
  NoiseSessionManager (pool, collision, quarantine)

Layer 2: BLE transport               ← SCAFFOLD READY
  SimpleBLE C library scan/connect · WinRT GATT peripheral
  BleTransport discovery + GATT write (unencrypted, no auto-connect yet)

Layer 3: Nostr transport             ← DONE (DM path)
  ChatEngine: kind-14 → 13 → 1059 triple-wrap · MiniRelayServer for local testing

Layer 4: Message router              ← STUB (transport selection logic exists)
  MessageRouter: BLE-first, Nostr-fallback priority cascade
  Outbox, courier, retry queue — not implemented

Layer 5: Application protocols       ← NOT STARTED
  Prekey bundles · Courier store-and-forward · Bulletin board
  Gossip sync (GCS filters) · Location channels · Push-to-talk
```

## License

Same as upstream — public domain. See [LICENSE](../LICENSE).

## Upstream

For the original iOS/macOS project, see [UPSTREAM.md](UPSTREAM.md) or the [original repository](https://github.com/permissionlesstech/bitchat).
