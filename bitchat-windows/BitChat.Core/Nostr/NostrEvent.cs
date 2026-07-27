using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BitChat.Core.Crypto;

namespace BitChat.Core.Nostr;

public class NostrEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("pubkey")]
    public string Pubkey { get; set; } = "";

    [JsonPropertyName("created_at")]
    public int CreatedAt { get; set; }

    [JsonPropertyName("kind")]
    public int Kind { get; set; }

    [JsonPropertyName("tags")]
    public List<string[]> Tags { get; set; } = [];

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("sig")]
    public string? Sig { get; set; }

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public NostrEvent() { }

    public NostrEvent(string pubkey, int kind, List<string[]> tags, string content)
    {
        Pubkey = pubkey;
        CreatedAt = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Kind = kind;
        Tags = tags;
        Content = content;
    }

    public string ComputeId()
    {
        var canonical = new object[]
        {
            0,
            Pubkey,
            CreatedAt,
            Kind,
            Tags,
            Content
        };
        var json = JsonSerializer.Serialize(canonical, CanonicalOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Sign(NostrIdentity identity)
    {
        var id = ComputeId();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id)); // event hash
        var sig = Secp256k1Helper.SchnorrSign(identity.PrivateKey, hash);
        Id = id;
        Sig = Convert.ToHexString(sig).ToLowerInvariant();
    }

    public bool VerifySignature()
    {
        if (Sig == null || Sig.Length != 128) return false;
        var expectedId = ComputeId();
        if (expectedId != Id) return false;
        var sigBytes = Convert.FromHexString(Sig);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Id));
        var pubBytes = Convert.FromHexString(Pubkey);
        return Secp256k1Helper.SchnorrVerify(pubBytes, hash, sigBytes);
    }

    public string ToJson() => JsonSerializer.Serialize(this, CanonicalOptions);

    public static NostrEvent? FromJson(string json) =>
        JsonSerializer.Deserialize<NostrEvent>(json);

    public static NostrEvent? FromJsonArray(JsonElement element)
    {
        // Nostr relay sends events as ["EVENT", "subId", {event object}]
        if (element.ValueKind == JsonValueKind.Array &&
            element.GetArrayLength() >= 2 &&
            element[0].GetString() == "EVENT")
        {
            var evtElem = element[2].ValueKind == JsonValueKind.Object ? element[2] : element[1];
            return JsonSerializer.Deserialize<NostrEvent>(evtElem.GetRawText());
        }
        return null;
    }
}
