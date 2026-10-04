using System.Net.WebSockets;

namespace ToniBreidingerRacing.Server;

public interface IClientPeer
{
    bool Enqueue(object message, string? snapshotKey = null);
    void Stop();
}

/// <summary>Tick code only enqueues; one writer sends control messages and coalesced per-match snapshots.</summary>
public sealed class SocketPeer : IClientPeer, IDisposable
{
    public const int MaxControlMessages = 64;
    public const int MaxSnapshotKeys = 10;
    private readonly object _gate = new();
    private readonly Queue<byte[]> _control = new();
    private readonly Dictionary<string, byte[]> _snapshots = [];
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _stop = new();
    private WebSocket? _socket;

    public bool Enqueue(object message, string? snapshotKey = null)
    {
        var bytes = Protocol.Serialize(message);
        lock (_gate)
        {
            if (_stop.IsCancellationRequested) return false;
            if (snapshotKey is null)
            {
                if (_control.Count >= MaxControlMessages) { Stop(); return false; }
                _control.Enqueue(bytes);
            }
            else
            {
                if (!_snapshots.ContainsKey(snapshotKey) && _snapshots.Count >= MaxSnapshotKeys)
                {
                    Stop();
                    return false;
                }
                _snapshots[snapshotKey] = bytes;
            }
            if (_signal.CurrentCount == 0) _signal.Release();
            return true;
        }
    }

    public void Stop()
    {
        _stop.Cancel();
        _socket?.Abort();
    }

    public async Task RunAsync(WebSocket socket, Action<ReadOnlyMemory<byte>> receive, CancellationToken requestAborted)
    {
        _socket = socket;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, _stop.Token);
        var writer = WriteAsync(socket, linked.Token);
        try
        {
            var buffer = new byte[Protocol.MaxMessageBytes + 1];
            while (!linked.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var used = 0;
                var fragments = 0;
                using var messageTimeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer.AsMemory(used), messageTimeout.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    used += result.Count;
                    if (++fragments == 1 && !result.EndOfMessage)
                        messageTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    if (result.MessageType != WebSocketMessageType.Text || used > Protocol.MaxMessageBytes || fragments > 32)
                    {
                        // CloseOutputAsync belongs to the writer; abort here never races two sends.
                        Stop();
                        return;
                    }
                } while (!result.EndOfMessage);
                receive(buffer.AsMemory(0, used));
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            Stop();
            await writer;
        }
    }

    private async Task WriteAsync(WebSocket socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _signal.WaitAsync(token);
                while (TryDequeue(out var bytes))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
                }
            }
        }
        catch (OperationCanceledException) { Stop(); }
        catch (WebSocketException) { Stop(); }
    }

    private bool TryDequeue(out byte[] bytes)
    {
        lock (_gate)
        {
            if (_control.TryDequeue(out bytes!)) return true;
            if (_snapshots.Count != 0)
            {
                var first = _snapshots.First();
                bytes = first.Value;
                _snapshots.Remove(first.Key);
                return true;
            }
            bytes = [];
            return false;
        }
    }

    public void Dispose()
    {
        Stop();
        _stop.Dispose();
        _signal.Dispose();
    }
}
