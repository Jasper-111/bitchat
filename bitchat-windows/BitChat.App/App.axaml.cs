using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BitChat.App.ViewModels;
using BitChat.App.Views;
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
            string? localRole = null;

            if (isLocal)
            {
                var port = 4869;
                // Try to bind port — if it fails, another instance is already hosting
                TcpListener? testListener = null;
                try
                {
                    testListener = new TcpListener(IPAddress.Loopback, port);
                    testListener.Start();
                    // Port was free — this instance hosts the relay
                    _localRelay = new MiniRelayServer(port);
                    _localRelay.Start();
                    localRole = $"host :{port}";
                }
                catch (SocketException)
                {
                    // Port in use — connect to the existing relay
                    localRole = $"guest → :{port}";
                }
                finally
                {
                    try { testListener?.Stop(); } catch { }
                }

                relayUrls = [$"ws://127.0.0.1:{port}"];
            }
            else
            {
                relayUrls = [
                    "wss://relay.damus.io",
                    "wss://nos.lol",
                    "wss://relay.primal.net",
                    "wss://offchain.pub"
                ];
            }

            var vm = new MainViewModel(relayUrls);
            if (isLocal)
                vm.Status = $"Local Relay :{4869} ({localRole})";

            desktop.MainWindow = new MainWindow { DataContext = vm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    // MiniRelayServer is IDisposable but not registered in the DI container.
    // The process exit cleans it up. For a proper shutdown, we'd dispose in
    // OnFrameworkInitializationCompleted override or IClassicDesktopStyleApplicationLifetime.Exit.
}
