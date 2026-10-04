namespace ToniBreidingerRacing.Core.Rewards;

/// <summary>"Just for playing" reward rules: every finished race earns A1870, whatever place you finish.</summary>
public static class PlayReward
{
    public const string Mode = "toni-racing";

    public static bool IsEligible(double raceSeconds, RewardTreasuryOptions options) =>
        options.Enabled && raceSeconds >= Math.Max(0, options.MinimumRaceSeconds);

    public static RewardGameProof CreateProof(string trackId, int round, int place, int carCount, DateTime completedAtUtc) => new()
    {
        GameId = $"toni-{trackId}-{Guid.NewGuid():N}",
        Mode = Mode,
        PlayerScore = Math.Max(0, carCount - place + 1),
        OpponentScore = Math.Max(0, place - 1),
        DifficultyLevel = $"Round {Math.Max(1, round)}",
        CompletedAt = completedAtUtc,
        PlayerWon = true,
    };
}
