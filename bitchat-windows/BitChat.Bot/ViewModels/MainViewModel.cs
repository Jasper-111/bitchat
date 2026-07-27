using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using BitChat.Core.Nostr;
using BitChat.Core.Services;

namespace BitChat.Bot.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MiniRelayServer _relay;
    private string _relayStatus = "";

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BitChat");
    private static readonly string KeyFile = Path.Combine(DataDir, "bot.key");

    public ClientViewModel Client { get; }
    public BotViewModel Bot { get; }
    public string RelayStatus { get => _relayStatus; set => SetProperty(ref _relayStatus, value); }

    public MainViewModel()
    {
        _relay = new MiniRelayServer(4869);
        _relay.OnLog += msg => RelayStatus = $"{msg}";

        var botIdentity = LoadOrCreateBotIdentity();
        var clientIdentity = NostrIdentity.Generate();

        var clientEngine = new ChatEngine(clientIdentity);
        var botEngine = new ChatEngine(botIdentity);

        Client = new ClientViewModel();
        Bot = new BotViewModel();

        _relay.Start();
        RelayStatus = $"ws://localhost:4869";

        Client.Initialize(clientEngine, botIdentity.PublicKeyHex);
        Bot.Initialize(botEngine);

        _ = ConnectBothAsync(clientEngine, botEngine);
    }

    private async Task ConnectBothAsync(ChatEngine clientEngine, ChatEngine botEngine)
    {
        var urls = new[] { "ws://localhost:4869" };
        await Task.WhenAll(
            clientEngine.ConnectAsync(urls),
            botEngine.ConnectAsync(urls)
        );
        Client.IsConnected = true;
        RelayStatus = $"ws://localhost:4869 (connected)";
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
