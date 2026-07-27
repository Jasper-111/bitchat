using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using BitChat.Core.Services;

namespace BitChat.Bot.ViewModels;

public partial class BotViewModel : ViewModelBase
{
    private ChatEngine? _engine;
    private string _identityInfo = "";
    private string _logs = "";
    private string _recipientPubkey = "";
    private string _responseTemplate = "Echo: {0}";
    private bool _autoReply = true;

    public string IdentityInfo { get => _identityInfo; private set => SetProperty(ref _identityInfo, value); }
    public string Logs { get => _logs; set => SetProperty(ref _logs, value); }
    public string RecipientPubkey { get => _recipientPubkey; set => SetProperty(ref _recipientPubkey, value); }
    public string ResponseTemplate { get => _responseTemplate; set => SetProperty(ref _responseTemplate, value); }
    public bool AutoReply { get => _autoReply; set => SetProperty(ref _autoReply, value); }

    public void Initialize(ChatEngine engine)
    {
        _engine = engine;
        IdentityInfo = $"npub: {engine.Identity.Npub}\nhex: {engine.Identity.PublicKeyHex}";
        _engine.OnLog += (ts, msg) => AppendLog($"[{ts}] {msg}");
        _engine.OnMessageReceived += OnMessageReceived;
        AppendLog("[INIT] Bot ready");
    }

    private void AppendLog(string line)
    {
        Dispatcher.UIThread.Post(() => Logs += line + "\n");
    }

    private async void OnMessageReceived(Message msg)
    {
        AppendLog($"[IN] {msg.SenderPubkey[..8]}...: {msg.Content}");

        Dispatcher.UIThread.Post(() =>
        {
            if (string.IsNullOrWhiteSpace(RecipientPubkey))
                RecipientPubkey = msg.SenderPubkey;
        });

        if (AutoReply && !string.IsNullOrEmpty(RecipientPubkey))
        {
            var reply = string.Format(ResponseTemplate, msg.Content);
            await Task.Delay(200);
            if (_engine != null)
                await _engine.SendMessageAsync(RecipientPubkey, reply);
            AppendLog($"[OUT] {reply[..Math.Min(reply.Length, 60)]}");
        }
    }
}
