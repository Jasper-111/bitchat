using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BitChat.App.ViewModels;
using BitChat.App.Views;
using BitChat.Core.Nostr;
using BitChat.Core.Services;

namespace BitChat.App;

public partial class App : Application
{
    private MiniRelayServer? _localRelay;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            var isLocal = args.Contains("--local");
            string[] relayUrls;

            if (isLocal)
            {
                var port = 4869;
                TcpListener? testListener = null;
                try
                {
                    testListener = new TcpListener(IPAddress.Loopback, port);
                    testListener.Start();
                    _localRelay = new MiniRelayServer(port);
                    _localRelay.Start();
                }
                catch (SocketException) { }
                finally
                {
                    try { testListener?.Stop(); } catch { }
                }

                relayUrls = [$"ws://127.0.0.1:{port}"];
            }
            else
            {
                relayUrls = RelayConfig.Load();
            }

            var identity = LoadOrCreateIdentity();
            var vm = new MainViewModel(identity, relayUrls);

            if (isLocal)
            {
                var role = _localRelay != null ? $"host :4869" : $"guest -> :4869";
                vm.Status = $"Local Relay ({role})";
            }

            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static NostrIdentity LoadOrCreateIdentity()
    {
        var store = new FileIdentityStore();

        if (store.Exists())
        {
            var existing = store.LoadAsync().GetAwaiter().GetResult();
            if (existing != null)
                return existing;
        }

        var identity = NostrIdentity.Generate();
        store.SaveAsync(identity).GetAwaiter().GetResult();
        return identity;
    }
}
