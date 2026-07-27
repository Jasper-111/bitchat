using System.Text.Json;

namespace BitChat.Core.Nostr;

public class NostrFilter
{
    public List<string>? Ids { get; set; }
    public List<string>? Authors { get; set; }
    public List<int>? Kinds { get; set; }
    public int? Since { get; set; }
    public int? Until { get; set; }
    public int? Limit { get; set; }
    public Dictionary<string, List<string>> TagFilters { get; set; } = [];

    public static NostrFilter GiftWrapsFor(string pubkeyHex, int sinceSeconds)
    {
        return new NostrFilter
        {
            Kinds = [1059],
            Since = sinceSeconds,
            Limit = 50,
            TagFilters = { ["p"] = [pubkeyHex] }
        };
    }

    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>();
        if (Ids != null) dict["ids"] = Ids;
        if (Authors != null) dict["authors"] = Authors;
        if (Kinds != null) dict["kinds"] = Kinds;
        if (Since.HasValue) dict["since"] = Since.Value;
        if (Until.HasValue) dict["until"] = Until.Value;
        if (Limit.HasValue) dict["limit"] = Limit.Value;
        foreach (var tag in TagFilters)
            dict[$"#{tag.Key}"] = tag.Value;
        return dict;
    }
}
