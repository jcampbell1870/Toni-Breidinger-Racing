using System.Numerics;
using System.Text;
using System.Text.Json;
using ToniBreidingerRacing.Core.Racing;
using ToniBreidingerRacing.Server;

namespace ToniBreidingerRacing.Tests;

public sealed class ServerProtocolTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"type\":\"input\",\"accelerate\":true,\"brake\":false,\"fire\":false,\"steer\":1.01}")]
    [InlineData("{\"type\":\"input\",\"accelerate\":true,\"brake\":false,\"fire\":false,\"steer\":-1.01}")]
    [InlineData("{\"type\":\"input\",\"accelerate\":true,\"brake\":false,\"fire\":false,\"steer\":1e400}")]
    [InlineData("{\"type\":\"input\",\"accelerate\":true,\"brake\":false,\"fire\":false,\"steer\":\"NaN\"}")]
    [InlineData("{\"type\":\"input\",\"accelerate\":1,\"brake\":false,\"fire\":false,\"steer\":0}")]
    [InlineData("{\"type\":\"ready\",\"ready\":\"true\"}")]
    [InlineData("{\"type\":\"ready\",\"ready\":true,\"ready\":false}")]
    [InlineData("{\"type\":\"create\",\"mode\":\"ai\",\"trackId\":\"sunshine-speedway\"}")]
    [InlineData("{\"type\":\"win\",\"winnerId\":\"me\"}")]
    public void InvalidMessagesAreRejected(string json) =>
        Assert.False(Protocol.TryParse(Encoding.UTF8.GetBytes(json), out _));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void FiniteHumanInputsAreAccepted(float steer)
    {
        var bytes = Protocol.Serialize(new { type = "input", accelerate = true, brake = false, steer, fire = true });
        Assert.True(Protocol.TryParse(bytes, out var command));
        Assert.Equal(new CarInput(true, false, steer, true), command!.Input);
    }

    [Fact]
    public void DeepOrOversizedMessagesAreRejected()
    {
        Assert.False(Protocol.TryParse(Encoding.UTF8.GetBytes(new string(' ', 4097)), out _));
        Assert.False(Protocol.TryParse(Encoding.UTF8.GetBytes(
            "{\"type\":\"leave\",\"extra\":" + new string('[', 9) + "0" + new string(']', 9) + "}"), out _));
    }

    [Fact]
    public void NamesAreBoundedPlainText()
    {
        var name = Protocol.SanitizeName("<img src=x>\u0000\u202e" + new string('Z', 100));
        Assert.Equal(24, name.Length);
        Assert.DoesNotContain("<", name);
        Assert.DoesNotContain(">", name);
        Assert.DoesNotContain('\u202e', name);
        Assert.Equal("Racer", Protocol.SanitizeName(" \u0000<> "));
    }

    [Fact]
    public void TrackProtocolUsesCartesianPointsAndHazards()
    {
        var track = TrackLibrary.All[0];
        var dto = Protocol.Tracks[0];
        Assert.Equal(track.Points.Count, dto.Points.Length);
        Assert.Equal(track.Points[0].X, dto.Points[0].X);
        Assert.Equal(track.FeaturePosition(track.Features[0]).Y, dto.Features[0].Y);
        Assert.Equal("zipper", dto.Features[0].Kind);
        var json = JsonSerializer.Serialize(dto, Protocol.JsonOptions);
        Assert.Contains("\"worldWidth\":", json);
        Assert.Contains("\"roadWidth\":", json);
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://127.0.0.1:5173", true)]
    [InlineData("http://[::1]:5173", true)]
    [InlineData("https://localhost.attacker.example", false)]
    [InlineData("http://192.168.0.1:5173", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    [InlineData("https://app.example/path", false)]
    [InlineData("https://app.example/", false)]
    [InlineData("http://localhost/.", false)]
    [InlineData("http://localhost\\", false)]
    public void LocalDevelopmentOriginPolicyIsNarrow(string origin, bool expected) =>
        Assert.Equal(expected, new OriginPolicy([], true).Allows(origin));

    [Fact]
    public void ProductionRequiresExplicitExactOrigins()
    {
        Assert.Throws<InvalidOperationException>(() => new OriginPolicy([], false));
        Assert.Throws<InvalidOperationException>(() => new OriginPolicy(["*"], false));
        var policy = new OriginPolicy(["https://app.example"], false);
        Assert.True(policy.Allows("https://app.example"));
        Assert.False(policy.Allows("https://app.example:444"));
        Assert.False(policy.Allows("http://localhost:5173"));
        Assert.False(policy.Allows("https://app.example.attacker.test"));
        Assert.False(policy.Allows("https://user@app.example"));
    }

    [Fact]
    public void DangerousConfigurationIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new MultiplayerHub(new ServerOptions { MaxRooms = 1000 }));
        Assert.Throws<InvalidOperationException>(() => new MultiplayerHub(new ServerOptions { InputTimeoutSeconds = double.NaN }));
    }
}

public sealed class ServerMatchTests
{
    private static readonly Track ShortTrack = new("test", "Test",
        [new(0, 0), new(180, 0), new(180, 180), new(0, 180)], 64, 1);

    [Fact]
    public void CountdownDoesNotMoveCarsAndOnlyServerStartsRace()
    {
        var p1 = new OnlinePlayer("1", "One") { Input = new(true, false, 0, false), LastInputAt = 0 };
        var match = new OnlineMatch(ShortTrack, p1, new("2", "Two"), 1, 0, new ServerOptions());
        var start = match.Snapshot("room").Cars[0];
        match.Step(1f / 60, 0);
        Assert.Equal(start.X, match.Snapshot("room").Cars[0].X);
        Assert.Equal(start.Y, match.Snapshot("room").Cars[0].Y);
        Assert.Equal("countdown", match.State);
        for (var i = 0; i < 181; i++) match.Step(1f / 60, i / 60d);
        Assert.Equal("racing", match.State);
        Assert.Null(match.Winner);
    }

    [Fact]
    public void ExpiredInputCannotLeaveThrottlePressed()
    {
        var options = new ServerOptions { CountdownSeconds = .01, InputTimeoutSeconds = .1 };
        var player = new OnlinePlayer("1", "One") { Input = new(true, false, 0, false), LastInputAt = 0 };
        var match = new OnlineMatch(ShortTrack, player, new("2", "Two"), 1, 0, options);
        match.Step(1f / 60, 0);
        match.Step(1f / 60, .01);
        var speed = match.Snapshot("room").Cars[0].Speed;
        Assert.True(speed > 0);
        for (var i = 0; i < 60; i++) match.Step(1f / 60, 1 + i / 60d);
        Assert.True(match.Snapshot("room").Cars[0].Speed < speed);
    }

    [Fact]
    public void TimeoutMarksBothAsDnfWithoutInventingWinner()
    {
        var match = new OnlineMatch(ShortTrack, new("1", "One"), new("2", "Two"), 1, 0,
            new ServerOptions { CountdownSeconds = .01, RaceTimeoutSeconds = .05 });
        for (var i = 0; i < 10; i++) match.Step(1f / 60, i / 60d);
        Assert.True(match.Finished);
        Assert.Null(match.Winner);
        Assert.All(match.Snapshot("room").Cars, c => Assert.False(c.Finished));
    }

    [Fact]
    public void DisconnectForfeitsDuringCountdownAndMissingHumansAreNotAi()
    {
        var player = new OnlinePlayer("1", "One");
        var opponent = new OnlinePlayer("2", "Two");
        var match = new OnlineMatch(ShortTrack, player, opponent, 1, 0, new ServerOptions());
        opponent.Connected = false;
        match.ResolveForfeits();
        Assert.True(match.Finished);
        Assert.Same(player, match.Winner);
        Assert.Equal(2, match.Snapshot("room").Cars.Length);
        var bye = new OnlineMatch(ShortTrack, player, null, 2, 0, new ServerOptions());
        Assert.True(bye.Finished);
        Assert.Single(bye.Snapshot("room").Cars);
        var empty = new OnlineMatch(ShortTrack, null, null, 3, 0, new ServerOptions());
        Assert.True(empty.Finished);
        Assert.Null(empty.Winner);
        Assert.Empty(empty.Snapshot("room").Cars);
    }

    [Fact]
    public void ActualHumanControlCanFinishLapAuthoritatively()
    {
        var player = new OnlinePlayer("human", "Human");
        var match = new OnlineMatch(ShortTrack, player, new("opponent", "Opponent"), 1, 0,
            new ServerOptions { CountdownSeconds = .01 });
        for (var i = 0; i < 60 * 60 && !match.Finished; i++)
        {
            var car = match.Snapshot("room").Cars[0];
            var position = new Vector2(car.X, car.Y);
            var distance = ShortTrack.Project(position).Distance;
            var target = ShortTrack.PointAt(distance + 45);
            var error = CarPhysics.NormalizeAngle(MathF.Atan2(target.Y - position.Y, target.X - position.X) - car.Heading);
            player.Input = new(car.Speed < 100, car.Speed > 110, Math.Clamp(error * 2.6f, -1, 1), false);
            player.LastInputAt = i / 60d;
            match.Step(1f / 60, i / 60d);
        }
        Assert.True(match.Finished);
        Assert.Same(player, match.Winner);
        Assert.True(match.Snapshot("room").Cars[0].Finished);
        Assert.False(match.Snapshot("room").Cars[1].Finished);
    }
}

public sealed class ServerHubTests
{
    private sealed class Peer : IClientPeer
    {
        public List<object> Controls { get; } = [];
        public Dictionary<string, object> Latest { get; } = [];
        public bool Stopped { get; private set; }
        public bool Reject { get; set; }
        public bool Enqueue(object message, string? snapshotKey = null)
        {
            if (Reject || Stopped) return false;
            if (snapshotKey is null) Controls.Add(message);
            else Latest[snapshotKey] = message;
            return true;
        }
        public void Stop() => Stopped = true;
        public RoomDto Room => (RoomDto)Latest["room"];
        public JsonElement Lobby => JsonSerializer.SerializeToElement(Latest["lobby"], Protocol.JsonOptions);
    }

    private static (string Id, Peer Peer) Connect(MultiplayerHub hub, double now = 0)
    {
        var peer = new Peer();
        Assert.True(hub.TryConnect(peer, out var id, now));
        return (id, peer);
    }
    private static void Send(MultiplayerHub hub, string id, object command, double now = 0) =>
        hub.Receive(id, Protocol.Serialize(command), now);
    private static void Create(MultiplayerHub hub, string id, string mode = "duel") =>
        Send(hub, id, new { type = "create", name = "Human", mode, trackId = "sunshine-speedway" });
    private static void Join(MultiplayerHub hub, string id, string roomId) =>
        Send(hub, id, new { type = "join", name = "Human", roomId });
    private static void Ready(MultiplayerHub hub, string id) =>
        Send(hub, id, new { type = "ready", ready = true });

    [Fact]
    public void DuelNeedsTwoConnectedReadyHumans()
    {
        var hub = new MultiplayerHub(new());
        var (id1, peer1) = Connect(hub);
        var (id2, peer2) = Connect(hub);
        Create(hub, id1);
        Ready(hub, id1);
        Assert.Equal("waiting", peer1.Room.Status);
        Assert.Single(peer1.Room.Players);
        Join(hub, id2, peer1.Room.Id);
        Assert.Equal("waiting", peer2.Room.Status);
        Ready(hub, id2);
        Assert.Equal("racing", peer1.Room.Status);
        Assert.Single(peer1.Room.Bracket);
        Assert.Equal(2, peer1.Latest.Values.OfType<SnapshotDto>().Single().Cars.Length);
        hub.Disconnect(id2, 0);
        Assert.Equal("finished", peer1.Room.Status);
        Assert.Equal(id1, peer1.Room.ChampionId);
        Assert.False(peer1.Room.Players.Single(p => p.Id == id2).Connected);
    }

    [Fact]
    public void TournamentRequiresEightHumansAndAdvancesThroughSevenMatches()
    {
        var hub = new MultiplayerHub(new());
        var players = Enumerable.Range(0, 8).Select(_ => Connect(hub)).ToArray();
        Create(hub, players[0].Id, "tournament");
        var roomId = players[0].Peer.Room.Id;
        foreach (var (id, _) in players.Skip(1)) Join(hub, id, roomId);
        foreach (var (id, _) in players.Take(7)) Ready(hub, id);
        Assert.Equal("waiting", players[0].Peer.Room.Status);
        Assert.Empty(players[0].Peer.Room.Bracket);
        Ready(hub, players[7].Id);
        Assert.Equal(4, players[0].Peer.Room.Bracket.Count(b => b.Round == 1));
        foreach (var index in new[] { 1, 3, 5, 7 }) hub.Disconnect(players[index].Id, 0);
        Assert.Equal(2, players[0].Peer.Room.Bracket.Count(b => b.Round == 2));
        hub.Disconnect(players[2].Id, 0);
        hub.Disconnect(players[6].Id, 0);
        Assert.Single(players[0].Peer.Room.Bracket, b => b.Round == 3);
        // An eliminated but still-connected human receives the final too.
        Assert.Contains(players[4].Peer.Latest.Values.OfType<SnapshotDto>(), s => s.Round == 3);
        hub.Disconnect(players[4].Id, 0);
        var room = players[0].Peer.Room;
        Assert.Equal(7, room.Bracket.Length);
        Assert.Equal("finished", room.Status);
        Assert.Equal(players[0].Id, room.ChampionId);
        Assert.All(room.Bracket, b => Assert.Equal("finished", b.Status));
    }

    [Fact]
    public void EliminatedConnectedHumanCanSpectateLaterRound()
    {
        var options = new ServerOptions { CountdownSeconds = .01, RaceTimeoutSeconds = .02 };
        var hub = new MultiplayerHub(options);
        var players = Enumerable.Range(0, 8).Select(_ => Connect(hub)).ToArray();
        Create(hub, players[0].Id, "tournament");
        foreach (var (id, _) in players.Skip(1)) Join(hub, id, players[0].Peer.Room.Id);
        foreach (var (id, _) in players) Ready(hub, id);
        for (var i = 0; i < 10; i++) hub.Tick(now: i / 60d);
        var room = players[0].Peer.Room;
        Assert.Equal("finished", room.Status);
        Assert.Null(room.ChampionId);
        Assert.Equal(7, room.Bracket.Length);
        Assert.All(room.Bracket, b => Assert.Null(b.WinnerId));
        Assert.Contains(players[7].Peer.Latest.Values.OfType<SnapshotDto>(), s => s.Round == 3 && s.State == "finished");
    }

    [Fact]
    public void WaitingLeaveFreesSlotAndEmptyRoomIsRemoved()
    {
        var hub = new MultiplayerHub(new());
        var (id1, peer1) = Connect(hub);
        var (id2, peer2) = Connect(hub);
        Create(hub, id1);
        Join(hub, id2, peer1.Room.Id);
        Send(hub, id2, new { type = "leave" });
        Assert.Single(peer1.Room.Players);
        Assert.Contains(peer2.Controls, x => JsonSerializer.SerializeToElement(x).GetProperty("type").GetString() == "left");
        Send(hub, id1, new { type = "leave" });
        Assert.Equal(0, hub.RoomCount);
        Assert.Empty(peer1.Lobby.GetProperty("rooms").EnumerateArray());
    }

    [Fact]
    public void CapacityRejectsExtraConnectionsRoomsAndPlayers()
    {
        var hub = new MultiplayerHub(new() { MaxConnections = 4, MaxRooms = 1 });
        var players = Enumerable.Range(0, 4).Select(_ => Connect(hub)).ToArray();
        Assert.False(hub.TryConnect(new Peer(), out _, 0));
        Create(hub, players[0].Id);
        Create(hub, players[1].Id);
        Assert.Equal(1, hub.RoomCount);
        Join(hub, players[1].Id, players[0].Peer.Room.Id);
        Join(hub, players[2].Id, players[0].Peer.Room.Id);
        Assert.Equal(2, players[0].Peer.Room.Players.Length);
        Assert.False(players[2].Peer.Latest.ContainsKey("room"));
    }

    [Fact]
    public void InputAndCommandSpamAreDisconnected()
    {
        var hub = new MultiplayerHub(new() { InputsPerSecond = 2, CommandsPerSecond = 2 });
        var (inputId, inputPeer) = Connect(hub);
        for (var i = 0; i < 3; i++)
            Send(hub, inputId, new { type = "input", accelerate = false, brake = false, steer = 0, fire = false });
        Assert.True(inputPeer.Stopped);
        var (commandId, commandPeer) = Connect(hub);
        for (var i = 0; i < 3; i++) Send(hub, commandId, new { type = "leave" });
        Assert.True(commandPeer.Stopped);
        Assert.Equal(0, hub.ConnectionCount);
    }

    [Fact]
    public void InvalidJsonHasBoundedToleranceAndNeverCrashesHub()
    {
        var hub = new MultiplayerHub(new());
        var (id, peer) = Connect(hub);
        for (var i = 0; i < 8; i++) hub.Receive(id, Encoding.UTF8.GetBytes("{"), 0);
        Assert.True(peer.Stopped);
        Assert.Equal(0, hub.ConnectionCount);
    }

    [Fact]
    public void IdleRoomsAndConnectionsAreReclaimed()
    {
        var hub = new MultiplayerHub(new() { LobbyIdleSeconds = 2, ConnectionIdleSeconds = 5 });
        var (id, peer) = Connect(hub);
        Create(hub, id);
        hub.Tick(now: 3);
        Assert.Equal(0, hub.RoomCount);
        Assert.Equal(1, hub.ConnectionCount);
        hub.Tick(now: 6);
        Assert.True(peer.Stopped);
        Assert.Equal(0, hub.ConnectionCount);
    }

    [Fact]
    public void FailedSocketDoesNotBlockOtherMatchesAndForfeitsOnNextTick()
    {
        var hub = new MultiplayerHub(new());
        var (id1, peer1) = Connect(hub);
        var (id2, peer2) = Connect(hub);
        Create(hub, id1);
        Join(hub, id2, peer1.Room.Id);
        Ready(hub, id1);
        Ready(hub, id2);
        peer2.Reject = true;
        hub.Tick(now: 0);
        hub.Tick(now: .02);
        Assert.True(peer2.Stopped);
        Assert.Equal(id1, peer1.Room.ChampionId);
        Assert.Equal(1, hub.ConnectionCount);
    }

    [Fact]
    public void FinishedRoomExpiresAndHumanCanCreateAgain()
    {
        var hub = new MultiplayerHub(new() { FinishedRetentionSeconds = 1 });
        var (id1, peer1) = Connect(hub);
        var (id2, _) = Connect(hub);
        Create(hub, id1);
        Join(hub, id2, peer1.Room.Id);
        Ready(hub, id1);
        Ready(hub, id2);
        hub.Disconnect(id2, 0);
        hub.Tick(now: 2);
        Assert.Equal(0, hub.RoomCount);
        Create(hub, id1);
        Assert.Equal("waiting", peer1.Room.Status);
    }
}
