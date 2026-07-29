using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitChat.Core.Nostr;
using BitChat.Core.Services;

namespace BitChat.Bot.ViewModels;

public partial class ClientViewModel : ViewModelBase
{
    private ChatEngine? _engine;
    private string _identityInfo = "";
    private string _composeText = "";
    private string _recipientPubkey = "";
    private bool _isConnected;

    public ObservableCollection<ChatBubble> Messages { get; } = [];

    public string IdentityInfo { get => _identityInfo; private set => SetProperty(ref _identityInfo, value); }
    public string ComposeText { get => _composeText; set => SetProperty(ref _composeText, value); }
    public string RecipientPubkey { get => _recipientPubkey; set => SetProperty(ref _recipientPubkey, value); }
    public bool IsConnected { get => _isConnected; set => SetProperty(ref _isConnected, value); }

    public void Initialize(ChatEngine engine, string defaultRecipient)
    {
        _engine = engine;
        RecipientPubkey = defaultRecipient;
        IdentityInfo = $"npub: {engine.Identity.Npub}\nhex: {engine.Identity.PublicKeyHex}";
        _engine.OnMessageReceived += OnMessage;
    }

    private void OnMessage(Message msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Messages.Add(new ChatBubble
            {
                Sender = msg.SenderPubkey[..8] + "...",
                Content = msg.Content,
                Time = msg.Timestamp.ToString("HH:mm:ss"),
                IsSelf = false
            });
        });
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (_engine == null || string.IsNullOrWhiteSpace(ComposeText)) return;
        if (string.IsNullOrWhiteSpace(RecipientPubkey)) return;
        var text = ComposeText;
        ComposeText = "";
        Messages.Add(new ChatBubble
        {
            Sender = "Me",
            Content = text,
            Time = DateTimeOffset.Now.ToString("HH:mm:ss"),
            IsSelf = true
        });
        try
        {
            await _engine.SendMessageAsync(RecipientPubkey, text);
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Messages.Add(new ChatBubble
                {
                    Sender = "ERROR",
                    Content = ex.Message,
                    Time = DateTimeOffset.Now.ToString("HH:mm:ss"),
                    IsSelf = false
                });
            });
        }
    }
}

public class ChatBubble
{
    public string Sender { get; init; } = "";
    public string Content { get; init; } = "";
    public string Time { get; init; } = "";
    public bool IsSelf { get; init; }
    public IBrush BubbleColor => IsSelf ? new SolidColorBrush(Color.Parse("#E3F2FD")) : new SolidColorBrush(Color.Parse("#F5F5F5"));
    public HorizontalAlignment Alignment => IsSelf ? HorizontalAlignment.Right : HorizontalAlignment.Left;
}
