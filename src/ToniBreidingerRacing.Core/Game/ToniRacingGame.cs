using System.Numerics;
using ToniBreidingerRacing.Core.Audio;
using ToniBreidingerRacing.Core.Configuration;
using ToniBreidingerRacing.Core.Racing;
using ToniBreidingerRacing.Core.Rendering;
using ToniBreidingerRacing.Core.Rewards;

namespace ToniBreidingerRacing.Core.Game;

public enum GameScreen
{
    Title,
    HowToPlay,
    TrackIntro,
    Racing,
    Paused,
    Results,
    Champion,
    GameOver,
}

/// <summary>
/// The whole game as a platform-independent state machine. The Windows shell feeds it keyboard state once per
/// frame, calls <see cref="Render"/>, and shows <see cref="Frame"/> scaled up on screen.
/// </summary>
public sealed partial class ToniRacingGame : IAsyncDisposable
{
    public const int WalletMaxLength = 42;
    public const double FinishDelaySeconds = 3.5;

    private static readonly string[] TitleMenu = ["START RACE", "SET A1870 WALLET", "HOW TO PLAY", "QUIT"];
    private static readonly string[] PauseMenu = ["RESUME", "RETIRE TO TITLE"];

    private readonly GameSettings _settings;
    private readonly Action _saveProfile;
    private readonly IGameHost _host;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, FrameBuffer> _trackMaps = [];
    private readonly Random _random = new();
    private int _menuIndex;
    private double _finishTimer;
    private Vector2 _camera;
    private string _message = string.Empty;
    private byte _messageColor = Pal.White;
    private double _messageUntil;

    public ToniRacingGame(
        GameSettings settings,
        PlayerProfile profile,
        Action saveProfile,
        RewardIssuerClient rewardClient,
        IGameHost host,
        TimeProvider? timeProvider = null)
    {
        _settings = settings;
        Profile = profile;
        _saveProfile = saveProfile;
        _host = host;
        _timeProvider = timeProvider ?? TimeProvider.System;
        Rewards = new RewardDesk(rewardClient);
        Rewards.ClaimReady += OnClaimReady;
    }

    public FrameBuffer Frame { get; } = new();

    public GameScreen Screen { get; private set; } = GameScreen.Title;

    public PlayerProfile Profile { get; }

    public RewardDesk Rewards { get; }

    public Season? Season { get; private set; }

    public Race? Race { get; private set; }

    public RaceResult? LastResult { get; private set; }

    public SeasonUpdate? LastUpdate { get; private set; }

    /// <summary>Seconds since the game started; drives blinking text and animations.</summary>
    public double Time { get; private set; }

    public int MenuIndex => _menuIndex;

    public void Update(double dt, InputState input)
    {
        dt = Math.Clamp(dt, 0, 0.25);
        Time += dt;
        Rewards.Poll();

        switch (Screen)
        {
            case GameScreen.Title:
                UpdateTitle(input);
                break;
            case GameScreen.HowToPlay:
                if (input.WasPressed(Buttons.Confirm | Buttons.Back))
                {
                    Play(SoundEffect.MenuSelect);
                    Screen = GameScreen.Title;
                }

                break;
            case GameScreen.TrackIntro:
                UpdateTrackIntro(input);
                break;
            case GameScreen.Racing:
                UpdateRacing(dt, input);
                break;
            case GameScreen.Paused:
                UpdatePaused(input);
                break;
            case GameScreen.Results:
                UpdateResults(input);
                break;
            case GameScreen.Champion:
                if (input.WasPressed(Buttons.Confirm))
                {
                    Play(SoundEffect.MenuSelect);
                    EnterTrackIntro();
                }
                else if (input.WasPressed(Buttons.Back))
                {
                    ReturnToTitle();
                }

                HandleRewardKeys(input);
                break;
            case GameScreen.GameOver:
                if (input.WasPressed(Buttons.Confirm | Buttons.Back))
                {
                    ReturnToTitle();
                }

                HandleRewardKeys(input);
                break;
        }
    }

    /// <summary>Starts a brand-new season at round 1.</summary>
    public void StartSeason()
    {
        Season = new Season();
        LastResult = null;
        LastUpdate = null;
        EnterTrackIntro();
    }

    /// <summary>Puts the grid on the current course and starts the countdown.</summary>
    public void StartRace()
    {
        if (Season is null)
        {
            StartSeason();
        }

        var track = Season!.CurrentTrack;
        Race = new Race(track, Season.CreateRaceSetup(_random.Next()));
        _camera = CameraTarget(Race);
        _finishTimer = 0;
        _message = string.Empty;
        Screen = GameScreen.Racing;
    }

    public FrameBuffer TrackMap(Track track)
    {
        if (!_trackMaps.TryGetValue(track.Id, out var map))
        {
            map = TrackRenderer.Render(track);
            _trackMaps[track.Id] = map;
        }

        return map;
    }

    public async ValueTask DisposeAsync() => await Rewards.DisposeAsync();

    private void UpdateTitle(InputState input)
    {
        if (MoveMenu(input, TitleMenu.Length))
        {
            return;
        }

        if (input.WasPressed(Buttons.Wallet))
        {
            PromptWallet();
            return;
        }

        if (input.WasPressed(Buttons.Back))
        {
            _host.Quit();
            return;
        }

        if (!input.WasPressed(Buttons.Confirm))
        {
            return;
        }

        Play(SoundEffect.MenuSelect);
        switch (_menuIndex)
        {
            case 0:
                StartSeason();
                break;
            case 1:
                PromptWallet();
                break;
            case 2:
                Screen = GameScreen.HowToPlay;
                break;
            default:
                _host.Quit();
                break;
        }
    }

    private void UpdateTrackIntro(InputState input)
    {
        HandleRewardKeys(input);
        if (input.WasPressed(Buttons.Confirm))
        {
            Play(SoundEffect.MenuSelect);
            StartRace();
        }
        else if (input.WasPressed(Buttons.Back))
        {
            ReturnToTitle();
        }
    }

    private void UpdateRacing(double dt, InputState input)
    {
        var race = Race!;
        if (input.WasPressed(Buttons.Back) && race.State != RaceState.Finished)
        {
            _menuIndex = 0;
            Screen = GameScreen.Paused;
            return;
        }

        var steer = (input.IsHeld(Buttons.Right) ? 1f : 0f) - (input.IsHeld(Buttons.Left) ? 1f : 0f);
        var carInput = new CarInput(input.IsHeld(Buttons.Gas), input.IsHeld(Buttons.Brake), steer, input.WasPressed(Buttons.Fire));
        race.Update((float)dt, carInput);
        HandleRaceEvents(race);
        UpdateCamera(race, (float)dt);

        if (race.State == RaceState.Finished)
        {
            _finishTimer += dt;
            if (_finishTimer >= FinishDelaySeconds || (_finishTimer > 1 && input.WasPressed(Buttons.Confirm)))
            {
                FinishRace();
            }
        }
    }

    private void UpdatePaused(InputState input)
    {
        if (MoveMenu(input, PauseMenu.Length))
        {
            return;
        }

        if (input.WasPressed(Buttons.Back) || (input.WasPressed(Buttons.Confirm) && _menuIndex == 0))
        {
            Play(SoundEffect.MenuSelect);
            Screen = GameScreen.Racing;
        }
        else if (input.WasPressed(Buttons.Confirm))
        {
            ReturnToTitle();
        }
    }

    private void UpdateResults(InputState input)
    {
        HandleRewardKeys(input);
        if (!input.WasPressed(Buttons.Confirm) || LastUpdate is not { } update)
        {
            return;
        }

        Play(SoundEffect.MenuSelect);
        switch (update.Outcome)
        {
            case SeasonOutcome.Advanced:
            case SeasonOutcome.ContinueUsed:
                EnterTrackIntro();
                break;
            case SeasonOutcome.SeasonChampion:
                Screen = GameScreen.Champion;
                break;
            default:
                Screen = GameScreen.GameOver;
                break;
        }
    }

    private void HandleRewardKeys(InputState input)
    {
        if (input.WasPressed(Buttons.Wallet))
        {
            if (PromptWallet() && Rewards.Status is RewardStatus.NeedsWallet or RewardStatus.Failed)
            {
                Rewards.Request(Profile.WalletAddress);
            }
        }
        else if (input.WasPressed(Buttons.Retry) && Rewards.Status is RewardStatus.Failed or RewardStatus.NeedsWallet)
        {
            Rewards.Request(Profile.WalletAddress);
        }
        else if (input.WasPressed(Buttons.Claim) && Rewards.PublishClaimPage() is { } url)
        {
            Play(SoundEffect.MenuSelect);
            _host.OpenUrl(url);
        }
    }

    private void FinishRace()
    {
        var race = Race!;
        var season = Season!;
        var result = race.Result!;
        var round = season.Round;
        var update = season.ApplyResult(result);
        LastResult = result;
        LastUpdate = update;

        var options = Rewards.Options;
        var proof = PlayReward.IsEligible(result.RaceSeconds, options)
            ? PlayReward.CreateProof(result.TrackId, round, result.Place, result.CarCount, _timeProvider.GetUtcNow().UtcDateTime)
            : null;

        Profile.RecordRace(
            new RaceRecord
            {
                GameId = proof?.GameId ?? string.Empty,
                TrackId = result.TrackId,
                Round = round,
                Place = result.Place,
                RaceSeconds = result.RaceSeconds,
                PlayedAt = _timeProvider.GetUtcNow().UtcDateTime,
            },
            result.PlayerFinished);
        if (update.Outcome == SeasonOutcome.SeasonChampion)
        {
            Profile.SeasonTitles++;
        }

        _saveProfile();
        Rewards.Begin(proof, result.TrackName, Profile.WalletAddress);
        Play(result.Place <= Season.PodiumPlaces ? SoundEffect.Win : SoundEffect.Lose);
        Screen = GameScreen.Results;
    }

    private void EnterTrackIntro()
    {
        Race = null;
        TrackMap(Season!.CurrentTrack);
        Screen = GameScreen.TrackIntro;
    }

    private void ReturnToTitle()
    {
        Play(SoundEffect.MenuSelect);
        Race = null;
        Season = null;
        _menuIndex = 0;
        Screen = GameScreen.Title;
    }

    private bool MoveMenu(InputState input, int count)
    {
        var delta = input.WasPressed(Buttons.Up) ? -1 : input.WasPressed(Buttons.Down) ? 1 : 0;
        if (delta == 0)
        {
            return false;
        }

        _menuIndex = (_menuIndex + delta + count) % count;
        Play(SoundEffect.MenuMove);
        return true;
    }

    /// <summary>Asks for the A1870 wallet. Returns true when a valid wallet is saved.</summary>
    private bool PromptWallet()
    {
        var value = _host.PromptText(
            "Arcade1870 wallet",
            "Enter the Ethereum wallet address (0x + 40 hex characters) that should receive your A1870 rewards:",
            Profile.WalletAddress,
            WalletMaxLength);
        if (value is null)
        {
            return false;
        }

        if (value.Length == 0)
        {
            Profile.WalletAddress = string.Empty;
            _saveProfile();
            ShowMessage("WALLET CLEARED", Pal.LightGray);
            return false;
        }

        if (!RewardClaimEncoder.IsValidAddress(value))
        {
            ShowMessage("INVALID WALLET ADDRESS", Pal.Red);
            return false;
        }

        Profile.WalletAddress = value;
        _saveProfile();
        ShowMessage("WALLET SAVED", Pal.Lime);
        return true;
    }

    private void OnClaimReady(RewardGameProof proof)
    {
        var record = Profile.History.LastOrDefault(r => r.GameId == proof.GameId);
        if (record is not null)
        {
            record.RewardIssued = true;
            _saveProfile();
        }

        Play(SoundEffect.Coin);
    }

    private void HandleRaceEvents(Race race)
    {
        foreach (var e in race.Events)
        {
            var isPlayer = e.Car?.IsPlayer == true;
            switch (e.Kind)
            {
                case RaceEventKind.CountdownBeep:
                    Play(SoundEffect.CountdownBeep);
                    break;
                case RaceEventKind.Go:
                    Play(SoundEffect.Go);
                    ShowMessage("GO!", Pal.Lime, 1);
                    break;
                case RaceEventKind.LapCompleted:
                    Play(SoundEffect.Lap);
                    ShowMessage($"LAP {e.Value}", Pal.White);
                    break;
                case RaceEventKind.FinalLap:
                    Play(SoundEffect.FinalLap);
                    ShowMessage("FINAL LAP!", Pal.Yellow);
                    break;
                case RaceEventKind.PickupCollected:
                    OnPickup(e);
                    break;
                case RaceEventKind.MissileFired when isPlayer:
                    Play(SoundEffect.Missile);
                    break;
                case RaceEventKind.CarHit:
                    Play(SoundEffect.Hit);
                    if (isPlayer)
                    {
                        ShowMessage("OUCH!", Pal.Red, 1);
                    }

                    break;
                case RaceEventKind.WallBump:
                case RaceEventKind.CarBump:
                    Play(SoundEffect.Bump);
                    break;
                case RaceEventKind.Boost when isPlayer:
                    Play(SoundEffect.Boost);
                    break;
                case RaceEventKind.OilSpin when isPlayer:
                    Play(SoundEffect.Oil);
                    ShowMessage("OIL SLICK!", Pal.LightGray, 1);
                    break;
                case RaceEventKind.RaceOver:
                    var place = e.Value;
                    ShowMessage(
                        race.Player.Finished ? $"FINISHED {Ordinal(place)}!" : "RACE OVER",
                        place <= Season.PodiumPlaces ? Pal.Yellow : Pal.Red,
                        FinishDelaySeconds);
                    break;
            }
        }
    }

    private void OnPickup(RaceEvent e)
    {
        switch (e.Pickup)
        {
            case PickupKind.Missiles:
                Play(SoundEffect.Pickup);
                ShowMessage("+3 MISSILES", Pal.White, 1.2);
                break;
            case PickupKind.Letter:
                Play(SoundEffect.Letter);
                ShowMessage($"LETTER {e.Letter}!", Pal.HotPink, 1.5);
                break;
            case PickupKind.Engine:
                Play(SoundEffect.Upgrade);
                ShowMessage("ENGINE UPGRADE!", Pal.Orange, 1.5);
                break;
            case PickupKind.Tires:
                Play(SoundEffect.Upgrade);
                ShowMessage("TIRE UPGRADE!", Pal.SkyBlue, 1.5);
                break;
            case PickupKind.TopSpeed:
                Play(SoundEffect.Upgrade);
                ShowMessage("TOP SPEED UPGRADE!", Pal.Yellow, 1.5);
                break;
        }
    }

    private void ShowMessage(string text, byte color, double seconds = 1.6)
    {
        _message = text;
        _messageColor = color;
        _messageUntil = Time + seconds;
    }

    private void Play(SoundEffect effect)
    {
        if (_settings.SoundEnabled)
        {
            _host.PlaySound(effect);
        }
    }

    private void UpdateCamera(Race race, float dt)
    {
        var target = CameraTarget(race);
        _camera = Vector2.Lerp(_camera, target, 1 - MathF.Exp(-6f * dt));
    }

    private static Vector2 CameraTarget(Race race) => race.Player.Position + race.Player.Velocity * 0.35f;

    public static string Ordinal(int place) => place switch
    {
        1 => "1ST",
        2 => "2ND",
        3 => "3RD",
        _ => $"{place}TH",
    };

    public static string FormatTime(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds / 10:00}";
    }
}
