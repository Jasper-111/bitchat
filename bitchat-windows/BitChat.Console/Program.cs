using BitChat.Core.Nostr;
using BitChat.Core.Services;
using BitChat.Core.Services.Transport;

var clientId = NostrIdentity.Generate();
var botId = NostrIdentity.Generate();

var clientPeer = new PeerID(Convert.FromHexString(clientId.PublicKeyHex[..16]));
var botPeer = new PeerID(Convert.FromHexString(botId.PublicKeyHex[..16]));

var hub = new InProcessRelayHub();
var clientFactory = new InProcessRelayFactory(hub);
var botFactory = new InProcessRelayFactory(hub);

var client = new NostrTransport(clientId, clientFactory, ["inproc://hub"], clientPeer);
var bot = new NostrTransport(botId, botFactory, ["inproc://hub"], botPeer);

bot.OnEvent += async evt =>
{
    if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content != null)
    {
        Console.WriteLine($"[BOT received] {evt.Content}");
        await bot.SendPrivateMessage("Echo: " + evt.Content, clientPeer);
    }
};

client.OnEvent += evt =>
{
    if (evt.Type == TransportEventType.PrivateMessageReceived && evt.Content != null)
        Console.WriteLine($"[CLIENT received] {evt.Content}");
};

client.OnLog += msg => Console.WriteLine($"[log] {msg}");
bot.OnLog += msg => Console.WriteLine($"[log] {msg}");

client.RegisterPeer(botPeer, botId.PublicKeyHex);
bot.RegisterPeer(clientPeer, clientId.PublicKeyHex);

await client.StartAsync();
await bot.StartAsync();

Console.WriteLine();
Console.WriteLine($"Bot npub: {botId.Npub} (hex: {botId.PublicKeyHex[..16]}...)");
Console.WriteLine("Type messages (empty line to quit):");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (string.IsNullOrEmpty(line)) break;

    try
    {
        await client.SendPrivateMessage(line, botPeer);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
}

Console.WriteLine("Exiting...");
await Task.WhenAll(client.StopAsync(), bot.StopAsync());
