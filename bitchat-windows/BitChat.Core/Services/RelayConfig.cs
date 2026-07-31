namespace BitChat.Core.Services;

public static class RelayConfig
{
    private static readonly string[] Defaults =
    [
        "wss://relay.damus.io",
        "wss://nos.lol",
        "wss://relay.primal.net",
        "wss://offchain.pub"
    ];

    public static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BitChat", "relays.txt");

    public static string[] Load(string? customPath = null)
    {
        var path = customPath ?? ConfigPath;

        if (!File.Exists(path))
        {
            SaveDefaults(path);
            return Defaults;
        }

        try
        {
            var lines = File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith('#'))
                .Where(l => l.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
                         || l.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (lines.Length == 0)
                return Defaults;

            return lines;
        }
        catch
        {
            return Defaults;
        }
    }

    public static void SaveDefaults(string? path = null)
    {
        var p = path ?? ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var content = string.Join(Environment.NewLine, Defaults);
        File.WriteAllText(p, content);
    }
}
