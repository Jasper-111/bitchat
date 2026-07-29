using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BitChat.Core.Nostr;

namespace BitChat.Core.Services;

public class MiniRelayServer : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private readonly ConcurrentDictionary<string, NostrEvent> _events = [];
    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = [];

    public event Action<string>? OnLog;

    public bool IsRunning => _listener != null;
    public int Port => (_listener?.LocalEndpoint as IPEndPoint)?.Port ?? _port;

    public MiniRelayServer(int port = 4869)
    {
        _port = port;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        Log($"Local relay listening on ws://127.0.0.1:{_port}");
        _acceptLoop = AcceptLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        Log("Local relay stopped");
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcp = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClient(tcp, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { }
        }
    }

    private async Task HandleClient(TcpClient tcp, CancellationToken ct)
    {
        WebSocket? ws = null;
        var subIds = new List<string>();
        try
        {
            var stream = tcp.GetStream();
            ws = await AcceptWebSocketAsync(stream, ct);

            var buffer = new byte[65536];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    break;
                }
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                var ids = await ProcessClientMessage(json, ws, ct);
                subIds.AddRange(ids);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (IOException) { }
        finally
        {
            foreach (var subId in subIds)
            {
                if (_subscribers.TryGetValue(subId, out var sub) && sub.Socket == ws)
                    _subscribers.TryRemove(subId, out _);
            }
            if (ws?.State == WebSocketState.Open)
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            ws?.Dispose();
            try { tcp.Close(); } catch { }
        }
    }

    private static async Task<WebSocket> AcceptWebSocketAsync(NetworkStream stream, CancellationToken ct)
    {
        var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var key = "";
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct)))
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            {
                key = line.Split(':')[1].Trim();
            }
        }

        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("No Sec-WebSocket-Key header");

        var accept = ComputeAcceptKey(key);
        var response = new StringBuilder()
            .Append("HTTP/1.1 101 Switching Protocols\r\n")
            .Append("Upgrade: websocket\r\n")
            .Append("Connection: Upgrade\r\n")
            .Append($"Sec-WebSocket-Accept: {accept}\r\n")
            .Append("\r\n")
            .ToString();

        var respBytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(respBytes, ct);
        await stream.FlushAsync(ct);

        return WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromMinutes(5));
    }

    private static string ComputeAcceptKey(string key)
    {
        var combined = key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(combined));
        return Convert.ToBase64String(hash);
    }

    private async Task<List<string>> ProcessClientMessage(string json, WebSocket ws, CancellationToken ct)
    {
        var subIds = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return subIds;

            var cmd = root[0].GetString();
            switch (cmd)
            {
                case "EVENT":
                    await HandleEvent(root, ws, ct);
                    break;
                case "REQ":
                    var id = await HandleReq(root, ws, ct);
                    if (id != null) subIds.Add(id);
                    break;
                case "CLOSE":
                    HandleClose(root, ws);
                    break;
            }
        }
        catch { }
        return subIds;
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
                try { await SendTo(sub.Socket, new object[] { "EVENT", subId, evt }); }
                catch { }
            }
        }
    }

    private async Task<string?> HandleReq(JsonElement root, WebSocket ws, CancellationToken ct)
    {
        if (root.GetArrayLength() < 3) return null;
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
            catch { return subId; }
        }
        try { await SendTo(ws, new object[] { "EOSE", subId }); }
        catch { }
        return subId;
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
                filter.TagFilters[tagName] = prop.Value.EnumerateArray().Select(j => j.GetString()!).ToList();
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
            bool tagMatches = evt.Tags.Any(tag => tag.Length >= 2 && tag[0] == tagName && values.Contains(tag[1]));
            if (!tagMatches) return false;
        }
        return true;
    }

    private static async Task SendTo(WebSocket ws, object data)
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
        try { _listener?.Stop(); } catch { }
        _cts?.Dispose();
    }

    private record Subscriber(WebSocket Socket, List<NostrFilter> Filters);
}
