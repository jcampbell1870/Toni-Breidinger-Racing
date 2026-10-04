using System.Net.WebSockets;
using System.Text.Json;
using ToniBreidingerRacing.Server;

namespace ToniBreidingerRacing.Tests;

public sealed class ServerSocketTests
{
    private sealed class Socket : WebSocket
    {
        public List<JsonElement> Sent { get; } = [];
        public TaskCompletionSource FirstSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSend { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Block { get; set; }
        public Queue<(byte[] Bytes, bool End, WebSocketMessageType Type)> Incoming { get; } = [];
        public int ConcurrentSendMaximum { get; private set; }
        private int _sending;
        private WebSocketState _state = WebSocketState.Open;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;
        public override void Abort() => _state = WebSocketState.Aborted;
        public override void Dispose() => Abort();
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken token)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken token) =>
            CloseAsync(closeStatus, statusDescription, token);
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            if (Incoming.TryDequeue(out var message))
            {
                message.Bytes.AsSpan().CopyTo(buffer.AsSpan());
                return new(message.Bytes.Length, message.Type, message.End);
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(0, WebSocketMessageType.Close, true);
        }
        public override async Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool endOfMessage, CancellationToken token)
        {
            var count = Interlocked.Increment(ref _sending);
            ConcurrentSendMaximum = Math.Max(ConcurrentSendMaximum, count);
            try
            {
                FirstSend.TrySetResult();
                if (Block) await ReleaseSend.Task.WaitAsync(token);
                lock (Sent)
                    Sent.Add(JsonSerializer.Deserialize<JsonElement>(buffer.AsSpan()));
            }
            finally { Interlocked.Decrement(ref _sending); }
        }
    }

    [Fact]
    public void LatestSnapshotsAreBoundedAndCoalesced()
    {
        using var peer = new SocketPeer();
        for (var i = 0; i < 10_000; i++)
            Assert.True(peer.Enqueue(new { type = "snapshot", sequence = i }, "one-match"));
        for (var i = 1; i < SocketPeer.MaxSnapshotKeys; i++)
            Assert.True(peer.Enqueue(new { type = "snapshot" }, $"match-{i}"));
        Assert.False(peer.Enqueue(new { type = "snapshot" }, "over-capacity"));
        Assert.False(peer.Enqueue(new { type = "left" }));
    }

    [Fact]
    public void ControlOverflowDisconnectsInsteadOfGrowingMemory()
    {
        using var peer = new SocketPeer();
        for (var i = 0; i < SocketPeer.MaxControlMessages; i++)
            Assert.True(peer.Enqueue(new { type = "error", message = "Invalid." }));
        Assert.False(peer.Enqueue(new { type = "left" }));
    }

    [Fact]
    public async Task SingleWriterSendsNewestSnapshotWithoutBlockingProducer()
    {
        using var peer = new SocketPeer();
        using var socket = new Socket { Block = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.True(peer.Enqueue(new { type = "welcome" }));
        var run = peer.RunAsync(socket, _ => { }, cts.Token);
        await socket.FirstSend.Task.WaitAsync(cts.Token);
        // The network writer is stuck, but neither simulation publication nor input reception waits for it.
        for (var i = 0; i < 1000; i++)
            Assert.True(peer.Enqueue(new { type = "snapshot", sequence = i }, "match"));
        socket.ReleaseSend.TrySetResult();
        while (true)
        {
            lock (socket.Sent)
                if (socket.Sent.Count >= 2) break;
            await Task.Delay(5, cts.Token);
        }
        peer.Stop();
        await run;
        Assert.Equal(1, socket.ConcurrentSendMaximum);
        Assert.Equal("welcome", socket.Sent[0].GetProperty("type").GetString());
        Assert.Equal(999, socket.Sent[1].GetProperty("sequence").GetInt32());
    }

    [Fact]
    public async Task DeadSocketSendTimesOutAndStopsSession()
    {
        using var peer = new SocketPeer();
        using var socket = new Socket { Block = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        peer.Enqueue(new { type = "welcome" });
        await peer.RunAsync(socket, _ => { }, cts.Token);
        Assert.Equal(WebSocketState.Aborted, socket.State);
        Assert.False(peer.Enqueue(new { type = "left" }));
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("oversized")]
    [InlineData("fragments")]
    public async Task InvalidFramesAreAbortedWithoutDispatch(string kind)
    {
        using var peer = new SocketPeer();
        using var socket = new Socket();
        if (kind == "binary") socket.Incoming.Enqueue(([0], true, WebSocketMessageType.Binary));
        if (kind == "oversized") socket.Incoming.Enqueue((new byte[4097], true, WebSocketMessageType.Text));
        if (kind == "fragments")
            for (var i = 0; i < 33; i++) socket.Incoming.Enqueue(([], false, WebSocketMessageType.Text));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var received = 0;
        await peer.RunAsync(socket, _ => received++, cts.Token);
        Assert.Equal(0, received);
        Assert.Equal(WebSocketState.Aborted, socket.State);
    }
}
