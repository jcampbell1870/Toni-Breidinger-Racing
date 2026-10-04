using System.Text;
using System.Text.Json;
using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Server;

public sealed record ClientCommand(string Type, string? Name = null, string? Mode = null,
    string? TrackId = null, string? RoomId = null, bool Ready = false, CarInput Input = default);
public sealed record PointDto(float X, float Y);
public sealed record FeatureDto(string Kind, float X, float Y, float Radius);
public sealed record TrackDto(string Id, string Name, PointDto[] Points, float RoadWidth, int Laps,
    int WorldWidth, int WorldHeight, FeatureDto[] Features);
public sealed record LobbyRoomDto(string Id, string Name, string Mode, string TrackId, int Count, int Capacity, string Status);
public sealed record PlayerDto(string Id, string Name, bool Ready, bool Connected);
public sealed record BracketDto(int Round, int Index, string? Player1Id, string? Player2Id, string? WinnerId, string Status);
public sealed record RoomDto(string Type, string Id, string Name, string Mode, string TrackId, string Status,
    PlayerDto[] Players, BracketDto[] Bracket, string? ChampionId);
public sealed record CarDto(string Id, string Name, float X, float Y, float Heading, float Speed, int Lap, bool Finished, int Place);
public sealed record SnapshotDto(string Type, string RoomId, string MatchId, int Round, string State,
    double Countdown, double Elapsed, string TrackId, CarDto[] Cars, string? WinnerId);

public static class Protocol
{
    public const int MaxMessageBytes = 4096;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static readonly TrackDto[] Tracks = TrackLibrary.All.Select(track => new TrackDto(
        track.Id, track.Name, track.Points.Select(p => new PointDto(p.X, p.Y)).ToArray(),
        track.RoadWidth, track.Laps, track.WorldWidth, track.WorldHeight,
        track.Features.Select(f =>
        {
            var p = track.FeaturePosition(f);
            return new FeatureDto(f.Kind.ToString().ToLowerInvariant(), p.X, p.Y, f.Radius);
        }).ToArray())).ToArray();

    public static byte[] Serialize(object message) => JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);

    public static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Racer";
        var result = new StringBuilder();
        foreach (var c in name.Take(128))
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.')
                result.Append(c);
            if (result.Length == 24) break;
        }
        return result.ToString().Trim() is { Length: > 0 } clean ? clean : "Racer";
    }

    public static bool TryParse(ReadOnlyMemory<byte> json, out ClientCommand? command)
    {
        command = null;
        if (json.Length > MaxMessageBytes) return false;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prop in root.EnumerateObject())
                if (!keys.Add(prop.Name)) return false;
            string? Text(string key) => root.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetString() : null;
            bool Boolean(string key, out bool value)
            {
                value = false;
                if (!root.TryGetProperty(key, out var p) || p.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                value = p.GetBoolean();
                return true;
            }
            var type = Text("type");
            switch (type)
            {
                case "create" when Text("mode") is "duel" or "tournament" && Text("trackId") is { Length: > 0 and <= 64 }:
                    command = new(type, Text("name"), Text("mode"), Text("trackId"));
                    break;
                case "join" when Text("roomId") is { Length: > 0 and <= 64 }:
                    command = new(type, Text("name"), RoomId: Text("roomId"));
                    break;
                case "ready" when Boolean("ready", out var ready):
                    command = new(type, Ready: ready);
                    break;
                case "leave":
                    command = new(type);
                    break;
                case "input" when Boolean("accelerate", out var accelerate) && Boolean("brake", out var brake) &&
                                  Boolean("fire", out var fire) && root.TryGetProperty("steer", out var steer) &&
                                  steer.ValueKind == JsonValueKind.Number && steer.TryGetSingle(out var number) &&
                                  float.IsFinite(number) && number is >= -1 and <= 1:
                    command = new(type, Input: new(accelerate, brake, number, fire));
                    break;
            }
            return command is not null;
        }
        catch (JsonException) { return false; }
    }
}
