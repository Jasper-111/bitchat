using BitChat.Core.Nostr;
using BitChat.Core.Services;

var (aliceRelay, bobRelay) = InProcessRelayClient.CreatePair();

var client = new ChatEngine(NostrIdentity.Generate());
var bot = new ChatEngine(NostrIdentity.Generate());

bot.OnMessageReceived += async msg =>
{
    Console.WriteLine($"[BOT received] {msg.Content}");
    await bot.SendMessageAsync(msg.SenderPubkey, "Echo: " + msg.Content);
};

client.OnMessageReceived += msg =>
    Console.WriteLine($"[CLIENT received] {msg.Content}");

client.OnLog += (ts, msg) => Console.WriteLine($"[log] {msg}");
bot.OnLog += (ts, msg) => Console.WriteLine($"[log] {msg}");

await client.ConnectToRelay(aliceRelay);
await bot.ConnectToRelay(bobRelay);

Console.WriteLine();
Console.WriteLine($"Bot npub: {bot.Identity.Npub} (hex: {bot.Identity.PublicKeyHex[..16]}...)");
Console.WriteLine("Type messages (empty line to quit):");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var line = Console.ReadLine();
    if (string.IsNullOrEmpty(line)) break;

    try
    {
        await client.SendMessageAsync(bot.Identity.PublicKeyHex, line);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
}

Console.WriteLine("Exiting...");
await Task.WhenAll(client.DisconnectAsync(), bot.DisconnectAsync());
