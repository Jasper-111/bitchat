using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BitChat.Core.Nostr;

namespace BitChat.Core.Services;

public class MiniRelayServer : IDisposable
{
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private readonly ConcurrentDictionary<string, NostrEvent> _events = []; // id -> event
    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = []; // subId -> handler

    public event Action<string>? OnLog;

    public bool IsRunning => _listener?.IsListening ?? false;

    public MiniRelayServer(int port = 4869)
    {
        _port = port;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        Log($"Local relay listening on ws://localhost:{_port}");
        _acceptLoop = AcceptLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        Log("Local relay stopped");
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                if (ctx.Request.IsWebSocketRequest)
                {
                    var wsCtx = await ctx.AcceptWebSocketAsync(null);
                    _ = HandleClient(wsCtx.WebSocket, ct);
                }
                else
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                }
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch { }
        }
    }

    private async Task HandleClient(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, linkedCts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await ProcessClientMessage(json, ws, linkedCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            foreach (var (subId, sub) in _subscribers)
            {
                if (sub.Socket == ws)
                    _subscribers.TryRemove(subId, out _);
            }
            if (ws.State == WebSocketState.Open)
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                catch { }
            ws.Dispose();
        }
    }

    private async Task ProcessClientMessage(string json, WebSocket ws, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return;

            var cmd = root[0].GetString();
            switch (cmd)
            {
                case "EVENT":
                    await HandleEvent(root, ws, ct);
                    break;
                case "REQ":
                    await HandleReq(root, ws, ct);
                    break;
                case "CLOSE":
                    HandleClose(root, ws);
                    break;
            }
        }
        catch { }
    }

    private async Task HandleEvent(JsonElement root, WebSocket ws, CancellationToken ct)
    {
        if (root.GetArrayLength() < 2) return;

        var evt = JsonSerializer.Deserialize<NostrEvent>(root[1].GetRawText());
        if (evt == null || string.IsNullOrEmpty(evt.Id) || string.IsNullOrEmpty(evt.Pubkey)) return;
        if (string.IsNullOrEmpty(evt.Sig)) return;

        if (_events.ContainsKey(evt.Id))
        {
            await SendTo(ws, new object[] { "OK", evt.Id, true, "duplicate: event already stored" });
            return;
        }

        _events[evt.Id] = evt;
        Log($"Stored event {evt.Id[..8]}... kind={evt.Kind}");

        await SendTo(ws, new object[] { "OK", evt.Id, true, "" });

        foreach (var (subId, sub) in _subscribers)
        {
            if (sub.Filters.Any(f => MatchesFilter(evt, f)))
            {
                try
                {
                    await SendTo(sub.Socket, new object[] { "EVENT", subId, evt });
                }
                catch { }
            }
        }
    }

    private async Task HandleReq(JsonElement root, WebSocket ws, CancellationToken ct)
    {
        if (root.GetArrayLength() < 3) return;
        var subId = root[1].GetString()!;

        var filters = new List<NostrFilter>();
        for (int i = 2; i < root.GetArrayLength(); i++)
        {
            var filter = ParseFilter(root[i]);
            if (filter != null) filters.Add(filter);
        }

        _subscribers[subId] = new Subscriber(ws, filters);

        var matched = _events.Values.Where(e => filters.Any(f => MatchesFilter(e, f))).ToList();
        foreach (var evt in matched)
        {
            try { await SendTo(ws, new object[] { "EVENT", subId, evt }); }
            catch { return; }
        }
        try { await SendTo(ws, new object[] { "EOSE", subId }); }
        catch { }
    }

    private void HandleClose(JsonElement root, WebSocket ws)
    {
        if (root.GetArrayLength() < 2) return;
        var subId = root[1].GetString()!;
        if (_subscribers.TryGetValue(subId, out var sub) && sub.Socket == ws)
            _subscribers.TryRemove(subId, out _);
    }

    private static NostrFilter? ParseFilter(JsonElement elem)
    {
        if (elem.ValueKind != JsonValueKind.Object) return null;

        var filter = new NostrFilter();

        if (elem.TryGetProperty("ids", out var ids))
            filter.Ids = ids.EnumerateArray().Select(j => j.GetString()!).ToList();

        if (elem.TryGetProperty("authors", out var authors))
            filter.Authors = authors.EnumerateArray().Select(j => j.GetString()!).ToList();

        if (elem.TryGetProperty("kinds", out var kinds))
            filter.Kinds = kinds.EnumerateArray().Select(j => j.GetInt32()).ToList();

        if (elem.TryGetProperty("since", out var since))
            filter.Since = since.GetInt32();

        if (elem.TryGetProperty("until", out var until))
            filter.Until = until.GetInt32();

        if (elem.TryGetProperty("limit", out var limit))
            filter.Limit = limit.GetInt32();

        foreach (var prop in elem.EnumerateObject())
        {
            if (prop.Name.StartsWith('#'))
            {
                var tagName = prop.Name[1..];
                filter.TagFilters[tagName] = prop.Value.EnumerateArray()
                    .Select(j => j.GetString()!).ToList();
            }
        }

        return filter;
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
            bool tagMatches = evt.Tags.Any(tag =>
                tag.Length >= 2 && tag[0] == tagName && values.Contains(tag[1]));
            if (!tagMatches) return false;
        }

        return true;
    }

    private async Task SendTo(WebSocket ws, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        if (ws.State == WebSocketState.Open)
            await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private void Log(string msg) => OnLog?.Invoke(msg);

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        _cts?.Dispose();
    }

    private record Subscriber(WebSocket Socket, List<NostrFilter> Filters);
}
