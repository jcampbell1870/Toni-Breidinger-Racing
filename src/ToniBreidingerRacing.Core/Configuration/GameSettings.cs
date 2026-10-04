using System.Text.Json;
using ToniBreidingerRacing.Core.Rewards;

namespace ToniBreidingerRacing.Core.Configuration;

/// <summary>Game configuration loaded from <c>appsettings.json</c> next to the executable.</summary>
public sealed class GameSettings
{
    public const string FileName = "appsettings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public RewardTreasuryOptions Rewards { get; set; } = new();

    public bool SoundEnabled { get; set; } = true;

    public bool StartFullScreen { get; set; }

    public static GameSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new GameSettings();
        }

        return JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path), JsonOptions) ?? new GameSettings();
    }
}

public sealed class RaceRecord
{
    public string GameId { get; set; } = string.Empty;

    public string TrackId { get; set; } = string.Empty;

    public int Round { get; set; }

    public int Place { get; set; }

    public double RaceSeconds { get; set; }

    public DateTime PlayedAt { get; set; }

    public bool RewardIssued { get; set; }
}

/// <summary>Per-player data saved under <c>%APPDATA%\ToniBreidingerRacing</c>.</summary>
public sealed class PlayerProfile
{
    public const int MaxHistory = 200;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string WalletAddress { get; set; } = string.Empty;

    public int RacesRun { get; set; }

    public int Wins { get; set; }

    public int BestRound { get; set; }

    public int SeasonTitles { get; set; }

    /// <summary>Best race time in seconds per track id.</summary>
    public Dictionary<string, double> BestTimes { get; set; } = [];

    public List<RaceRecord> History { get; set; } = [];

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ToniBreidingerRacing",
            "player.json");

    /// <summary>Records a race. Returns true when it set a new best time for the track.</summary>
    public bool RecordRace(RaceRecord record, bool finished)
    {
        ArgumentNullException.ThrowIfNull(record);
        RacesRun++;
        if (record.Place == 1)
        {
            Wins++;
        }

        BestRound = Math.Max(BestRound, record.Round);
        History.Add(record);
        if (History.Count > MaxHistory)
        {
            History.RemoveRange(0, History.Count - MaxHistory);
        }

        if (!finished || record.RaceSeconds <= 0)
        {
            return false;
        }

        if (BestTimes.TryGetValue(record.TrackId, out var best) && best <= record.RaceSeconds)
        {
            return false;
        }

        BestTimes[record.TrackId] = record.RaceSeconds;
        return true;
    }

    public static PlayerProfile Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(path), JsonOptions) ?? new PlayerProfile();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable profile should never stop the game from starting.
        }

        return new PlayerProfile();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
