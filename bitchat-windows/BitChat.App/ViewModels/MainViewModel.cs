using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitChat.Core.Nostr;
using BitChat.Core.Services;

namespace BitChat.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private ChatEngine? _engine;
    private string _status = "Disconnected";
    private string _npub = "";
    private string _npubHex = "";
    private string _recipientPubkey = "";
    private string _composeText = "";
    private bool _isConnected;

    public ObservableCollection<ChatBubble> Messages { get; } = [];

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Npub { get => _npub; set => SetProperty(ref _npub, value); }
    public string NpubHex { get => _npubHex; set => SetProperty(ref _npubHex, value); }
    public string RecipientPubkey { get => _recipientPubkey; set => SetProperty(ref _recipientPubkey, value); }
    public string ComposeText { get => _composeText; set => SetProperty(ref _composeText, value); }
    public bool IsConnected { get => _isConnected; set => SetProperty(ref _isConnected, value); }

    private readonly string[] _relayUrls =
    [
        "wss://relay.damus.io",
        "wss://nos.lol",
        "wss://relay.primal.net",
        "wss://offchain.pub"
    ];

    public MainViewModel()
    {
        var identity = NostrIdentity.Generate();
        Npub = identity.Npub;
        NpubHex = identity.PublicKeyHex;
        _engine = new ChatEngine(identity);
        _engine.OnConnected += (id) =>
        {
            Status = $"Connected — {id.Npub[..12]}...";
            IsConnected = true;
        };
        _engine.OnLog += (_, msg) => { };
        _engine.OnMessageReceived += (msg) =>
        {
            Messages.Add(new ChatBubble
            {
                Sender = msg.SenderPubkey[..8] + "...",
                Content = msg.Content,
                Time = msg.Timestamp.ToString("HH:mm"),
                IsSelf = false
            });
            foreach (var p in _engine.KnownPeers)
            {
                if (!RecipientPubkey.Contains(p))
                    RecipientPubkey = p;
            }
        };
    }

    public ICommand SendCommand => _sendCommand ??= new AsyncRelayCommand(SendAsync);
    private ICommand? _sendCommand;

    public ICommand ConnectCommand => _connectCommand ??= new AsyncRelayCommand(ConnectAsync);
    private ICommand? _connectCommand;

    private async Task ConnectAsync()
    {
        if (IsConnected)
        {
            if (_engine != null) await _engine.DisconnectAsync();
            IsConnected = false;
            Status = "Disconnected";
            return;
        }
        Status = "Connecting...";
        if (_engine != null) await _engine.ConnectAsync(_relayUrls);
    }

    private async Task SendAsync()
    {
        if (_engine == null || string.IsNullOrWhiteSpace(ComposeText)) return;
        if (string.IsNullOrWhiteSpace(RecipientPubkey))
            return;
        var text = ComposeText;
        ComposeText = "";
        Messages.Add(new ChatBubble { Sender = "Me", Content = text, Time = DateTimeOffset.UtcNow.ToString("HH:mm"), IsSelf = true });
        await _engine.SendMessageAsync(RecipientPubkey, text);
    }
}

public class ChatBubble
{
    public string Sender { get; init; } = "";
    public string Content { get; init; } = "";
    public string Time { get; init; } = "";
    public bool IsSelf { get; init; }
    public string BubbleColor => IsSelf ? "#E3F2FD" : "#F5F5F5";
}
