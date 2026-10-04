using System.Numerics;
using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Tests;

public class RaceTests
{
    private static Race NewRace(Track? track = null, char? letter = null, PickupKind? upgrade = null, int seed = 1) =>
        new(track ?? TrackLibrary.All[0], new RaceSetup
        {
            PlayerStats = CarStats.Rookie,
            Rivals = Rivals.ForRound(0, 0),
            Letter = letter,
            Upgrade = upgrade,
            Seed = seed,
        });

    [Fact]
    public void Grid_HasToniAndThreeRivals()
    {
        var race = NewRace();
        Assert.Equal(4, race.Cars.Count);
        Assert.Equal(Rivals.ToniName, race.Player.DriverName);
        Assert.True(race.Player.IsPlayer);
        Assert.Single(race.Cars, c => c.IsPlayer);
        Assert.Equal(3, race.Player.Missiles);
    }

    [Fact]
    public void Countdown_HoldsCarsThenStartsRace()
    {
        var race = NewRace();
        var start = race.Player.Position;
        var sawGo = false;
        for (var i = 0; i < 60 * 2; i++)
        {
            race.Update(TestHelpers.Step, new CarInput(true, false, 0, false));
        }

        Assert.Equal(RaceState.Countdown, race.State);
        Assert.Equal(start, race.Player.Position);

        for (var i = 0; i < 60 * 2; i++)
        {
            race.Update(TestHelpers.Step, new CarInput(true, false, 0, false));
            sawGo |= race.Events.Any(e => e.Kind == RaceEventKind.Go);
        }

        Assert.True(sawGo);
        Assert.Equal(RaceState.Racing, race.State);
        Assert.NotEqual(start, race.Player.Position);
    }

    [Theory]
    [MemberData(nameof(TrackTests.TrackIds), MemberType = typeof(TrackTests))]
    public void Autopilot_FinishesEveryCourseWithAllLaps(string id)
    {
        var race = NewRace(TrackLibrary.Get(id));
        TestHelpers.AutopilotToFinish(race);

        Assert.Equal(RaceState.Finished, race.State);
        var result = Assert.IsType<RaceResult>(race.Result);
        Assert.True(result.PlayerFinished);
        Assert.Equal(race.Track.Laps, race.Player.LapsCompleted);
        Assert.InRange(result.Place, 1, 4);
        Assert.Equal(4, result.CarCount);
        Assert.Equal(4, result.Standings.Count);
        Assert.NotNull(result.BestLapSeconds);
        Assert.True(result.RaceSeconds > 30, "Every race should be long enough to earn the A1870 play reward.");
    }

    [Fact]
    public void PlacesAreUniqueAndOrdered()
    {
        var race = NewRace();
        TestHelpers.AutopilotToFinish(race);
        var places = race.Cars.Select(race.PlaceOf).OrderBy(p => p).ToArray();
        Assert.Equal([1, 2, 3, 4], places);
    }

    [Fact]
    public void FiringMissile_UsesAmmoAndCreatesMissile()
    {
        var race = NewRace();
        while (race.State == RaceState.Countdown)
        {
            race.Update(TestHelpers.Step, CarInput.None);
        }

        race.Update(TestHelpers.Step, new CarInput(true, false, 0, true));
        Assert.Equal(2, race.Player.Missiles);
        Assert.Single(race.Missiles);
        Assert.Contains(race.Events, e => e.Kind == RaceEventKind.MissileFired);
    }

    [Fact]
    public void Missile_HittingRival_SpinsItOut()
    {
        var race = NewRace();
        while (race.State == RaceState.Countdown)
        {
            race.Update(TestHelpers.Step, CarInput.None);
        }

        var rival = race.Cars.First(c => !c.IsPlayer);
        var player = race.Player;
        rival.Position = player.Position + player.Forward * 40;
        rival.Velocity = Vector2.Zero;
        var hit = false;
        race.Update(TestHelpers.Step, new CarInput(false, false, 0, true));
        for (var i = 0; i < 60 && !hit; i++)
        {
            rival.Position = player.Position + player.Forward * 40;
            race.Update(TestHelpers.Step, CarInput.None);
            hit = race.Events.Any(e => e.Kind == RaceEventKind.CarHit && e.Car == rival);
        }

        Assert.True(hit);
        Assert.True(rival.SpinTimer > 0);
    }

    [Fact]
    public void DrivingOverLetterAndUpgrade_CollectsThem()
    {
        var race = NewRace(letter: 'T', upgrade: PickupKind.Engine);
        while (race.State == RaceState.Countdown)
        {
            race.Update(TestHelpers.Step, CarInput.None);
        }

        foreach (var pickup in race.Pickups.Where(p => p.Kind is PickupKind.Letter or PickupKind.Engine).ToList())
        {
            race.Player.Position = pickup.Position;
            race.Player.Velocity = Vector2.Zero;
            race.Update(TestHelpers.Step, CarInput.None);
            Assert.False(pickup.Active);
        }

        Assert.Equal(['T'], race.CollectedLetters);
        TestHelpers.AutopilotToFinish(race);
        Assert.Equal(['T'], race.Result!.LettersCollected);
        Assert.Equal([PickupKind.Engine], race.Result.UpgradesCollected);
    }

    [Fact]
    public void Physics_GasAcceleratesAndWallsKeepCarOnCourse()
    {
        var track = TrackLibrary.All[0];
        var car = new Car(0, "Test", Rivals.ToniLivery, CarStats.Rookie, true)
        {
            Position = track.PointAt(0),
            Heading = MathF.Atan2(track.TangentAt(0).Y, track.TangentAt(0).X),
        };

        for (var i = 0; i < 60; i++)
        {
            CarPhysics.Step(car, new CarInput(true, false, 0, false), track, TestHelpers.Step);
        }

        Assert.True(car.Speed > 80);
        Assert.True(car.Speed <= CarStats.Rookie.TopSpeed + 0.5f);

        car.Position = track.PositionAt(track.Length / 3, track.HalfWidth + 30);
        CarPhysics.ResolveWalls(car, track);
        Assert.True(MathF.Abs(track.Project(car.Position).Lateral) <= track.HalfWidth + 0.5f);
    }
}
