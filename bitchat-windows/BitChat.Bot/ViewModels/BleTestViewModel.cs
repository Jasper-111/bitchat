using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitChat.Core.Interop.WinRT;
using BitChat.Core.Nostr;
using BitChat.Core.Services.Store;
using BitChat.Core.Services.Transport;

namespace BitChat.Bot.ViewModels;

public partial class BleTestViewModel : ViewModelBase
{
    private BleTransport? _ble;
    private NostrTransport? _nostr;
    private MessageRouter? _router;
    private PeerRegistry? _peerReg;
    private BleScanner? _winRtScanner;

    private string _bleStatus = "Idle";
    private string _nostrStatus = "Idle";
    private string _scanLog = "";
    private string _peerList = "";
    private string _composeText = "";
    private string _targetPeerHex = "";
    private bool _isBleStarted;
    private bool _isNostrStarted;

    public ObservableCollection<string> LogLines { get; } = [];

    // ═══════════════════════════════════════════════════════════
    //  Bound properties
    // ═══════════════════════════════════════════════════════════

    public string BLEStatus { get => _bleStatus; set => SetProperty(ref _bleStatus, value); }
    public string NostrStatus { get => _nostrStatus; set => SetProperty(ref _nostrStatus, value); }
    public string ScanLog { get => _scanLog; set => SetProperty(ref _scanLog, value); }
    public string PeerList { get => _peerList; set => SetProperty(ref _peerList, value); }
    public string ComposeText { get => _composeText; set => SetProperty(ref _composeText, value); }
    public string TargetPeerHex { get => _targetPeerHex; set => SetProperty(ref _targetPeerHex, value); }
    public bool IsBLEStarted { get => _isBleStarted; set => SetProperty(ref _isBleStarted, value); }
    public bool IsNostrStarted { get => _isNostrStarted; set => SetProperty(ref _isNostrStarted, value); }

    // ═══════════════════════════════════════════════════════════
    //  Commands
    // ═══════════════════════════════════════════════════════════

    [RelayCommand]
    private void StartBLE()
    {
        _ = StartBLEAsync();
    }

    [RelayCommand]
    private void StopBLE()
    {
        _ = StopBLEAsync();
    }

    [RelayCommand]
    private void StartNostr()
    {
        _ = StartNostrAsync();
    }

    [RelayCommand]
    private void StopNostr()
    {
        _ = StopNostrAsync();
    }

    [RelayCommand]
    private void ScanBLENow()
    {
        if (_ble != null)
        {
            _ble.StartScan();
            AppendLog("BLE scan triggered");
        }
    }

    [RelayCommand]
    private async Task SendBLEAsync()
    {
        if (_router == null || string.IsNullOrWhiteSpace(ComposeText)) return;
        if (string.IsNullOrWhiteSpace(TargetPeerHex)) return;

        try
        {
            var peerID = new PeerID(TargetPeerHex);
            var text = ComposeText;
            ComposeText = "";
            await _router.SendPrivateMessage(text, peerID);
            AppendLog($"[BLE SEND] → {peerID}: {text[..Math.Min(text.Length, 40)]}");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] BLE send: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SendNostrAsync()
    {
        if (_router == null || string.IsNullOrWhiteSpace(ComposeText)) return;
        if (string.IsNullOrWhiteSpace(TargetPeerHex)) return;

        try
        {
            var peerID = new PeerID(TargetPeerHex);
            var text = ComposeText;
            ComposeText = "";
            await _router.SendPrivateMessage(text, peerID);
            AppendLog($"[Nostr SEND] → {peerID}: {text[..Math.Min(text.Length, 40)]}");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Nostr send: {ex.Message}");
        }
    }

    [RelayCommand]
    private void WinRTScan()
    {
        if (_winRtScanner == null)
        {
            _winRtScanner = new BleScanner();
            _winRtScanner.OnLog += msg => Dispatcher.UIThread.Post(() => AppendLog($"[Scanner] {msg}"));
            _winRtScanner.OnAdvertisementReceived += (addr, rssi, data) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var hex = string.Join(" ", data.Take(Math.Min(data.Length, 27)).Select(b => b.ToString("X2")));
                    AppendLog($"[ADV] addr={addr:X} rssi={rssi} data=[{hex}]");
                });
            };
        }

        if (_winRtScanner.IsScanning)
        {
            _winRtScanner.Stop();
            AppendLog("WinRT scan stopped");
        }
        else
        {
            _winRtScanner.Start();
            AppendLog("WinRT scan started");
        }
    }

    [RelayCommand]
    private void DumpPeers()
    {
        if (_ble == null) return;
        var peers = _ble.GetPeerSnapshots();
        PeerList = string.Join("\n", peers.Select(p =>
            $"{p.Nickname} ({p.PeerID}) connected={p.IsConnected} seen={p.LastSeen:HH:mm:ss}"));
        AppendLog($"Peer dump: {peers.Count} peers");
    }

    // ═══════════════════════════════════════════════════════════
    //  Async start/stop
    // ═══════════════════════════════════════════════════════════

    private async Task StartBLEAsync()
    {
        if (_ble != null) return;

        var myPeer = new PeerID(Guid.NewGuid().ToByteArray());
        var nickname = $"win-{Environment.MachineName}"[..Math.Min(Environment.MachineName.Length + 4, 20)];

        _peerReg = new PeerRegistry();
        _peerReg.OnLog += msg => Dispatcher.UIThread.Post(() => AppendLog(msg));

        _ble = new BleTransport(myPeer, nickname);
        _ble.OnLog += msg => Dispatcher.UIThread.Post(() => AppendLog(msg));
        _ble.OnEvent += evt =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                AppendLog($"[EVENT] {evt.Type} peer={evt.PeerID}");
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                {
                    AppendLog($"  ← {evt.Nickname ?? evt.PeerID.ToString()[..8]}: {evt.Content}");
                }
            });
        };

        await _ble.StartAsync();
        IsBLEStarted = true;
        BLEStatus = "Running";
        AppendLog("BLE transport started");
    }

    private async Task StopBLEAsync()
    {
        if (_ble == null) return;
        await _ble.StopAsync();
        _ble.Dispose();
        _ble = null;
        IsBLEStarted = false;
        BLEStatus = "Stopped";
        AppendLog("BLE transport stopped");
    }

    private async Task StartNostrAsync()
    {
        if (_nostr != null) return;

        var identity = NostrIdentity.Generate();
        var myPeer = new PeerID(Convert.FromHexString(identity.PublicKeyHex[..16]));

        _nostr = new NostrTransport(identity, myPeer);
        _nostr.OnLog += msg => Dispatcher.UIThread.Post(() => AppendLog(msg));
        _nostr.OnEvent += evt =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                AppendLog($"[NOSTR-EVENT] {evt.Type} peer={evt.PeerID}");
                if (evt.Type == TransportEventType.PrivateMessageReceived)
                {
                    AppendLog($"  ← {evt.Content}");
                }
            });
        };

        await _nostr.StartAsync();
        IsNostrStarted = true;
        NostrStatus = $"Connected ({_nostr.Npub[..12]}...)";
        AppendLog($"Nostr transport started: npub={_nostr.Npub}");

        // Wire up router
        if (_ble != null && _nostr != null && _router == null)
        {
            _router = new MessageRouter(_ble, _nostr);
            _router.OnLog += msg => Dispatcher.UIThread.Post(() => AppendLog(msg));
            _router.WireEvents();
            _router.OnTransportEvent += evt =>
            {
                Dispatcher.UIThread.Post(() =>
                    AppendLog($"[ROUTER] {evt.Type} from {evt.PeerID}"));
            };
            AppendLog("MessageRouter wired (BLE + Nostr)");
        }
    }

    private async Task StopNostrAsync()
    {
        if (_nostr == null) return;
        _router = null;
        await _nostr.StopAsync();
        _nostr.Dispose();
        _nostr = null;
        IsNostrStarted = false;
        NostrStatus = "Disconnected";
        AppendLog("Nostr transport stopped");
    }

    // ═══════════════════════════════════════════════════════════

    private void AppendLog(string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{ts}] {msg}";
        LogLines.Add(line);
        ScanLog = string.Join("\n", LogLines.TakeLast(50));
    }
}
