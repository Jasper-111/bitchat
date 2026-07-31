using System.Security.Cryptography;
using System.Text.Json;
using BitChat.Core.Nostr;

namespace BitChat.Core.Services;

public sealed class FileIdentityStore : IIdentityStore
{
    private readonly string _filePath;

    public FileIdentityStore(string? customPath = null)
    {
        var folder = customPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BitChat");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "identity.dat");
    }

    public bool Exists() => File.Exists(_filePath);

    public async Task<NostrIdentity?> LoadAsync()
    {
        if (!File.Exists(_filePath)) return null;

        var protectedBytes = await File.ReadAllBytesAsync(_filePath);
        var jsonBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        var dto = JsonSerializer.Deserialize<IdentityDto>(jsonBytes);
        if (dto?.PrivateKeyHex == null) return null;

        var privateKey = Convert.FromHexString(dto.PrivateKeyHex);
        return NostrIdentity.FromPrivateKey(privateKey);
    }

    public Task SaveAsync(NostrIdentity identity)
    {
        var dto = new IdentityDto
        {
            PrivateKeyHex = Convert.ToHexString(identity.PrivateKey).ToLowerInvariant(),
            SavedAt = DateTimeOffset.UtcNow
        };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dto);
        var protectedBytes = ProtectedData.Protect(jsonBytes, null, DataProtectionScope.CurrentUser);
        return File.WriteAllBytesAsync(_filePath, protectedBytes);
    }

    private sealed class IdentityDto
    {
        public string PrivateKeyHex { get; set; } = "";
        public DateTimeOffset SavedAt { get; set; }
    }
}
