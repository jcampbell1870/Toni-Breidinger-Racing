using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Core.Game;

public enum SeasonOutcome
{
    /// <summary>Toni finished in the top three and moves on to the next course.</summary>
    Advanced,

    /// <summary>Toni missed the podium but had a continue left; the round is re-run.</summary>
    ContinueUsed,

    /// <summary>Toni missed the podium with no continues left.</summary>
    GameOver,

    /// <summary>Toni won through the final course; the season loops with faster rivals.</summary>
    SeasonChampion,
}

/// <summary>What a finished race did to the season.</summary>
public readonly record struct SeasonUpdate(SeasonOutcome Outcome, bool UnlockedToniTurbo, bool BonusContinue, int PointsEarned);

/// <summary>
/// The R.C. Pro-Am style championship: finish in the top three to advance, collect upgrades that stay on
/// Toni's car, and spell T-O-N-I to unlock the Toni Turbo (spelling it again earns a bonus continue). After the last course the season loops with faster rivals.
/// </summary>
public sealed class Season
{
    public const string Word = "TONI";
    public const int MaxUpgradeLevel = 3;
    public const int PodiumPlaces = 3;
    public const int StartingContinues = 2;

    private static readonly PickupKind[] UpgradeRotation = [PickupKind.Engine, PickupKind.Tires, PickupKind.TopSpeed];
    private static readonly int[] PointsByPlace = [0, 10, 6, 4, 1];

    private readonly HashSet<char> _letters = [];

    public Season(IReadOnlyList<Track>? tracks = null)
    {
        Tracks = tracks ?? TrackLibrary.All;
        if (Tracks.Count == 0)
        {
            throw new ArgumentException("A season needs at least one track.", nameof(tracks));
        }
    }

    public IReadOnlyList<Track> Tracks { get; }

    /// <summary>1-based round number across the whole run (keeps counting when the season loops).</summary>
    public int Round { get; private set; } = 1;

    public int RoundIndex => (Round - 1) % Tracks.Count;

    /// <summary>How many times the season has looped. Rivals get faster each loop.</summary>
    public int Tier => (Round - 1) / Tracks.Count;

    public Track CurrentTrack => Tracks[RoundIndex];

    public int Continues { get; private set; } = StartingContinues;

    public int Points { get; private set; }

    public int Wins { get; private set; }

    public int EngineLevel { get; private set; }

    public int TiresLevel { get; private set; }

    public int SpeedLevel { get; private set; }

    /// <summary>Unlocked by spelling T-O-N-I: a faster car with better grip for the rest of the run.</summary>
    public bool HasToniTurbo { get; private set; }

    public IReadOnlyCollection<char> Letters => _letters;

    public bool HasLetter(char letter) => _letters.Contains(char.ToUpperInvariant(letter));

    public char? NextLetter => Word.Cast<char?>().FirstOrDefault(c => !_letters.Contains(c!.Value));

    public CarStats PlayerStats()
    {
        var stats = CarStats.Rookie;
        stats = stats with
        {
            TopSpeed = stats.TopSpeed + 7 * SpeedLevel,
            Acceleration = stats.Acceleration + 18 * EngineLevel,
            TurnRate = stats.TurnRate + 0.2f * TiresLevel,
            Grip = stats.Grip + 1.2f * TiresLevel,
        };

        return HasToniTurbo
            ? stats with { TopSpeed = stats.TopSpeed * 1.08f, Acceleration = stats.Acceleration * 1.1f, Grip = stats.Grip + 1 }
            : stats;
    }

    public RaceSetup CreateRaceSetup(int seed) => new()
    {
        PlayerStats = PlayerStats(),
        Rivals = Rivals.ForRound(RoundIndex, Tier),
        Letter = NextLetter,
        Upgrade = NextUpgrade(),
        StartingMissiles = 3,
        Seed = seed,
    };

    /// <summary>Upgrade placed on the course this round, or null once every upgrade is maxed.</summary>
    public PickupKind? NextUpgrade()
    {
        for (var i = 0; i < UpgradeRotation.Length; i++)
        {
            var kind = UpgradeRotation[(Round - 1 + i) % UpgradeRotation.Length];
            if (LevelOf(kind) < MaxUpgradeLevel)
            {
                return kind;
            }
        }

        return null;
    }

    public int LevelOf(PickupKind kind) => kind switch
    {
        PickupKind.Engine => EngineLevel,
        PickupKind.Tires => TiresLevel,
        PickupKind.TopSpeed => SpeedLevel,
        _ => 0,
    };

    public static int PointsFor(int place) => place >= 1 && place < PointsByPlace.Length ? PointsByPlace[place] : 0;

    /// <summary>Applies a finished race: upgrades, letters, points, then advance / continue / game over.</summary>
    public SeasonUpdate ApplyResult(RaceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (var upgrade in result.UpgradesCollected)
        {
            switch (upgrade)
            {
                case PickupKind.Engine:
                    EngineLevel = Math.Min(MaxUpgradeLevel, EngineLevel + 1);
                    break;
                case PickupKind.Tires:
                    TiresLevel = Math.Min(MaxUpgradeLevel, TiresLevel + 1);
                    break;
                case PickupKind.TopSpeed:
                    SpeedLevel = Math.Min(MaxUpgradeLevel, SpeedLevel + 1);
                    break;
            }
        }

        foreach (var letter in result.LettersCollected)
        {
            var upper = char.ToUpperInvariant(letter);
            if (Word.Contains(upper))
            {
                _letters.Add(upper);
            }
        }

        var unlocked = false;
        var bonusContinue = false;
        if (Word.All(_letters.Contains))
        {
            if (HasToniTurbo)
            {
                Continues++;
                bonusContinue = true;
            }
            else
            {
                HasToniTurbo = true;
                unlocked = true;
            }

            _letters.Clear();
        }

        var points = PointsFor(result.Place);
        Points += points;
        if (result.Place == 1)
        {
            Wins++;
        }

        if (result.Place <= PodiumPlaces)
        {
            var finalRound = RoundIndex == Tracks.Count - 1;
            Round++;
            return new SeasonUpdate(finalRound ? SeasonOutcome.SeasonChampion : SeasonOutcome.Advanced, unlocked, bonusContinue, points);
        }

        if (Continues > 0)
        {
            Continues--;
            return new SeasonUpdate(SeasonOutcome.ContinueUsed, unlocked, bonusContinue, points);
        }

        return new SeasonUpdate(SeasonOutcome.GameOver, unlocked, bonusContinue, points);
    }
}
