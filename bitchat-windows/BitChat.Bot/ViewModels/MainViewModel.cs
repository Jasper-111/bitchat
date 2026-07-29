using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BitChat.Core.Nostr;
using BitChat.Core.Services;

namespace BitChat.Bot.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private ChatEngine? _clientEngine;
    private ChatEngine? _botEngine;
    private string _relayStatus = "";
    private string _testResults = "";

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BitChat");
    private static readonly string KeyFile = Path.Combine(DataDir, "bot.key");

    public ClientViewModel Client { get; }
    public BotViewModel Bot { get; }
    public string RelayStatus { get => _relayStatus; set => SetProperty(ref _relayStatus, value); }
    public string TestResults { get => _testResults; set => SetProperty(ref _testResults, value); }

    public MainViewModel()
    {
        var botIdentity = LoadOrCreateBotIdentity();
        var clientIdentity = NostrIdentity.Generate();

        _clientEngine = new ChatEngine(clientIdentity);
        _botEngine = new ChatEngine(botIdentity);

        Client = new ClientViewModel();
        Bot = new BotViewModel();

        Client.Initialize(_clientEngine, botIdentity.PublicKeyHex);
        Bot.Initialize(_botEngine);

        _ = ConnectBothAsync();
    }

    private async Task ConnectBothAsync()
    {
        // In-process relay: zero network, zero platform issues
        var (aliceRelay, bobRelay) = InProcessRelayClient.CreatePair();

        await Task.WhenAll(
            _clientEngine!.ConnectToRelay(aliceRelay),
            _botEngine!.ConnectToRelay(bobRelay)
        );

        Client.IsConnected = true;
        RelayStatus = "inproc:// (connected, no network)";
    }

    [RelayCommand]
    private async Task RunSelfTest()
    {
        if (_botEngine == null || _clientEngine == null) return;
        TestResults = "Running...";

        var success = await Bot.RunSelfTest(_clientEngine.Identity.PublicKeyHex);
        TestResults = success ? "PASS: All messages delivered and acknowledged" : "FAIL: Check logs";
    }

    private static NostrIdentity LoadOrCreateBotIdentity()
    {
        try
        {
            if (File.Exists(KeyFile))
            {
                var hex = File.ReadAllText(KeyFile).Trim();
                var privKey = Convert.FromHexString(hex);
                return NostrIdentity.FromPrivateKey(privKey);
            }
        }
        catch { }

        var newId = NostrIdentity.Generate();
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(KeyFile, Convert.ToHexString(newId.PrivateKey).ToLowerInvariant());
        }
        catch { }
        return newId;
    }
}
