using ToniBreidingerRacing.Core.Game;
using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Tests;

public class SeasonTests
{
    private static RaceResult Result(int place, char[]? letters = null, PickupKind[]? upgrades = null) => new()
    {
        TrackId = "test",
        TrackName = "Test",
        Place = place,
        CarCount = 4,
        PlayerFinished = true,
        RaceSeconds = 60,
        LettersCollected = letters ?? [],
        UpgradesCollected = upgrades ?? [],
        Standings = ["A", "B", "C", "D"],
    };

    [Fact]
    public void Podium_AdvancesAndAwardsPoints()
    {
        var season = new Season();
        var update = season.ApplyResult(Result(1));
        Assert.Equal(SeasonOutcome.Advanced, update.Outcome);
        Assert.Equal(2, season.Round);
        Assert.Equal(10, season.Points);
        Assert.Equal(1, season.Wins);

        Assert.Equal(SeasonOutcome.Advanced, season.ApplyResult(Result(3)).Outcome);
        Assert.Equal(3, season.Round);
    }

    [Fact]
    public void MissingPodium_UsesContinuesThenGameOver()
    {
        var season = new Season();
        Assert.Equal(Season.StartingContinues, season.Continues);
        for (var i = 0; i < Season.StartingContinues; i++)
        {
            Assert.Equal(SeasonOutcome.ContinueUsed, season.ApplyResult(Result(4)).Outcome);
            Assert.Equal(1, season.Round);
        }

        Assert.Equal(SeasonOutcome.GameOver, season.ApplyResult(Result(4)).Outcome);
    }

    [Fact]
    public void WinningFinalCourse_IsChampionAndLoopsHarder()
    {
        var season = new Season();
        for (var i = 0; i < season.Tracks.Count - 1; i++)
        {
            Assert.Equal(SeasonOutcome.Advanced, season.ApplyResult(Result(1)).Outcome);
        }

        Assert.Equal(SeasonOutcome.SeasonChampion, season.ApplyResult(Result(2)).Outcome);
        Assert.Equal(1, season.Tier);
        Assert.Equal(0, season.RoundIndex);
        Assert.True(Rivals.ForRound(0, 1)[0].Stats.TopSpeed > Rivals.ForRound(0, 0)[0].Stats.TopSpeed);
    }

    [Fact]
    public void SpellingToni_UnlocksTurboThenBonusContinue()
    {
        var season = new Season();
        Assert.Equal('T', season.NextLetter);
        season.ApplyResult(Result(1, ['T', 'O']));
        Assert.Equal('N', season.NextLetter);
        var baseline = season.PlayerStats();

        var update = season.ApplyResult(Result(1, ['N', 'I']));
        Assert.True(update.UnlockedToniTurbo);
        Assert.True(season.HasToniTurbo);
        Assert.Empty(season.Letters);
        Assert.True(season.PlayerStats().TopSpeed > baseline.TopSpeed);

        var continues = season.Continues;
        update = season.ApplyResult(Result(1, ['T', 'O', 'N', 'I']));
        Assert.False(update.UnlockedToniTurbo);
        Assert.True(update.BonusContinue);
        Assert.Equal(continues + 1, season.Continues);
    }

    [Fact]
    public void Upgrades_ImproveCarAndCapAtMax()
    {
        var season = new Season();
        var rookie = season.PlayerStats();
        for (var i = 0; i < Season.MaxUpgradeLevel + 2; i++)
        {
            season.ApplyResult(Result(1, upgrades: [PickupKind.Engine, PickupKind.Tires, PickupKind.TopSpeed]));
        }

        Assert.Equal(Season.MaxUpgradeLevel, season.EngineLevel);
        Assert.Equal(Season.MaxUpgradeLevel, season.TiresLevel);
        Assert.Equal(Season.MaxUpgradeLevel, season.SpeedLevel);
        Assert.Null(season.NextUpgrade());
        var upgraded = season.PlayerStats();
        Assert.True(upgraded.Acceleration > rookie.Acceleration);
        Assert.True(upgraded.Grip > rookie.Grip);
        Assert.True(upgraded.TopSpeed > rookie.TopSpeed);
    }

    [Fact]
    public void RaceSetup_PlacesNextLetterAndUpgrade()
    {
        var season = new Season();
        var setup = season.CreateRaceSetup(42);
        Assert.Equal('T', setup.Letter);
        Assert.NotNull(setup.Upgrade);
        Assert.Equal(3, setup.Rivals.Count);
    }
}
