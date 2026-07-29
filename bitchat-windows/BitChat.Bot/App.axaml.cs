using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BitChat.Bot.ViewModels;
using BitChat.Bot.Views;

namespace BitChat.Bot;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.DataContext = new MainViewModel(
                async text =>
                {
                    var tl = TopLevel.GetTopLevel(window);
                    if (tl?.Clipboard != null)
                        await tl.Clipboard.SetTextAsync(text);
                });
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}