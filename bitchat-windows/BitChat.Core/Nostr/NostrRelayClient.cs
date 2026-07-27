using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BitChat.Core.Nostr;

public class NostrRelayClient : IDisposable
{
    private readonly Uri _url;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private readonly Dictionary<string, Action<NostrEvent>> _subscriptions = [];

    public event Action<NostrEvent>? OnEvent;
    public event Action<string>? OnNotice;
    public event Action<string, bool, string>? OnOk;
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public NostrRelayClient(Uri url) => _url = url;

    public async Task ConnectAsync()
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        await _ws.ConnectAsync(_url, _cts.Token);
        _receiveLoop = ReceiveLoop(_cts.Token);
    }

    public async Task Subscribe(string subId, NostrFilter filter, Action<NostrEvent> handler)
    {
        _subscriptions[subId] = handler;
        var msg = JsonSerializer.Serialize(new object[] { "REQ", subId, filter.ToDictionary() });
        await SendAsync(msg);
    }

    public async Task Unsubscribe(string subId)
    {
        _subscriptions.Remove(subId);
        await SendAsync(JsonSerializer.Serialize(new object[] { "CLOSE", subId }));
    }

    public async Task PublishEvent(NostrEvent evt)
    {
        var msg = JsonSerializer.Serialize(new object[] { "EVENT", evt });
        await SendAsync(msg);
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();
        if (_receiveLoop != null)
            try { await _receiveLoop; } catch { }
        if (_ws?.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        _ws?.Dispose();
        _ws = null;
    }

    private async Task SendAsync(string message)
    {
        if (_ws?.State == WebSocketState.Open)
            await _ws.SendAsync(
                Encoding.UTF8.GetBytes(message),
                WebSocketMessageType.Text,
                true,
                _cts?.Token ?? CancellationToken.None);
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[65536];
        try
        {
            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                ProcessMessage(json);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return;

            var cmd = root[0].GetString();
            switch (cmd)
            {
                case "EVENT":
                    var subId = root[1].GetString()!;
                    var evt = JsonSerializer.Deserialize<NostrEvent>(root[2].GetRawText());
                    if (evt != null)
                    {
                        OnEvent?.Invoke(evt);
                        if (_subscriptions.TryGetValue(subId, out var handler))
                            handler(evt);
                    }
                    break;
                case "OK":
                    var eventId = root[1].GetString()!;
                    var success = root[2].GetBoolean();
                    var reason = root.GetArrayLength() > 3 ? root[3].GetString() ?? "" : "";
                    OnOk?.Invoke(eventId, success, reason);
                    break;
                case "EOSE":
                    break;
                case "NOTICE":
                    OnNotice?.Invoke(root[1].GetString()!);
                    break;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
