using BitChat.Bot.ViewModels;

namespace BitChat.Bot.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public BotViewModel Bot { get; } = new();
}
