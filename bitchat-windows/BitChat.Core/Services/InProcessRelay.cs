using System.Collections.Concurrent;
using System.Text.Json;
using BitChat.Core.Nostr;

namespace BitChat.Core.Services;

public class InProcessRelayHub
{
    private readonly ConcurrentDictionary<InProcessRelayClient, byte> _clients = [];
    private readonly ConcurrentDictionary<string, NostrEvent> _storedEvents = [];
    private readonly ConcurrentDictionary<(InProcessRelayClient client, string subId), (NostrFilter Filter, Action<NostrEvent> Handler)> _subscriptions = [];

    internal void Register(InProcessRelayClient client)
    {
        _clients.TryAdd(client, 0);
    }

    internal void Unregister(InProcessRelayClient client)
    {
        _clients.TryRemove(client, out _);
        var keys = _subscriptions.Keys.Where(k => k.client == client).ToList();
        foreach (var key in keys) _subscriptions.TryRemove(key, out _);
    }

    internal string AddSubscription(InProcessRelayClient client, string subId, NostrFilter filter, Action<NostrEvent> handler)
    {
        _subscriptions[(client, subId)] = (filter, handler);
        return subId;
    }

    internal void RemoveSubscription(InProcessRelayClient client, string subId)
    {
        _subscriptions.TryRemove((client, subId), out _);
    }

    internal Task ProcessEvent(InProcessRelayClient publisher, NostrEvent evt, Action<string, bool, string>? onOk)
    {
        if (_storedEvents.ContainsKey(evt.Id))
        {
            onOk?.Invoke(evt.Id, true, "duplicate: event already stored");
            return Task.CompletedTask;
        }
        _storedEvents[evt.Id] = evt;
        onOk?.Invoke(evt.Id, true, "");

        // Send to all OTHER clients with matching filters
        // Fire-and-forget to avoid blocking the publisher
        foreach (var ((client, subId), (filter, handler)) in _subscriptions)
        {
            if (client == publisher) continue;
            if (MatchesFilter(evt, filter))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await client.DeliverEvent(subId, evt);
                    }
                    catch { }
                });
            }
        }
        return Task.CompletedTask;
    }

    internal List<NostrEvent> QueryStored(string subId, InProcessRelayClient subscriber, NostrFilter filter)
    {
        var since = filter.Since ?? 0;
        return _storedEvents.Values
            .Where(e => MatchesFilter(e, filter))
            .OrderBy(e => e.CreatedAt)
            .ToList();
    }

    private static bool MatchesFilter(NostrEvent evt, NostrFilter filter)
    {
        if (filter.Ids != null && !filter.Ids.Contains(evt.Id)) return false;
        if (filter.Authors != null && !filter.Authors.Contains(evt.Pubkey)) return false;
        if (filter.Kinds != null && !filter.Kinds.Contains(evt.Kind)) return false;
        if (filter.Since.HasValue && evt.CreatedAt < filter.Since.Value) return false;
        if (filter.Until.HasValue && evt.CreatedAt > filter.Until.Value) return false;
        foreach (var (tagName, values) in filter.TagFilters)
        {
            bool tagMatches = evt.Tags.Any(tag => tag.Length >= 2 && tag[0] == tagName && values.Contains(tag[1]));
            if (!tagMatches) return false;
        }
        return true;
    }
}

public class InProcessRelayClient : INostrRelay
{
    private readonly InProcessRelayHub _hub;
    private readonly string _name;
    private bool _isConnected;
    private readonly Dictionary<string, Action<NostrEvent>> _localHandlers = [];

    public event Action<NostrEvent>? OnEvent;
    public event Action<string>? OnNotice;
    public event Action<string, bool, string>? OnOk;
    public Uri Url { get; }
    public bool IsConnected => _isConnected;

    internal InProcessRelayClient(InProcessRelayHub hub, string name)
    {
        _hub = hub;
        _name = name;
        Url = new Uri($"inproc://{name}");
    }

    public static (InProcessRelayClient, InProcessRelayClient) CreatePair()
    {
        var hub = new InProcessRelayHub();
        var a = new InProcessRelayClient(hub, "alice");
        var b = new InProcessRelayClient(hub, "bob");
        return (a, b);
    }

    public Task ConnectAsync()
    {
        _hub.Register(this);
        _isConnected = true;
        return Task.CompletedTask;
    }

    public async Task Subscribe(string subId, NostrFilter filter, Action<NostrEvent> handler)
    {
        _localHandlers[subId] = handler;
        _hub.AddSubscription(this, subId, filter, handler);

        var stored = _hub.QueryStored(subId, this, filter);
        foreach (var evt in stored)
        {
            await DeliverEvent(subId, evt);
        }
    }

    public Task Unsubscribe(string subId)
    {
        _localHandlers.Remove(subId);
        _hub.RemoveSubscription(this, subId);
        return Task.CompletedTask;
    }

    internal async Task DeliverEvent(string subId, NostrEvent evt)
    {
        OnEvent?.Invoke(evt);
        if (_localHandlers.TryGetValue(subId, out var handler))
            handler(evt);
        // Extra safety: ensure the await yields
        await Task.Yield();
    }

    internal void DeliverNotice(string notice)
    {
        OnNotice?.Invoke(notice);
    }

    public async Task PublishEvent(NostrEvent evt)
    {
        await _hub.ProcessEvent(this, evt, (id, ok, reason) =>
        {
            OnOk?.Invoke(id, ok, reason);
        });
    }

    public Task DisconnectAsync()
    {
        _hub.Unregister(this);
        _isConnected = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _hub.Unregister(this);
    }
}
