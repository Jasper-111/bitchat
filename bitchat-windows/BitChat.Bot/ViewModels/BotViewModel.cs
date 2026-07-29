using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private int _messagesReceived;
    private int _messagesSent;
    private int _receiptsReceived;
    private TaskCompletionSource<bool>? _selftestCompletion;
    private Func<string, Task>? _clipboardSetter;

    public string IdentityInfo { get => _identityInfo; private set => SetProperty(ref _identityInfo, value); }
    public string Logs { get => _logs; set => SetProperty(ref _logs, value); }
    public string RecipientPubkey { get => _recipientPubkey; set => SetProperty(ref _recipientPubkey, value); }
    public string ResponseTemplate { get => _responseTemplate; set => SetProperty(ref _responseTemplate, value); }
    public bool AutoReply { get => _autoReply; set => SetProperty(ref _autoReply, value); }
    public int MessagesReceived => _messagesReceived;
    public int MessagesSent => _messagesSent;
    public int ReceiptsReceived => _receiptsReceived;

    [RelayCommand]
    private async Task CopyNpub()
    {
        if (_engine == null || _clipboardSetter == null) return;
        await _clipboardSetter(_engine.Identity.Npub);
    }

    [RelayCommand]
    private async Task CopyHex()
    {
        if (_engine == null || _clipboardSetter == null) return;
        await _clipboardSetter(_engine.Identity.PublicKeyHex);
    }

    public void Initialize(ChatEngine engine, Func<string, Task>? clipboardSetter = null)
    {
        _engine = engine;
        _clipboardSetter = clipboardSetter;
        IdentityInfo = $"npub: {engine.Identity.Npub}\nhex: {engine.Identity.PublicKeyHex}";
        _engine.OnLog += (ts, msg) => AppendLog($"[{ts}] {msg}");
        _engine.OnMessageReceived += OnMessageReceived;
        _engine.OnReceiptReceived += OnReceiptReceived;
        AppendLog("[INIT] Bot ready");
    }

    public Task<bool> RunSelfTest(string targetPubkey)
    {
        _selftestCompletion = new TaskCompletionSource<bool>();
        _messagesReceived = 0;
        _messagesSent = 0;
        _receiptsReceived = 0;

        AppendLog("[SELFTEST] Starting...");
        _ = RunSelfTestAsync(targetPubkey);
        return _selftestCompletion.Task;
    }

    private async Task RunSelfTestAsync(string targetPubkey)
    {
        try
        {
            var testPhrases = new[] { "ping", "hello", "test-123", "selftest-complete" };
            var repliesExpected = new HashSet<string>();

            foreach (var phrase in testPhrases)
            {
                repliesExpected.Add("BOT: " + phrase);
                if (_engine != null)
                {
                    await _engine.SendMessageAsync(targetPubkey, phrase);
                    _messagesSent++;
                    AppendLog($"[SELFTEST] Sent: {phrase}");
                }
                await Task.Delay(300);
            }

            await Task.Delay(2000);

            var success = _messagesReceived >= 3 && _receiptsReceived >= 1;
            AppendLog(success
                ? $"[SELFTEST] PASS: received={_messagesReceived} receipts={_receiptsReceived}"
                : $"[SELFTEST] FAIL: received={_messagesReceived} receipts={_receiptsReceived} expected>=3/1");
            _selftestCompletion?.TrySetResult(success);
        }
        catch (Exception ex)
        {
            AppendLog($"[SELFTEST] ERROR: {ex.Message}");
            _selftestCompletion?.TrySetResult(false);
        }
    }

    private void AppendLog(string line)
    {
        Dispatcher.UIThread.Post(() => Logs += line + "\n");
    }

    private async void OnMessageReceived(Message msg)
    {
        _messagesReceived++;
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
            {
                await _engine.SendMessageAsync(RecipientPubkey, reply);
                _messagesSent++;
            }
            AppendLog($"[OUT] {reply[..Math.Min(reply.Length, 60)]}");
        }
    }

    private void OnReceiptReceived(string senderPubkey, string messageId, string receiptType)
    {
        _receiptsReceived++;
        var shortId = messageId.Length > 16 ? messageId[..16] : messageId;
        AppendLog($"[RECEIPT] {receiptType} for {shortId} from {senderPubkey[..8]}...");
    }
}
