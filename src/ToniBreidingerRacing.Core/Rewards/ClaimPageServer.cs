using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ToniBreidingerRacing.Core.Rewards;

/// <summary>
/// Serves claim pages on <c>http://127.0.0.1</c> so browser wallets (which do not inject into <c>file://</c>
/// pages) can submit the claim. Each page lives at an unguessable path; nothing is exposed off the machine.
/// </summary>
public sealed class ClaimPageServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Dictionary<string, string> _pages = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _acceptLoop;

    public int Port { get; private set; }

    public void Start()
    {
        if (_acceptLoop is not null)
        {
            return;
        }

        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));
    }

    /// <summary>Publishes a page and returns its local URL.</summary>
    public Uri Publish(string html)
    {
        Start();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        lock (_gate)
        {
            _pages[token] = html;
        }

        return new Uri($"http://127.0.0.1:{Port}/claim/{token}");
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        _listener.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _listener.Dispose();
        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 5000;
                var stream = client.GetStream();
                var requestLine = await ReadRequestLineAsync(stream, cancellationToken);
                var (status, contentType, body) = Route(requestLine);
                var bytes = Encoding.UTF8.GetBytes(body);
                var header =
                    $"HTTP/1.1 {status}\r\nContent-Type: {contentType}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\n" +
                    "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
                await stream.WriteAsync(bytes, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private (string Status, string ContentType, string Body) Route(string? requestLine)
    {
        var parts = requestLine?.Split(' ');
        if (parts is not { Length: >= 2 } || parts[0] != "GET")
        {
            return ("405 Method Not Allowed", "text/plain", "Method not allowed.");
        }

        const string prefix = "/claim/";
        var path = parts[1];
        if (path.StartsWith(prefix, StringComparison.Ordinal))
        {
            lock (_gate)
            {
                if (_pages.TryGetValue(path[prefix.Length..], out var html))
                {
                    return ("200 OK", "text/html", html);
                }
            }
        }

        return ("404 Not Found", "text/plain", "Not found.");
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token);
            if (read == 0)
            {
                break;
            }

            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return text[..text.IndexOf("\r\n", StringComparison.Ordinal)];
            }
        }

        var partial = Encoding.ASCII.GetString(buffer, 0, total);
        var newline = partial.IndexOf("\r\n", StringComparison.Ordinal);
        return newline >= 0 ? partial[..newline] : null;
    }
}
