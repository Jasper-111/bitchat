using Avalonia.Controls;
using Avalonia.Input;
using BitChat.Bot.ViewModels;

namespace BitChat.Bot.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnComposeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm && vm.Client.SendCommand.CanExecute(null))
        {
            vm.Client.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
