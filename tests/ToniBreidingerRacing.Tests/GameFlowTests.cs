using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ToniBreidingerRacing.Core.Audio;
using ToniBreidingerRacing.Core.Configuration;
using ToniBreidingerRacing.Core.Game;
using ToniBreidingerRacing.Core.Racing;
using ToniBreidingerRacing.Core.Rewards;

namespace ToniBreidingerRacing.Tests;

public class GameFlowTests
{
    private readonly FakeHost _host = new();
    private readonly PlayerProfile _profile = new();
    private readonly StubHandler _handler = new(
        HttpStatusCode.OK,
        JsonSerializer.Serialize(RewardTests.ValidPayload(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private int _saves;
    private Buttons _previous;

    private ToniRacingGame NewGame()
    {
        var settings = new GameSettings();
        var client = new RewardIssuerClient(new HttpClient(_handler), settings.Rewards, new FakeTimeProvider(RewardTests.Now));
        return new ToniRacingGame(settings, _profile, () => _saves++, client, _host, new FakeTimeProvider(RewardTests.Now));
    }

    private void Press(ToniRacingGame game, Buttons buttons)
    {
        Step(game, buttons);
        Step(game, Buttons.None);
    }

    private void Step(ToniRacingGame game, Buttons held)
    {
        game.Update(1.0 / 60, new InputState(held, held & ~_previous));
        _previous = held;
    }

    private void DriveRaceToResults(ToniRacingGame game)
    {
        var pilot = new AiDriver(5);
        for (var i = 0; i < 60 * 600 && game.Screen == GameScreen.Racing; i++)
        {
            var race = game.Race!;
            Step(game, TestHelpers.ToButtons(pilot.Drive(race.Player, race.Track, race.Elapsed, TestHelpers.Step)));
            if (i % 120 == 0)
            {
                game.Render();
            }
        }
    }

    [Fact]
    public void TitleMenu_NavigatesToHowToPlayAndBack()
    {
        var game = NewGame();
        game.Render();
        Press(game, Buttons.Down);
        Press(game, Buttons.Down);
        Assert.Equal(2, game.MenuIndex);
        Press(game, Buttons.Confirm);
        Assert.Equal(GameScreen.HowToPlay, game.Screen);
        game.Render();
        Press(game, Buttons.Back);
        Assert.Equal(GameScreen.Title, game.Screen);
        Press(game, Buttons.Back);
        Assert.Equal(1, _host.QuitCount);
    }

    [Fact]
    public void WalletPrompt_SavesOnlyValidAddresses()
    {
        var game = NewGame();
        _host.NextPromptAnswer = "not a wallet";
        Press(game, Buttons.Wallet);
        Assert.Equal(string.Empty, _profile.WalletAddress);

        _host.NextPromptAnswer = RewardTests.Wallet;
        Press(game, Buttons.Wallet);
        Assert.Equal(RewardTests.Wallet, _profile.WalletAddress);
        Assert.True(_saves > 0);
    }

    [Fact]
    public async Task FullRace_ShowsResultsRecordsProfileAndEarnsA1870()
    {
        _profile.WalletAddress = RewardTests.Wallet;
        var game = NewGame();

        Press(game, Buttons.Confirm);
        Assert.Equal(GameScreen.TrackIntro, game.Screen);
        Assert.Equal(1, game.Season!.Round);
        game.Render();

        Press(game, Buttons.Confirm);
        Assert.Equal(GameScreen.Racing, game.Screen);
        game.Render();

        Press(game, Buttons.Back);
        Assert.Equal(GameScreen.Paused, game.Screen);
        game.Render();
        Press(game, Buttons.Back);
        Assert.Equal(GameScreen.Racing, game.Screen);

        DriveRaceToResults(game);
        Assert.Equal(GameScreen.Results, game.Screen);
        Assert.NotNull(game.LastResult);
        Assert.Equal(1, _profile.RacesRun);
        Assert.Single(_profile.History);
        Assert.StartsWith("toni-", _profile.History[0].GameId);

        await RewardTests.WaitFor(() =>
        {
            Step(game, Buttons.None);
            return game.Rewards.Status != RewardStatus.Requesting;
        });
        Assert.Equal(RewardStatus.Ready, game.Rewards.Status);
        Assert.True(_profile.History[0].RewardIssued);
        Assert.Contains(SoundEffect.Coin, _host.Sounds);
        Assert.Equal(new Uri(RewardTreasuryOptions.CryptoHockeyRewardIssuerUrl), _handler.LastUri);
        game.Render();

        Press(game, Buttons.Claim);
        Assert.Single(_host.OpenedUrls);
        Assert.True(_host.OpenedUrls[0].IsLoopback);

        Press(game, Buttons.Confirm);
        Assert.Contains(game.Screen, new[] { GameScreen.TrackIntro, GameScreen.GameOver, GameScreen.Champion });
        game.Render();
        await game.DisposeAsync();
    }

    [Fact]
    public async Task RaceWithoutWallet_AsksForWalletOnResults()
    {
        var game = NewGame();
        Press(game, Buttons.Confirm);
        Press(game, Buttons.Confirm);
        DriveRaceToResults(game);

        Assert.Equal(GameScreen.Results, game.Screen);
        Assert.Equal(RewardStatus.NeedsWallet, game.Rewards.Status);
        Assert.Null(_handler.LastUri);

        _host.NextPromptAnswer = RewardTests.Wallet;
        Press(game, Buttons.Wallet);
        await RewardTests.WaitFor(() =>
        {
            Step(game, Buttons.None);
            return game.Rewards.Status is not (RewardStatus.Requesting or RewardStatus.NeedsWallet);
        });
        Assert.Equal(RewardStatus.Ready, game.Rewards.Status);
        await game.DisposeAsync();
    }
}
