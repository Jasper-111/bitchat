using System.Net;

namespace BitChat.Core.Services;

/// <summary>
/// Abstraction over an in-process Tor proxy (backed by Arti, a pure-Rust
/// Tor implementation compiled to the target platform).
///
/// When Arti is not compiled in, a <see cref="StubTorSocksProxy"/> provides
/// a no-op implementation for testing and development.
/// </summary>
public interface ITorSocksProxy : IDisposable
{
    /// <summary>True once the proxy is accepting connections.</summary>
    bool IsReady { get; }

    /// <summary>The SOCKS5 endpoint, e.g. 127.0.0.1:39050. Null before ready.</summary>
    IPEndPoint? ProxyEndpoint { get; }

    /// <summary>Latest bootstrap progress 0.0–1.0, or null before first report.</summary>
    double? BootstrapProgress { get; }

    /// <summary>Started was requested but not yet ready.</summary>
    bool IsStarting { get; }

    /// <summary>
    /// Start building circuits.  Completes when bootstrap is finished
    /// (the proxy is ready to handle SOCKS5 CONNECT), or throws on
    /// unrecoverable failure (e.g. 75-second deadline exceeded).
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Gracefully shut down circuits and release the proxy port.
    /// Safe to call multiple times; after the first call the proxy
    /// is permanently stopped.
    /// </summary>
    Task StopAsync();

    /// <summary>Restart the proxy (e.g., after network change).</summary>
    Task RestartAsync(CancellationToken ct = default);

    /// <summary>Fired when bootstrap reaches 100% and the proxy is ready.</summary>
    event Action? OnReady;
    /// <summary>Fired when the proxy is about to start (gives UI a chance to show a banner).</summary>
    event Action? OnWillStart;
    /// <summary>Fired when the proxy has stopped.</summary>
    event Action? OnStopped;
    /// <summary>Fired on a non-recoverable error (proxy dead).</summary>
    event Action<Exception>? OnError;
}

/// <summary>
/// No-op for environments where Arti is not compiled in.
/// All operations succeed instantly; the proxy is never actually running.
/// </summary>
public sealed class StubTorSocksProxy : ITorSocksProxy
{
    public bool IsReady { get; private set; }
    public IPEndPoint? ProxyEndpoint { get; private set; }
    public double? BootstrapProgress { get; private set; }
    public bool IsStarting { get; private set; }

    public event Action? OnReady;
    public event Action? OnWillStart;
    public event Action? OnStopped;
    public event Action<Exception>? OnError;

    public Task StartAsync(CancellationToken ct = default)
    {
        IsStarting = true;
        BootstrapProgress = 0.0;
        OnWillStart?.Invoke();

        BootstrapProgress = 1.0;
        IsStarting = false;
        IsReady = true;
        ProxyEndpoint = new IPEndPoint(System.Net.IPAddress.Loopback, 39050);
        OnReady?.Invoke();

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsReady = false;
        IsStarting = false;
        ProxyEndpoint = null;
        BootstrapProgress = null;
        OnStopped?.Invoke();
        return Task.CompletedTask;
    }

    public async Task RestartAsync(CancellationToken ct = default)
    {
        await StopAsync();
        await StartAsync(ct);
    }

    public void Dispose() { _ = StopAsync(); }
}
