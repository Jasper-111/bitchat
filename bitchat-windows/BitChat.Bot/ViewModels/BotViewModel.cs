using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitChat.Core.Nostr;
using BitChat.Core.Services;

namespace BitChat.Bot.ViewModels;

public partial class BotViewModel : ViewModelBase
{
    private ChatEngine? _engine;
    private string _status = "Disconnected";
    private string _npub = "";
    private string _npubHex = "";
    private string _logs = "";
    private string _recipientPubkey = "";
    private string _responseTemplate = "Echo: {0}";
    private bool _autoReply = true;
    private bool _isConnected;

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Npub { get => _npub; set => SetProperty(ref _npub, value); }
    public string NpubHex { get => _npubHex; set => SetProperty(ref _npubHex, value); }
    public string Logs { get => _logs; set => SetProperty(ref _logs, value); }
    public string RecipientPubkey { get => _recipientPubkey; set => SetProperty(ref _recipientPubkey, value); }
    public string ResponseTemplate { get => _responseTemplate; set => SetProperty(ref _responseTemplate, value); }
    public bool AutoReply { get => _autoReply; set => SetProperty(ref _autoReply, value); }
    public bool IsConnected { get => _isConnected; set => SetProperty(ref _isConnected, value); }

    private readonly string[] _relayUrls =
    [
        "wss://relay.damus.io",
        "wss://nos.lol",
        "wss://relay.primal.net",
        "wss://offchain.pub"
    ];

    public BotViewModel()
    {
        var identity = NostrIdentity.Generate();
        Npub = identity.Npub;
        NpubHex = identity.PublicKeyHex;
        _engine = new ChatEngine(identity);
        _engine.OnLog += (ts, msg) => Logs += $"[{ts}] {msg}\n";
        _engine.OnConnected += (id) =>
        {
            Status = $"Connected — {id.Npub[..12]}...";
            IsConnected = true;
        };
        _engine.OnMessageReceived += async (msg) =>
        {
            Logs += $"[IN] {msg.SenderPubkey[..8]}...: {msg.Content}\n";
            foreach (var p in _engine.KnownPeers)
            {
                if (!RecipientPubkey.Contains(p))
                    RecipientPubkey = p;
            }
            if (AutoReply && !string.IsNullOrEmpty(RecipientPubkey))
            {
                var reply = string.Format(ResponseTemplate, msg.Content);
                await Task.Delay(500);
                if (_engine != null)
                    await _engine.SendMessageAsync(RecipientPubkey, reply);
                Logs += $"[OUT] {reply[..Math.Min(reply.Length, 50)]}\n";
            }
        };
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsConnected)
        {
            if (_engine != null)
                await _engine.DisconnectAsync();
            IsConnected = false;
            Status = "Disconnected";
            return;
        }
        Status = "Connecting...";
        if (_engine != null)
            await _engine.ConnectAsync(_relayUrls);
    }
}
