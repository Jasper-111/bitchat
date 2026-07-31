using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitChat.Core.Crypto;
using BitChat.Core.Nostr;
using BitChat.Core.Services;
using BitChat.Core.Services.Transport;

namespace BitChat.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly NostrIdentity _identity;
    private readonly NostrTransport _transport;
    private readonly MessageRouter _router;

    private string _status = "Disconnected";
    private string _npub = "";
    private string _recipientPubkey = "";
    private string _recipientDisplay = "";
    private string _composeText = "";
    private bool _isConnected;

    public ObservableCollection<ChatBubble> Messages { get; } = [];

    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Npub { get => _npub; set => SetProperty(ref _npub, value); }

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
    public bool IsLocal { get; }

    public MainViewModel(NostrIdentity identity, string[] relayUrls, INostrRelayFactory? relayFactory = null)
    {
        _identity = identity;
        IsLocal = relayUrls.Length == 1 && relayUrls[0].StartsWith("ws://");

        Npub = identity.Npub;

        var factory = relayFactory ?? new DefaultNostrRelayFactory();
        _transport = new NostrTransport(identity, factory, relayUrls);
        _transport.OnLog += (msg) =>
        {
            if (msg.StartsWith("Failed to connect"))
                Status = msg[..Math.Min(msg.Length, 50)];
        };

        _router = new MessageRouter([_transport]);
        _router.OnTransportEvent += OnTransportEvent;
        _router.WireEvents();

        if (IsLocal)
        {
            Status = "Local Relay — autoconnect...";
            _ = ConnectAsync();
        }
    }

    private void OnTransportEvent(TransportEvent evt)
    {
        switch (evt.Type)
        {
            case TransportEventType.RelayConnected:
                Status = IsLocal
                    ? $"Local Relay — {_identity.Npub[..12]}..."
                    : $"Connected — {_identity.Npub[..12]}...";
                IsConnected = true;
                break;

            case TransportEventType.RelayDisconnected:
                IsConnected = false;
                Status = "Disconnected";
                break;

            case TransportEventType.PrivateMessageReceived:
                Messages.Add(new ChatBubble
                {
                    Sender = evt.PeerID.ToString()[..8] + "...",
                    Content = evt.Content ?? "",
                    Time = evt.Timestamp.ToString("HH:mm"),
                    IsSelf = false
                });
                if (string.IsNullOrWhiteSpace(_recipientPubkey) && evt.Content != null)
                    RecipientPubkey = evt.PeerID.ToString();
                break;
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
            await _router.StopAllAsync();
            IsConnected = false;
            Status = "Disconnected";
            return;
        }

        Status = "Connecting...";
        await _router.StartAllAsync();
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(ComposeText)) return;

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
            var peerID = new PeerID(Convert.FromHexString(_recipientPubkey[..16]));
            _transport.RegisterPeer(peerID, _recipientPubkey);
            await _router.SendPrivateMessage(text, peerID);
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
