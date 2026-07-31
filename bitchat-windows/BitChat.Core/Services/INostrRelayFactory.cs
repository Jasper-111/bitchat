namespace BitChat.Core.Services;

public interface INostrRelayFactory
{
    INostrRelay Create(Uri url);
}
