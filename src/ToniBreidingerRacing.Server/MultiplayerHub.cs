using System.Diagnostics;
using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Server;

public sealed class MultiplayerHub
{
    private sealed class Session(string id, IClientPeer peer, double now)
    {
        public string Id { get; } = id;
        public IClientPeer Peer { get; } = peer;
        public OnlinePlayer? Player { get; set; }
        public Room? Room { get; set; }
        public double LastActivity { get; set; } = now;
        public double Window { get; set; } = now;
        public int Messages { get; set; }
        public int Inputs { get; set; }
        public int Commands { get; set; }
        public int InvalidMessages { get; set; }
        public bool Failed { get; set; }
    }

    private sealed class Room(string name, string mode, Track track, double now)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Name { get; } = name;
        public string Mode { get; } = mode;
        public Track Track { get; } = track;
        public int Capacity => Mode == "duel" ? 2 : 8;
        public string Status { get; set; } = "waiting";
        public List<OnlinePlayer> Players { get; } = [];
        public List<OnlineMatch> Matches { get; } = [];
        public int Round { get; set; } = 1;
        public string? ChampionId { get; set; }
        public double ChangedAt { get; set; } = now;

        public RoomDto Dto() => new("room", Id, Name, Mode, Track.Id, Status,
            Players.Select(p => new PlayerDto(p.Id, p.Name, p.Ready, p.Connected)).ToArray(),
            Matches.Select(m => new BracketDto(m.Round, m.Index, m.Player1?.Id, m.Player2?.Id,
                m.Winner?.Id, m.State)).ToArray(), ChampionId);
    }

    private readonly object _gate = new();
    private readonly ServerOptions _options;
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly Dictionary<string, Room> _rooms = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _ticks;
    public int ConnectionCount { get { lock (_gate) return _sessions.Count; } }
    public int RoomCount { get { lock (_gate) return _rooms.Count; } }

    public MultiplayerHub(ServerOptions options)
    {
        options.Validate();
        _options = options;
    }

    public bool TryConnect(IClientPeer peer, out string id, double? now = null)
    {
        lock (_gate)
        {
            id = "";
            if (_sessions.Count >= _options.MaxConnections) return false;
            id = Guid.NewGuid().ToString("N");
            var session = new Session(id, peer, now ?? _clock.Elapsed.TotalSeconds);
            _sessions.Add(id, session);
            Send(session, new { type = "welcome", playerId = id, tracks = Protocol.Tracks });
            Send(session, Lobby(), "lobby");
            return true;
        }
    }

    public void Receive(string id, ReadOnlyMemory<byte> bytes, double? now = null)
    {
        var valid = Protocol.TryParse(bytes, out var command);
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var session) || session.Failed) return;
            var time = now ?? _clock.Elapsed.TotalSeconds;
            if (time - session.Window >= 1)
            {
                session.Window = time;
                session.Messages = session.Inputs = session.Commands = 0;
            }
            if (++session.Messages > _options.MessagesPerSecond)
            {
                Drop(session, time);
                return;
            }
            session.LastActivity = time;
            if (!valid)
            {
                Error(session, "Invalid message.");
                if (++session.InvalidMessages >= 8) Drop(session, time);
                return;
            }
            if (command!.Type == "input")
            {
                if (++session.Inputs > _options.InputsPerSecond) { Drop(session, time); return; }
                if (session.Room?.Status == "racing" && session.Player is { Connected: true } player)
                {
                    player.Input = command.Input;
                    player.LastInputAt = time;
                }
                return;
            }
            if (++session.Commands > _options.CommandsPerSecond) { Drop(session, time); return; }
            switch (command.Type)
            {
                case "create":
                    if (session.Room is not null) { Error(session, "Leave your current room first."); break; }
                    if (_rooms.Count >= _options.MaxRooms) { Error(session, "Room limit reached."); break; }
                    var track = TrackLibrary.All.FirstOrDefault(t => t.Id == command.TrackId);
                    if (track is null) { Error(session, "Unknown track."); break; }
                    var name = Protocol.SanitizeName(command.Name);
                    var created = new Room($"{name}'s {command.Mode}", command.Mode!, track, time);
                    _rooms.Add(created.Id, created);
                    Join(session, created, name, time);
                    break;
                case "join":
                    if (session.Room is not null) { Error(session, "Leave your current room first."); break; }
                    if (!_rooms.TryGetValue(command.RoomId!, out var room)) { Error(session, "Room not found."); break; }
                    if (room.Status != "waiting" || room.Players.Count >= room.Capacity)
                    { Error(session, "Room is full or already started."); break; }
                    Join(session, room, Protocol.SanitizeName(command.Name), time);
                    break;
                case "ready":
                    if (session.Room is not { Status: "waiting" } waiting || session.Player is null)
                    { Error(session, "Join a waiting room first."); break; }
                    if (session.Player.Ready == command.Ready) break;
                    session.Player.Ready = command.Ready;
                    waiting.ChangedAt = time;
                    if (waiting.Players.Count == waiting.Capacity && waiting.Players.All(p => p.Ready && p.Connected))
                        Start(waiting, time);
                    BroadcastRoom(waiting);
                    BroadcastLobby();
                    break;
                case "leave":
                    Leave(session, time);
                    Send(session, new { type = "left" });
                    Send(session, Lobby(), "lobby");
                    break;
            }
        }
    }

    private void Join(Session session, Room room, string name, double now)
    {
        session.Player = new OnlinePlayer(session.Id, name);
        session.Room = room;
        room.Players.Add(session.Player);
        room.ChangedAt = now;
        BroadcastRoom(room);
        BroadcastLobby();
    }

    private void Start(Room room, double now)
    {
        room.Status = "racing";
        room.ChangedAt = now;
        for (var i = 0; i < room.Players.Count; i += 2)
            room.Matches.Add(new OnlineMatch(room.Track, room.Players[i], room.Players[i + 1], 1, i / 2, _options));
        BroadcastSnapshots(room);
    }

    public void Disconnect(string id, double? now = null)
    {
        lock (_gate)
            if (_sessions.TryGetValue(id, out var session)) Drop(session, now ?? _clock.Elapsed.TotalSeconds);
    }

    private void Drop(Session session, double now)
    {
        session.Peer.Stop();
        _sessions.Remove(session.Id);
        Leave(session, now);
    }

    private void Leave(Session session, double now)
    {
        if (session.Room is not { } room || session.Player is not { } player) return;
        session.Room = null;
        session.Player = null;
        player.Connected = false;
        player.Ready = false;
        player.Input = CarInput.None;
        if (room.Status == "waiting") room.Players.Remove(player);
        room.ChangedAt = now;
        foreach (var match in room.Matches.Where(m => !m.Finished)) match.ResolveForfeits();
        AdvanceBracket(room, now);
        if (room.Players.All(p => !p.Connected))
            _rooms.Remove(room.Id);
        else
        {
            BroadcastRoom(room);
            BroadcastSnapshots(room);
        }
        BroadcastLobby();
    }

    // This critical section contains no network awaits; slow clients cannot stall physics.
    public void Tick(float dt = 1f / 60f, double? now = null)
    {
        lock (_gate)
        {
            var time = now ?? _clock.Elapsed.TotalSeconds;
            foreach (var session in _sessions.Values.ToArray())
                if (session.Failed || time - session.LastActivity > _options.ConnectionIdleSeconds)
                    Drop(session, time);
            foreach (var room in _rooms.Values.ToArray())
            {
                if ((room.Status == "waiting" && time - room.ChangedAt > _options.LobbyIdleSeconds) ||
                    (room.Status == "finished" && time - room.ChangedAt > _options.FinishedRetentionSeconds))
                {
                    foreach (var session in _sessions.Values.Where(s => s.Room == room))
                    {
                        session.Room = null;
                        session.Player = null;
                        Send(session, new { type = "left" });
                    }
                    _rooms.Remove(room.Id);
                    BroadcastLobby();
                    continue;
                }
                if (room.Status != "racing") continue;
                var before = room.Matches.Count(m => m.Finished);
                foreach (var match in room.Matches.Where(m => m.Round == room.Round))
                    match.Step(Math.Clamp(dt, 0, 1f / 60f), time);
                if (room.Matches.Count(m => m.Finished) != before)
                {
                    // Enqueue terminal snapshots before creating the next round.
                    BroadcastSnapshots(room);
                    AdvanceBracket(room, time);
                    BroadcastRoom(room);
                    BroadcastLobby();
                }
                if (_ticks % 3 == 0) BroadcastSnapshots(room);
            }
            _ticks++;
        }
    }

    private void AdvanceBracket(Room room, double now)
    {
        if (room.Status != "racing") return;
        while (room.Matches.Where(m => m.Round == room.Round).All(m => m.Finished))
        {
            var completed = room.Matches.Where(m => m.Round == room.Round).OrderBy(m => m.Index).ToArray();
            if (completed.Length == 0) return;
            if (completed.Length == 1)
            {
                room.Status = "finished";
                room.ChampionId = completed[0].Winner?.Id;
                room.ChangedAt = now;
                return;
            }
            room.Round++;
            for (var i = 0; i < completed.Length; i += 2)
                room.Matches.Add(new OnlineMatch(room.Track, completed[i].Winner, completed[i + 1].Winner,
                    room.Round, i / 2, _options));
            BroadcastSnapshots(room);
        }
    }

    private object Lobby() => new
    {
        type = "lobby",
        rooms = _rooms.Values.Select(r => new LobbyRoomDto(r.Id, r.Name, r.Mode, r.Track.Id,
            r.Players.Count(p => p.Connected), r.Capacity, r.Status)).ToArray(),
    };
    private void BroadcastLobby()
    {
        var lobby = Lobby();
        foreach (var session in _sessions.Values) Send(session, lobby, "lobby");
    }
    private void BroadcastRoom(Room room)
    {
        var dto = room.Dto();
        foreach (var session in _sessions.Values.Where(s => s.Room == room)) Send(session, dto, "room");
    }
    private void BroadcastSnapshots(Room room)
    {
        foreach (var match in room.Matches)
        {
            var snapshot = match.Snapshot(room.Id);
            foreach (var session in _sessions.Values.Where(s => s.Room == room))
                Send(session, snapshot, match.Id);
        }
    }
    private static void Error(Session session, string message) => Send(session, new { type = "error", message });
    private static void Send(Session session, object message, string? snapshotKey = null)
    {
        if (!session.Failed && !session.Peer.Enqueue(message, snapshotKey))
        {
            session.Failed = true;
            session.Peer.Stop();
        }
    }
}

public sealed class SimulationService(MultiplayerHub hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(8));
        var clock = Stopwatch.StartNew();
        var previous = clock.Elapsed.TotalSeconds;
        var accumulator = 0d;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = clock.Elapsed.TotalSeconds;
                accumulator += Math.Clamp(now - previous, 0, .1);
                previous = now;
                // Fixed 60 Hz despite timer granularity, with bounded catch-up after host stalls.
                var steps = 0;
                while (accumulator >= 1d / 60 && steps++ < 6)
                {
                    hub.Tick();
                    accumulator -= 1d / 60;
                }
                if (steps > 6) accumulator = 0;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
