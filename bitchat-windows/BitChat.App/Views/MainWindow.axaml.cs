using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BitChat.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnNpubClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
        {
            await Clipboard!.SetTextAsync(vm.Npub);
        }
    }

    private void OnComposeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ViewModels.MainViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
