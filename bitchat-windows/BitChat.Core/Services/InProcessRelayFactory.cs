namespace BitChat.Core.Services;

public sealed class InProcessRelayFactory : INostrRelayFactory
{
    private readonly InProcessRelayHub _hub;
    private int _counter;

    public InProcessRelayFactory(InProcessRelayHub? hub = null)
    {
        _hub = hub ?? new InProcessRelayHub();
    }

    public InProcessRelayHub Hub => _hub;

    public INostrRelay Create(Uri url)
    {
        var name = $"{url.Host}-{Interlocked.Increment(ref _counter)}";
        return new InProcessRelayClient(_hub, name);
    }
}
