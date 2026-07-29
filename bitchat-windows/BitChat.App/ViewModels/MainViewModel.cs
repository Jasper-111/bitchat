using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitChat.Core.Crypto;
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
    private string _recipientDisplay = "";
    private string _composeText = "";
    private bool _isConnected;
    private int _relayConnected;
    private int _relayTotal;
    private readonly string[] _relayUrls;

    public ObservableCollection<ChatBubble> Messages { get; } = [];

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Npub { get => _npub; set => SetProperty(ref _npub, value); }
    public string NpubHex { get => _npubHex; set => SetProperty(ref _npubHex, value); }

    public string RecipientPubkey
    {
        get => _recipientDisplay;
        set
        {
            _recipientDisplay = value;
            _recipientPubkey = ResolvePubkey(value);
            OnPropertyChanged();
        }
    }

    public string ComposeText { get => _composeText; set => SetProperty(ref _composeText, value); }
    public string ConnectText => IsConnected ? "Disconnect" : "Connect";
    public bool IsConnected { get => _isConnected; set => SetProperty(ref _isConnected, value); }
    public bool IsLocal => _relayUrls.Length == 1 && _relayUrls[0].StartsWith("ws://");

    public MainViewModel(string[]? relayUrls = null)
    {
        _relayUrls = relayUrls ?? [
            "wss://relay.damus.io",
            "wss://nos.lol",
            "wss://relay.primal.net",
            "wss://offchain.pub"
        ];
        _relayTotal = _relayUrls.Length;

        var identity = NostrIdentity.Generate();
        Npub = identity.Npub;
        NpubHex = identity.PublicKeyHex;
        _engine = new ChatEngine(identity);
        _engine.OnConnected += (id) =>
        {
            _relayConnected++;
            var suffix = IsLocal
                ? $"Local Relay — {id.Npub[..12]}..."
                : $"{_relayConnected}/{_relayTotal} relays — {id.Npub[..12]}...";
            Status = suffix;
            IsConnected = true;
        };
        _engine.OnLog += (_, msg) =>
        {
            if (msg.StartsWith("Failed to connect"))
                Status = $"{_relayConnected}/{_relayTotal} relays | {msg[..Math.Min(msg.Length, 50)]}";
        };
        _engine.OnMessageReceived += (msg) =>
        {
            Messages.Add(new ChatBubble
            {
                Sender = msg.SenderPubkey[..8] + "...",
                Content = msg.Content,
                Time = msg.Timestamp.ToString("HH:mm"),
                IsSelf = false
            });
            if (string.IsNullOrWhiteSpace(_recipientPubkey))
                RecipientPubkey = msg.SenderPubkey;
        };

        if (IsLocal)
        {
            Status = $"Local Relay — autoconnect...";
            _ = ConnectAsync();
        }
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
            _relayConnected = 0;
            Status = "Disconnected";
            return;
        }
        Status = $"Connecting 0/{_relayTotal}...";
        _relayConnected = 0;
        if (_engine != null)
        {
            await _engine.ConnectAsync(_relayUrls);
            if (_relayConnected == 0)
                Status = $"0/{_relayTotal} relays — check network or use [3] local";
        }
    }

    private async Task SendAsync()
    {
        if (_engine == null || string.IsNullOrWhiteSpace(ComposeText)) return;
        if (string.IsNullOrWhiteSpace(_recipientPubkey))
        {
            Messages.Add(new ChatBubble
            {
                Sender = "ERROR",
                Content = "Enter a recipient npub or hex first",
                Time = DateTimeOffset.Now.ToString("HH:mm"),
                IsSelf = false
            });
            return;
        }
        var text = ComposeText;
        ComposeText = "";
        Messages.Add(new ChatBubble
        {
            Sender = "Me",
            Content = text,
            Time = DateTimeOffset.Now.ToString("HH:mm"),
            IsSelf = true
        });
        try
        {
            await _engine.SendMessageAsync(_recipientPubkey, text);
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatBubble
            {
                Sender = "ERROR",
                Content = ex.Message,
                Time = DateTimeOffset.Now.ToString("HH:mm"),
                IsSelf = false
            });
        }
    }

    private static string ResolvePubkey(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "";

        if (trimmed.StartsWith("npub1", StringComparison.Ordinal))
        {
            try
            {
                var (hrp, data) = Bech32.Decode(trimmed);
                if (hrp == "npub" && data.Length == 32)
                    return Convert.ToHexString(data).ToLowerInvariant();
            }
            catch { return trimmed; }
        }

        if (trimmed.Length == 64 && trimmed.All(c => char.IsAsciiHexDigit(c)))
            return trimmed.ToLowerInvariant();

        return trimmed;
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
