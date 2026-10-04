using System.Numerics;
using ToniBreidingerRacing.Core.Racing;
using ToniBreidingerRacing.Core.Rendering;
using ToniBreidingerRacing.Core.Rewards;

namespace ToniBreidingerRacing.Core.Game;

public sealed partial class ToniRacingGame
{
    private const int HudHeight = 16;

    /// <summary>Draws the current screen into <see cref="Frame"/>.</summary>
    public void Render()
    {
        var f = Frame;
        switch (Screen)
        {
            case GameScreen.Title:
                DrawTitle(f);
                break;
            case GameScreen.HowToPlay:
                DrawHowToPlay(f);
                break;
            case GameScreen.TrackIntro:
                DrawTrackIntro(f);
                break;
            case GameScreen.Racing:
                DrawRace(f);
                break;
            case GameScreen.Paused:
                DrawRace(f);
                DrawPause(f);
                break;
            case GameScreen.Results:
                DrawResults(f);
                break;
            case GameScreen.Champion:
                DrawChampion(f);
                break;
            case GameScreen.GameOver:
                DrawGameOver(f);
                break;
        }

        if (Screen is not (GameScreen.Racing or GameScreen.Paused) && Time < _messageUntil && _message.Length > 0)
        {
            f.FillRect(0, 226, f.Width, 12, Pal.Black);
            PixelFont.DrawCentered(f, _message, f.Width / 2, 228, _messageColor);
        }
    }

    private bool Blink(double rate = 2) => (int)(Time * rate * 2) % 2 == 0;

    private static void DrawCheckeredStrip(FrameBuffer f, int y, int rows = 2, int size = 4)
    {
        for (var row = 0; row < rows; row++)
        {
            for (var x = 0; x < f.Width; x += size)
            {
                f.FillRect(x, y + row * size, size, size, ((x / size) + row) % 2 == 0 ? Pal.White : Pal.Black);
            }
        }
    }

    private static void DrawPanel(FrameBuffer f, int x, int y, int width, int height, byte border = Pal.White, byte fill = Pal.Black)
    {
        f.FillRect(x, y, width, height, fill);
        f.DrawRect(x, y, width, height, border);
    }

    private void DrawTitle(FrameBuffer f)
    {
        f.Clear(Pal.Navy);
        DrawCheckeredStrip(f, 0);
        PixelFont.DrawCentered(f, "TONI BREIDINGER", 128, 14, Pal.HotPink, 2, Pal.Black);
        PixelFont.DrawCentered(f, "RACING", 128, 34, Pal.Yellow, 3, Pal.Orange);

        DrawPanel(f, 10, 64, 68, 68, Pal.HotPink, Pal.SkyBlue);
        f.Blit(Sprites.ToniPortrait, 12, 66, 2);
        PixelFont.DrawCentered(f, "TONI", 44, 135, Pal.White);

        for (var i = 0; i < TitleMenu.Length; i++)
        {
            var selected = i == _menuIndex;
            var y = 70 + i * 13;
            if (selected)
            {
                PixelFont.Draw(f, ">", 92, y, Blink() ? Pal.Yellow : Pal.Orange);
            }

            PixelFont.Draw(f, TitleMenu[i], 102, y, selected ? Pal.Yellow : Pal.White);
        }

        var wallet = RewardClaimEncoder.IsValidAddress(Profile.WalletAddress)
            ? $"WALLET {ShortAddress(Profile.WalletAddress)}"
            : "NO WALLET - PRESS W";
        PixelFont.Draw(f, wallet, 92, 126, RewardClaimEncoder.IsValidAddress(Profile.WalletAddress) ? Pal.Lime : Pal.Pink);

        PixelFont.DrawCentered(f, $"EVERY RACE EARNS {Rewards.Options.RewardAmount} {Rewards.Options.TokenSymbol}!", 128, 148, Pal.Lime);
        PixelFont.DrawCentered(f, "SAME TREASURY AS CRYPTO HOCKEY", 128, 158, Pal.LightGray);
        PixelFont.DrawCentered(f, $"RACES {Profile.RacesRun}  WINS {Profile.Wins}  TITLES {Profile.SeasonTitles}", 128, 170, Pal.SkyBlue);

        DrawTitleRoad(f, 184);
        PixelFont.DrawCentered(f, "UNOFFICIAL FAN GAME", 128, 216, Pal.Gray);
    }

    private void DrawTitleRoad(FrameBuffer f, int y)
    {
        f.FillRect(0, y, f.Width, 28, Pal.Asphalt);
        for (var x = 0; x < f.Width; x += 8)
        {
            var color = (x / 8) % 2 == 0 ? Pal.Red : Pal.White;
            f.FillRect(x, y, 8, 3, color);
            f.FillRect(x, y + 25, 8, 3, color);
        }

        var liveries = new[] { Rivals.ToniLivery }.Concat(Rivals.Roster.Select(r => r.Livery)).ToArray();
        for (var i = 0; i < liveries.Length; i++)
        {
            // Toni leads the pack across the screen, of course.
            var x = (int)((Time * 90 - i * 30) % 320) - 32;
            var lane = i % 2 == 0 ? y + 4 : y + 10;
            var sprite = Sprites.Car(liveries[i], 0);
            f.Blit(sprite, x + 1, lane + 2, solidColor: Pal.Black);
            f.Blit(sprite, x, lane);
        }
    }

    private void DrawHowToPlay(FrameBuffer f)
    {
        f.Clear(Pal.Navy);
        DrawCheckeredStrip(f, 0);
        PixelFont.DrawCentered(f, "HOW TO PLAY", 128, 14, Pal.Yellow, 2, Pal.Black);
        string[] controls =
        [
            "UP / Z       GAS",
            "DOWN         BRAKE / REVERSE",
            "LEFT RIGHT   STEER",
            "SPACE / X    FIRE MISSILE",
            "ESC          PAUSE",
            "F11          FULL SCREEN",
        ];
        for (var i = 0; i < controls.Length; i++)
        {
            PixelFont.Draw(f, controls[i], 24, 38 + i * 11, Pal.White);
        }

        string[] rules =
        [
            "FINISH IN THE TOP 3 TO ADVANCE.",
            "MISS THE PODIUM AND YOU USE A CONTINUE.",
            "GRAB ENGINE, TIRE AND SPEED UPGRADES.",
            "SPELL T-O-N-I FOR THE TONI TURBO CAR!",
            "ZIPPERS BOOST YOU. DODGE OIL & PUDDLES.",
            "MISSILES SPIN OUT YOUR RIVALS.",
            $"EVERY RACE EARNS {Rewards.Options.RewardAmount} {Rewards.Options.TokenSymbol} TOKENS.",
            "SET A WALLET WITH W, CLAIM WITH C.",
        ];
        for (var i = 0; i < rules.Length; i++)
        {
            PixelFont.Draw(f, rules[i], 12, 116 + i * 11, i == 3 ? Pal.HotPink : i == 6 ? Pal.Lime : Pal.SkyBlue);
        }

        PixelFont.DrawCentered(f, "PRESS ENTER", 128, 210, Blink() ? Pal.Yellow : Pal.Navy);
    }

    private void DrawTrackIntro(FrameBuffer f)
    {
        var season = Season!;
        var track = season.CurrentTrack;
        f.Clear(Pal.Navy);
        DrawCheckeredStrip(f, 0);
        PixelFont.DrawCentered(f, $"ROUND {season.Round}", 128, 14, Pal.Yellow, 2, Pal.Black);
        PixelFont.DrawCentered(f, track.Name.ToUpperInvariant(), 128, 34, Pal.White, 1, Pal.Black);
        var tierText = season.Tier > 0 ? $"  SEASON {season.Tier + 1}" : string.Empty;
        PixelFont.DrawCentered(f, $"{track.Laps} LAPS{tierText}", 128, 45, Pal.SkyBlue);

        DrawPanel(f, 8, 58, 136, 112, Pal.LightGray, Pal.DarkGreen);
        DrawTrackOutline(f, track, 12, 62, 128, 104, Pal.LightGray, Pal.White);

        f.Blit(Sprites.ToniPortrait, 152, 58);
        PixelFont.Draw(f, "LET'S GO!", 188, 70, Pal.HotPink);
        DrawUpgradeBar(f, "ENGINE", season.EngineLevel, 152, 98, Pal.Orange);
        DrawUpgradeBar(f, "TIRES", season.TiresLevel, 152, 110, Pal.SkyBlue);
        DrawUpgradeBar(f, "SPEED", season.SpeedLevel, 152, 122, Pal.Yellow);
        DrawLetters(f, 152, 136, season.Letters);
        if (season.HasToniTurbo)
        {
            PixelFont.Draw(f, "TURBO", 210, 136, Pal.HotPink);
        }

        PixelFont.Draw(f, $"CONT {season.Continues}", 152, 150, Pal.White);
        PixelFont.Draw(f, $"PTS {season.Points}", 152, 160, Pal.White);

        if (Profile.BestTimes.TryGetValue(track.Id, out var best))
        {
            PixelFont.DrawCentered(f, $"BEST TIME {FormatTime(best)}", 128, 178, Pal.Lime);
        }

        if (Rewards.Status is RewardStatus.Ready or RewardStatus.ClaimPageOpened)
        {
            PixelFont.DrawCentered(f, $"C: CLAIM LAST RACE'S {Rewards.Transaction!.DisplayAmount} {Rewards.Transaction.TokenSymbol}", 128, 190, Pal.Lime);
        }
        else if (Rewards.Status is RewardStatus.NeedsWallet or RewardStatus.Failed)
        {
            PixelFont.DrawCentered(f, "W: SET WALLET  R: RETRY A1870 REWARD", 128, 190, Pal.Pink);
        }

        PixelFont.DrawCentered(f, "PRESS ENTER TO RACE", 128, 206, Blink() ? Pal.Yellow : Pal.Orange);
    }

    private static void DrawUpgradeBar(FrameBuffer f, string label, int level, int x, int y, byte color)
    {
        PixelFont.Draw(f, label, x, y, Pal.White);
        for (var i = 0; i < Season.MaxUpgradeLevel; i++)
        {
            f.FillRect(x + 44 + i * 10, y, 8, 7, i < level ? color : Pal.DarkGray);
        }
    }

    private static void DrawLetters(FrameBuffer f, int x, int y, IReadOnlyCollection<char> collected, IReadOnlyCollection<char>? extra = null)
    {
        for (var i = 0; i < Season.Word.Length; i++)
        {
            var letter = Season.Word[i];
            var has = collected.Contains(letter) || extra?.Contains(letter) == true;
            PixelFont.Draw(f, letter.ToString(), x + i * 10, y, has ? Pal.HotPink : Pal.DarkGray);
        }
    }

    /// <summary>Draws a course outline scaled to fit a box, with the start line marked.</summary>
    private static (float Scale, Vector2 Offset) DrawTrackOutline(FrameBuffer f, Track track, int x, int y, int width, int height, byte color, byte startColor)
    {
        var scale = MathF.Min(width / (float)track.WorldWidth, height / (float)track.WorldHeight);
        var offset = new Vector2(x + (width - track.WorldWidth * scale) / 2, y + (height - track.WorldHeight * scale) / 2);
        var points = track.Points;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i] * scale + offset;
            var b = points[(i + 1) % points.Count] * scale + offset;
            f.DrawLine((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, color);
        }

        var start = points[0] * scale + offset;
        f.FillRect((int)start.X - 1, (int)start.Y - 1, 3, 3, startColor);
        return (scale, offset);
    }

    private void DrawRace(FrameBuffer f)
    {
        var race = Race!;
        var track = race.Track;
        var map = TrackMap(track);
        var camX = (int)Math.Clamp(_camera.X - f.Width / 2f, 0, Math.Max(0, map.Width - f.Width));
        var camY = (int)Math.Clamp(_camera.Y - (f.Height + HudHeight) / 2f, -HudHeight, Math.Max(0, map.Height - f.Height));
        f.CopyFrom(map.Pixels, map.Width, map.Height, camX, camY, TrackRenderer.GroundColors(track.Theme).Dark);

        foreach (var pickup in race.Pickups.Where(p => p.Active))
        {
            DrawPickup(f, pickup, (int)pickup.Position.X - camX, (int)pickup.Position.Y - camY);
        }

        foreach (var missile in race.Missiles)
        {
            var p = missile.Position - new Vector2(camX, camY);
            var dir = Vector2.Normalize(missile.Velocity);
            var tail = p - dir * 4;
            var flame = p - dir * 7;
            f.DrawLine((int)tail.X, (int)tail.Y, (int)p.X, (int)p.Y, Pal.White);
            f.DrawLine((int)flame.X, (int)flame.Y, (int)tail.X, (int)tail.Y, Blink(8) ? Pal.Orange : Pal.Yellow);
        }

        foreach (var car in race.Cars.OrderBy(c => c.Position.Y))
        {
            DrawCar(f, car, camX, camY);
        }

        DrawHud(f, race);
        DrawMinimap(f, race);

        if (race.State == RaceState.Countdown)
        {
            var count = (int)MathF.Ceiling(race.CountdownRemaining);
            PixelFont.DrawCentered(f, count.ToString(), 128, 80, count == 1 ? Pal.Yellow : Pal.Red, 4, Pal.Black);
            PixelFont.DrawCentered(f, race.Track.Name.ToUpperInvariant(), 128, 120, Pal.White, 1, Pal.Black);
        }

        if (Time < _messageUntil && _message.Length > 0)
        {
            var scale = race.State == RaceState.Finished || _message == "GO!" ? 2 : 1;
            PixelFont.DrawCentered(f, _message, 128, 60, _messageColor, scale, Pal.Black);
        }
    }

    private void DrawCar(FrameBuffer f, Car car, int camX, int camY)
    {
        var sprite = Sprites.Car(car.Livery, car.Heading);
        var x = (int)MathF.Round(car.Position.X) - camX - sprite.Width / 2;
        var y = (int)MathF.Round(car.Position.Y) - camY - sprite.Height / 2;
        if (x < -sprite.Width || y < -sprite.Height || x > f.Width || y > f.Height)
        {
            return;
        }

        f.Blit(sprite, x + 2, y + 3, solidColor: Pal.Black);
        if (car.BoostTimer > 0)
        {
            var back = car.Position - car.Forward * 9 - new Vector2(camX, camY);
            f.FillCircle((int)back.X, (int)back.Y, Blink(10) ? 2 : 3, Blink(10) ? Pal.Yellow : Pal.Orange);
        }

        f.Blit(sprite, x, y, solidColor: car.SpinTimer > 0 && Blink(6) ? Pal.White : null);
        if (car.IsPlayer && Race!.State == RaceState.Countdown)
        {
            PixelFont.DrawCentered(f, "TONI", x + sprite.Width / 2, y - 9, Pal.HotPink, 1, Pal.Black);
        }
    }

    private void DrawPickup(FrameBuffer f, Pickup pickup, int cx, int cy)
    {
        var (fill, label) = pickup.Kind switch
        {
            PickupKind.Missiles => (Pal.Red, "M"),
            PickupKind.Letter => (Pal.HotPink, pickup.Letter.ToString()),
            PickupKind.Engine => (Pal.Orange, "E"),
            PickupKind.Tires => (Pal.Blue, "T"),
            _ => (Pal.Yellow, "S"),
        };
        var border = pickup.Kind switch
        {
            PickupKind.Missiles => Pal.White,
            PickupKind.TopSpeed => Blink(4) ? Pal.HotPink : Pal.White,
            _ => Blink(4) ? Pal.Yellow : Pal.White,
        };

        f.FillRect(cx - 5, cy - 5 + 2, 11, 11, Pal.Black);
        f.FillRect(cx - 6, cy - 6, 11, 11, fill);
        f.DrawRect(cx - 6, cy - 6, 11, 11, border);
        PixelFont.Draw(f, label, cx - 3, cy - 4, pickup.Kind == PickupKind.TopSpeed ? Pal.Black : Pal.White);
    }

    private void DrawHud(FrameBuffer f, Race race)
    {
        var player = race.Player;
        f.FillRect(0, 0, f.Width, HudHeight, Pal.Black);
        f.FillRect(0, HudHeight - 1, f.Width, 1, Pal.HotPink);
        PixelFont.Draw(f, $"LAP {race.CurrentLap(player)}/{race.Track.Laps}", 4, 4, Pal.White);
        var place = race.PlaceOf(player);
        PixelFont.Draw(f, Ordinal(place), 58, 4, place <= Season.PodiumPlaces ? Pal.Yellow : Pal.Red);
        PixelFont.Draw(f, FormatTime(player.FinishTime ?? race.Elapsed), 86, 4, Pal.White);
        PixelFont.Draw(f, "M", 138, 4, Pal.Red);
        PixelFont.Draw(f, player.Missiles.ToString(), 146, 4, Pal.White);
        DrawSpeedometer(f, player, 160, 4);
        DrawLetters(f, 214, 4, Season?.Letters ?? [], race.CollectedLetters.ToHashSet());
    }

    private static void DrawSpeedometer(FrameBuffer f, Car car, int x, int y)
    {
        const int width = 46;
        f.DrawRect(x, y, width, 7, Pal.DarkGray);
        var fraction = Math.Clamp(MathF.Max(0, car.Speed) / (car.Stats.TopSpeed * CarPhysics.BoostSpeedFactor), 0, 1);
        var fill = (int)((width - 2) * fraction);
        f.FillRect(x + 1, y + 1, fill, 5, car.BoostTimer > 0 ? Pal.Orange : Pal.Lime);
    }

    private void DrawMinimap(FrameBuffer f, Race race)
    {
        const int x = 196;
        const int y = 186;
        const int width = 56;
        const int height = 50;
        DrawPanel(f, x, y, width, height, Pal.LightGray, Pal.Black);
        var (scale, offset) = DrawTrackOutline(f, race.Track, x + 3, y + 3, width - 6, height - 6, Pal.Gray, Pal.White);
        foreach (var car in race.Cars.Reverse())
        {
            if (car.IsPlayer && !Blink(4))
            {
                continue;
            }

            var p = car.Position * scale + offset;
            f.FillRect((int)p.X - 1, (int)p.Y - 1, 3, 3, car.Livery.Body);
        }
    }

    private void DrawPause(FrameBuffer f)
    {
        DrawPanel(f, 68, 84, 120, 62, Pal.HotPink);
        PixelFont.DrawCentered(f, "PAUSED", 128, 92, Pal.Yellow, 2);
        for (var i = 0; i < PauseMenu.Length; i++)
        {
            var selected = i == _menuIndex;
            PixelFont.Draw(f, (selected ? "> " : "  ") + PauseMenu[i], 80, 114 + i * 12, selected ? Pal.Yellow : Pal.White);
        }
    }

    private void DrawResults(FrameBuffer f)
    {
        var result = LastResult!;
        var update = LastUpdate!.Value;
        var season = Season!;
        f.Clear(Pal.Navy);
        DrawCheckeredStrip(f, 0);

        var podium = result.Place <= Season.PodiumPlaces;
        var headline = result.PlayerFinished ? $"{Ordinal(result.Place)} PLACE!" : "RACE OVER";
        PixelFont.DrawCentered(f, headline, 128, 14, podium ? Pal.Yellow : Pal.Red, 2, Pal.Black);
        PixelFont.DrawCentered(f, result.TrackName.ToUpperInvariant(), 128, 32, Pal.White);

        var standings = Race?.Standings() ?? [];
        for (var i = 0; i < standings.Count; i++)
        {
            var car = standings[i];
            var y = 46 + i * 11;
            var color = car.IsPlayer ? Pal.HotPink : Pal.White;
            PixelFont.Draw(f, Ordinal(i + 1), 10, y, color);
            f.FillRect(36, y, 7, 7, car.Livery.Body);
            PixelFont.Draw(f, car.DriverName.ToUpperInvariant(), 48, y, color);
            PixelFont.Draw(f, car.FinishTime is { } time ? FormatTime(time) : "--", 200, y, color);
        }

        var y2 = 46 + standings.Count * 11 + 4;
        var bestLap = result.BestLapSeconds is { } lap ? FormatTime(lap) : "--";
        PixelFont.Draw(f, $"BEST LAP {bestLap}", 10, y2, Pal.SkyBlue);
        PixelFont.Draw(f, $"PTS +{update.PointsEarned} ({season.Points})", 148, y2, Pal.SkyBlue);

        var (outcomeText, outcomeColor) = update.Outcome switch
        {
            SeasonOutcome.Advanced => ($"ON TO ROUND {season.Round}!", Pal.Lime),
            SeasonOutcome.ContinueUsed => ($"NO PODIUM - CONTINUE USED ({season.Continues} LEFT)", Pal.Orange),
            SeasonOutcome.SeasonChampion => ("SEASON CHAMPION!", Pal.Yellow),
            _ => ("NO PODIUM - GAME OVER", Pal.Red),
        };
        PixelFont.DrawCentered(f, outcomeText, 128, y2 + 14, outcomeColor);

        var y3 = y2 + 26;
        foreach (var upgrade in result.UpgradesCollected)
        {
            PixelFont.DrawCentered(f, $"{UpgradeName(upgrade)} UPGRADE INSTALLED", 128, y3, Pal.Orange);
            y3 += 10;
        }

        if (update.UnlockedToniTurbo)
        {
            PixelFont.DrawCentered(f, "T-O-N-I! TONI TURBO UNLOCKED!", 128, y3, Blink() ? Pal.HotPink : Pal.Pink);
            y3 += 10;
        }
        else if (update.BonusContinue)
        {
            PixelFont.DrawCentered(f, "T-O-N-I! BONUS CONTINUE!", 128, y3, Blink() ? Pal.HotPink : Pal.Pink);
            y3 += 10;
        }

        DrawRewardPanel(f, Math.Max(y3 + 2, 150));
        PixelFont.DrawCentered(f, "ENTER CONTINUE  C CLAIM  W WALLET", 128, 214, Pal.LightGray);
    }

    private void DrawRewardPanel(FrameBuffer f, int y)
    {
        DrawPanel(f, 6, y, 244, 58, Pal.Lime, Pal.Black);
        PixelFont.Draw(f, $"{Rewards.Options.TokenSymbol} REWARD", 12, y + 4, Pal.Yellow);
        PixelFont.Draw(f, $"+{Rewards.Options.RewardAmount} FOR PLAYING", 150, y + 4, Pal.Lime);
        var color = Rewards.Status switch
        {
            RewardStatus.Ready or RewardStatus.ClaimPageOpened => Pal.Lime,
            RewardStatus.Requesting => Blink() ? Pal.White : Pal.LightGray,
            RewardStatus.Failed or RewardStatus.NeedsWallet => Pal.Pink,
            _ => Pal.LightGray,
        };
        var lines = PixelFont.Wrap(Rewards.Message, 232);
        for (var i = 0; i < Math.Min(4, lines.Count); i++)
        {
            PixelFont.Draw(f, lines[i], 12, y + 16 + i * 10, color);
        }
    }

    private void DrawChampion(FrameBuffer f)
    {
        var season = Season!;
        f.Clear(Pal.Black);
        var confetti = new Random((int)(Time * 8));
        byte[] colors = [Pal.HotPink, Pal.Yellow, Pal.SkyBlue, Pal.Lime, Pal.White];
        for (var i = 0; i < 80; i++)
        {
            f.FillRect(confetti.Next(f.Width), confetti.Next(f.Height), 2, 2, colors[confetti.Next(colors.Length)]);
        }

        DrawCheckeredStrip(f, 0);
        PixelFont.DrawCentered(f, "SEASON CHAMPION!", 128, 16, Pal.Yellow, 2, Pal.Orange);
        f.Blit(Sprites.ToniPortrait, 40, 44, 2);
        f.Blit(Sprites.Trophy, 140, 56, 4);
        PixelFont.DrawCentered(f, "TONI BREIDINGER", 128, 118, Pal.HotPink, 2, Pal.Black);
        PixelFont.DrawCentered(f, "IS THE BEST RACER ON THE CIRCUIT!", 128, 138, Pal.White);
        PixelFont.DrawCentered(f, $"WINS {season.Wins}   POINTS {season.Points}", 128, 152, Pal.SkyBlue);
        DrawRewardPanel(f, 162);
        PixelFont.DrawCentered(f, "ENTER: NEXT SEASON  ESC: TITLE", 128, 226, Blink() ? Pal.Yellow : Pal.Orange);
    }

    private void DrawGameOver(FrameBuffer f)
    {
        var season = Season;
        f.Clear(Pal.Black);
        PixelFont.DrawCentered(f, "GAME OVER", 128, 30, Pal.Red, 3, Pal.Maroon);
        f.Blit(Sprites.ToniPortrait, 112, 64);
        PixelFont.DrawCentered(f, "TONI WILL BE BACK!", 128, 102, Pal.HotPink);
        if (season is not null)
        {
            PixelFont.DrawCentered(f, $"REACHED ROUND {season.Round}  POINTS {season.Points}", 128, 116, Pal.White);
        }

        DrawRewardPanel(f, 132);
        PixelFont.DrawCentered(f, "PRESS ENTER", 128, 206, Blink() ? Pal.Yellow : Pal.Black);
    }

    private static string UpgradeName(PickupKind kind) => kind switch
    {
        PickupKind.Engine => "ENGINE",
        PickupKind.Tires => "TIRE",
        PickupKind.TopSpeed => "TOP SPEED",
        _ => kind.ToString().ToUpperInvariant(),
    };

    private static string ShortAddress(string address) =>
        address.Length > 12 ? $"{address[..6]}..{address[^4..]}".ToUpperInvariant().Replace("0X", "0x") : address;
}
