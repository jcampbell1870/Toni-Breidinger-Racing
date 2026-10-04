using System.Numerics;

namespace ToniBreidingerRacing.Core.Racing;

public enum PickupKind
{
    /// <summary>Three homing-free missiles, fired straight ahead with Space.</summary>
    Missiles,

    /// <summary>One letter of T-O-N-I. Spell the full name to unlock the Toni Turbo car.</summary>
    Letter,

    /// <summary>Engine upgrade: quicker acceleration.</summary>
    Engine,

    /// <summary>Tire upgrade: sharper steering and more grip.</summary>
    Tires,

    /// <summary>Top speed upgrade.</summary>
    TopSpeed,
}

public sealed class Pickup
{
    public required PickupKind Kind { get; init; }

    public char Letter { get; init; }

    public required Vector2 Position { get; init; }

    public bool Active { get; set; } = true;

    public float RespawnTimer { get; set; }

    public bool Respawns => Kind == PickupKind.Missiles;
}

public sealed class Missile
{
    public const float Speed = 300;
    public const float Lifetime = 1.6f;

    public required Car Owner { get; init; }

    public required Vector2 Position { get; set; }

    public required Vector2 Velocity { get; init; }

    public float Life { get; set; } = Lifetime;

    public float Heading => MathF.Atan2(Velocity.Y, Velocity.X);
}

public enum RaceState
{
    Countdown,
    Racing,
    Finished,
}

public enum RaceEventKind
{
    CountdownBeep,
    Go,
    LapCompleted,
    FinalLap,
    PickupCollected,
    MissileFired,
    CarHit,
    WallBump,
    CarBump,
    Boost,
    OilSpin,
    CarFinished,
    RaceOver,
}

public readonly record struct RaceEvent(RaceEventKind Kind, Car? Car = null, PickupKind? Pickup = null, char Letter = '\0', int Value = 0);

/// <summary>A rival on the grid: name, livery, handling and how cautiously the AI takes corners.</summary>
public sealed record RivalSetup(string Name, CarLivery Livery, CarStats Stats, float CornerCaution);

public sealed class RaceSetup
{
    public required CarStats PlayerStats { get; init; }

    public CarLivery PlayerLivery { get; init; } = Racing.Rivals.ToniLivery;

    public string PlayerName { get; init; } = Racing.Rivals.ToniName;

    public required IReadOnlyList<RivalSetup> Rivals { get; init; }

    /// <summary>Letter of T-O-N-I placed on the course this race, if any.</summary>
    public char? Letter { get; init; }

    /// <summary>Upgrade placed on the course this race, if any.</summary>
    public PickupKind? Upgrade { get; init; }

    public int StartingMissiles { get; init; } = 3;

    public int Seed { get; init; }

    public float CountdownSeconds { get; init; } = 3;
}

public sealed class RaceResult
{
    public required string TrackId { get; init; }

    public required string TrackName { get; init; }

    /// <summary>Toni's finishing place (1 = winner).</summary>
    public required int Place { get; init; }

    public required int CarCount { get; init; }

    public required bool PlayerFinished { get; init; }

    public required double RaceSeconds { get; init; }

    public double? BestLapSeconds { get; init; }

    public required IReadOnlyList<char> LettersCollected { get; init; }

    public required IReadOnlyList<PickupKind> UpgradesCollected { get; init; }

    public required IReadOnlyList<string> Standings { get; init; }
}

/// <summary>One race: four cars, a countdown, laps, pickups, missiles and hazards.</summary>
public sealed class Race
{
    public const float StepSeconds = 1f / 60f;
    public const int MaxMissiles = 9;
    public const float PickupRadius = 11;

    private readonly List<RaceEvent> _events = [];
    private readonly Dictionary<Car, AiDriver> _drivers = [];
    private readonly AiDriver _autopilot;
    private readonly List<char> _letters = [];
    private readonly List<PickupKind> _upgrades = [];
    private readonly Dictionary<Car, float> _hazardImmunity = [];
    private float _wallBumpCooldown;
    private int _finishedCount;
    private int _lastCountdownWhole;

    public Race(Track track, RaceSetup setup)
    {
        Track = track;
        Setup = setup;
        CountdownRemaining = setup.CountdownSeconds;
        _lastCountdownWhole = (int)MathF.Ceiling(setup.CountdownSeconds) + 1;
        _autopilot = new AiDriver(99);

        var grid = new List<Car>();
        var player = new Car(0, setup.PlayerName, setup.PlayerLivery, setup.PlayerStats, isPlayer: true)
        {
            Missiles = setup.StartingMissiles,
        };
        for (var i = 0; i < setup.Rivals.Count; i++)
        {
            var rival = setup.Rivals[i];
            var car = new Car(i + 1, rival.Name, rival.Livery, rival.Stats, isPlayer: false);
            _drivers[car] = new AiDriver(i + 1, rival.CornerCaution);
            grid.Add(car);
        }

        // R.C. Pro-Am style 2x2 grid: Toni starts on the second row and has to fight her way forward.
        grid.Insert(Math.Min(2, grid.Count), player);
        for (var slot = 0; slot < grid.Count; slot++)
        {
            var car = grid[slot];
            var back = 22 + (slot / 2) * 26f;
            var lateral = slot % 2 == 0 ? -13f : 13f;
            var distance = track.Wrap(-back);
            car.Position = track.PositionAt(distance, lateral);
            var tangent = track.TangentAt(distance);
            car.Heading = MathF.Atan2(tangent.Y, tangent.X);
            car.TotalDistance = -back;
            car.LastTrackDistance = distance;
        }

        Cars = [player, .. grid.Where(c => !c.IsPlayer)];
        Player = player;
        Pickups = CreatePickups(track, setup);
    }

    public Track Track { get; }

    public RaceSetup Setup { get; }

    public IReadOnlyList<Car> Cars { get; }

    public Car Player { get; }

    public IReadOnlyList<Pickup> Pickups { get; }

    public List<Missile> Missiles { get; } = [];

    public RaceState State { get; private set; } = RaceState.Countdown;

    public float CountdownRemaining { get; private set; }

    /// <summary>Seconds since the green flag.</summary>
    public double Elapsed { get; private set; }

    public RaceResult? Result { get; private set; }

    /// <summary>T-O-N-I letters Toni has picked up so far this race.</summary>
    public IReadOnlyList<char> CollectedLetters => _letters;

    /// <summary>Events raised during the most recent <see cref="Update"/> call (sounds, messages).</summary>
    public IReadOnlyList<RaceEvent> Events => _events;

    public int CurrentLap(Car car) => Math.Clamp(car.LapsCompleted + 1, 1, Track.Laps);

    public IReadOnlyList<Car> Standings() =>
        Cars.OrderBy(c => c.FinishPlace ?? int.MaxValue)
            .ThenByDescending(c => c.TotalDistance)
            .ThenBy(c => c.Id)
            .ToList();

    public int PlaceOf(Car car)
    {
        var standings = Standings();
        for (var i = 0; i < standings.Count; i++)
        {
            if (ReferenceEquals(standings[i], car))
            {
                return i + 1;
            }
        }

        return standings.Count;
    }

    public void Update(float dt, CarInput playerInput)
    {
        _events.Clear();
        var remaining = Math.Clamp(dt, 0f, 0.25f);
        while (remaining > 1e-6f)
        {
            var step = MathF.Min(StepSeconds, remaining);
            Step(step, playerInput);
            remaining -= step;
        }
    }

    private void Step(float dt, CarInput playerInput)
    {
        if (State == RaceState.Countdown)
        {
            CountdownRemaining -= dt;
            var whole = (int)MathF.Ceiling(CountdownRemaining);
            if (whole < _lastCountdownWhole && whole > 0)
            {
                _events.Add(new RaceEvent(RaceEventKind.CountdownBeep, Value: whole));
            }

            _lastCountdownWhole = whole;
            if (CountdownRemaining <= 0)
            {
                CountdownRemaining = 0;
                State = RaceState.Racing;
                _events.Add(new RaceEvent(RaceEventKind.Go));
            }

            return;
        }

        Elapsed += dt;
        _wallBumpCooldown = MathF.Max(0, _wallBumpCooldown - dt);

        foreach (var car in Cars)
        {
            var input = car.IsPlayer
                ? (car.Finished || State == RaceState.Finished ? _autopilot.Drive(car, Track, Elapsed, dt) : playerInput)
                : _drivers[car].Drive(car, Track, Elapsed, dt);

            car.FireCooldown = MathF.Max(0, car.FireCooldown - dt);
            if (input.Fire && car.Missiles > 0 && car.FireCooldown <= 0 && car.SpinTimer <= 0)
            {
                FireMissile(car);
            }

            var impact = CarPhysics.Step(car, input, Track, dt);
            if (car.IsPlayer && impact > 45 && _wallBumpCooldown <= 0)
            {
                _wallBumpCooldown = 0.3f;
                _events.Add(new RaceEvent(RaceEventKind.WallBump, car));
            }

            ApplyFeatures(car, dt);
            UpdateProgress(car);
        }

        for (var i = 0; i < Cars.Count; i++)
        {
            for (var j = i + 1; j < Cars.Count; j++)
            {
                if (CarPhysics.ResolveCarCollision(Cars[i], Cars[j]) && (Cars[i].IsPlayer || Cars[j].IsPlayer) && _wallBumpCooldown <= 0)
                {
                    _wallBumpCooldown = 0.3f;
                    _events.Add(new RaceEvent(RaceEventKind.CarBump, Cars[i].IsPlayer ? Cars[j] : Cars[i]));
                }
            }
        }

        UpdateMissiles(dt);
        UpdatePickups(dt);
        CheckRaceOver();
    }

    private void FireMissile(Car car)
    {
        car.Missiles--;
        car.FireCooldown = 0.35f;
        var forward = car.Forward;
        Missiles.Add(new Missile
        {
            Owner = car,
            Position = car.Position + forward * (Car.Radius + 4),
            Velocity = forward * (Missile.Speed + MathF.Max(0, car.Speed)),
        });
        _events.Add(new RaceEvent(RaceEventKind.MissileFired, car));
    }

    private void ApplyFeatures(Car car, float dt)
    {
        car.OnPuddle = false;
        if (_hazardImmunity.TryGetValue(car, out var immunity))
        {
            _hazardImmunity[car] = MathF.Max(0, immunity - dt);
        }

        foreach (var feature in Track.Features)
        {
            if (Vector2.Distance(car.Position, Track.FeaturePosition(feature)) > feature.Radius + Car.Radius * 0.5f)
            {
                continue;
            }

            switch (feature.Kind)
            {
                case TrackFeatureKind.Zipper when car.BoostTimer < 0.6f && car.SpinTimer <= 0:
                    car.BoostTimer = 0.9f;
                    car.Velocity = car.Forward * car.Stats.TopSpeed * CarPhysics.BoostSpeedFactor;
                    _events.Add(new RaceEvent(RaceEventKind.Boost, car));
                    break;
                case TrackFeatureKind.Puddle:
                    car.OnPuddle = true;
                    break;
                case TrackFeatureKind.Oil when car.SpinTimer <= 0 && _hazardImmunity.GetValueOrDefault(car) <= 0:
                    car.SpinTimer = 0.8f;
                    _hazardImmunity[car] = 2f;
                    _events.Add(new RaceEvent(RaceEventKind.OilSpin, car));
                    break;
            }
        }
    }

    private void UpdateProgress(Car car)
    {
        var along = Track.Project(car.Position).Distance;
        var delta = along - car.LastTrackDistance;
        if (delta > Track.Length / 2)
        {
            delta -= Track.Length;
        }
        else if (delta < -Track.Length / 2)
        {
            delta += Track.Length;
        }

        car.TotalDistance += delta;
        car.LastTrackDistance = along;

        if (car.Finished)
        {
            return;
        }

        var laps = (int)MathF.Floor(car.TotalDistance / Track.Length);
        if (laps <= car.LapsCompleted)
        {
            return;
        }

        var lapTime = Elapsed - car.LapStartTime;
        car.BestLapTime = car.BestLapTime is { } best ? Math.Min(best, lapTime) : lapTime;
        car.LapStartTime = Elapsed;
        car.LapsCompleted = laps;

        if (car.LapsCompleted >= Track.Laps)
        {
            car.FinishTime = Elapsed;
            car.FinishPlace = ++_finishedCount;
            _events.Add(new RaceEvent(RaceEventKind.CarFinished, car, Value: car.FinishPlace.Value));
        }
        else if (car.IsPlayer)
        {
            _events.Add(new RaceEvent(
                car.LapsCompleted == Track.Laps - 1 ? RaceEventKind.FinalLap : RaceEventKind.LapCompleted,
                car,
                Value: car.LapsCompleted + 1));
        }
    }

    private void UpdateMissiles(float dt)
    {
        for (var i = Missiles.Count - 1; i >= 0; i--)
        {
            var missile = Missiles[i];
            missile.Position += missile.Velocity * dt;
            missile.Life -= dt;
            var hit = Cars.FirstOrDefault(c =>
                !ReferenceEquals(c, missile.Owner) && Vector2.Distance(c.Position, missile.Position) <= Car.Radius + 3);
            if (hit is not null)
            {
                hit.SpinTimer = 1.1f;
                hit.Velocity *= 0.3f;
                hit.BoostTimer = 0;
                _events.Add(new RaceEvent(RaceEventKind.CarHit, hit));
                Missiles.RemoveAt(i);
            }
            else if (missile.Life <= 0 || MathF.Abs(Track.Project(missile.Position).Lateral) > Track.HalfWidth)
            {
                Missiles.RemoveAt(i);
            }
        }
    }

    private void UpdatePickups(float dt)
    {
        foreach (var pickup in Pickups)
        {
            if (!pickup.Active)
            {
                if (pickup.Respawns)
                {
                    pickup.RespawnTimer -= dt;
                    if (pickup.RespawnTimer <= 0)
                    {
                        pickup.Active = true;
                    }
                }

                continue;
            }

            if (Player.Finished || State != RaceState.Racing
                || Vector2.Distance(Player.Position, pickup.Position) > PickupRadius)
            {
                continue;
            }

            pickup.Active = false;
            pickup.RespawnTimer = 6;
            switch (pickup.Kind)
            {
                case PickupKind.Missiles:
                    Player.Missiles = Math.Min(MaxMissiles, Player.Missiles + 3);
                    break;
                case PickupKind.Letter:
                    _letters.Add(pickup.Letter);
                    break;
                default:
                    _upgrades.Add(pickup.Kind);
                    break;
            }

            _events.Add(new RaceEvent(RaceEventKind.PickupCollected, Player, pickup.Kind, pickup.Letter));
        }
    }

    private void CheckRaceOver()
    {
        if (State != RaceState.Racing)
        {
            return;
        }

        var rivalsFinished = Cars.Where(c => !c.IsPlayer).All(c => c.Finished);
        if (!Player.Finished && !rivalsFinished)
        {
            return;
        }

        State = RaceState.Finished;
        Result = new RaceResult
        {
            TrackId = Track.Id,
            TrackName = Track.Name,
            Place = Player.FinishPlace ?? Cars.Count,
            CarCount = Cars.Count,
            PlayerFinished = Player.Finished,
            RaceSeconds = Player.FinishTime ?? Elapsed,
            BestLapSeconds = Player.BestLapTime,
            LettersCollected = [.. _letters],
            UpgradesCollected = [.. _upgrades],
            Standings = Standings().Select(c => c.DriverName).ToList(),
        };
        _events.Add(new RaceEvent(RaceEventKind.RaceOver, Player, Value: Result.Place));
    }

    private static List<Pickup> CreatePickups(Track track, RaceSetup setup)
    {
        var random = new Random(setup.Seed);
        float Lateral(float range) => (float)(random.NextDouble() * 2 - 1) * range;

        var pickups = new List<Pickup>();
        foreach (var fraction in new[] { 0.2f, 0.5f, 0.76f })
        {
            pickups.Add(new Pickup
            {
                Kind = PickupKind.Missiles,
                Position = track.PositionAt(track.Length * (fraction + Lateral(0.03f)), Lateral(16)),
            });
        }

        if (setup.Letter is { } letter)
        {
            pickups.Add(new Pickup
            {
                Kind = PickupKind.Letter,
                Letter = letter,
                Position = track.PositionAt(track.Length * (0.4f + Lateral(0.04f)), Lateral(14)),
            });
        }

        if (setup.Upgrade is { } upgrade)
        {
            pickups.Add(new Pickup
            {
                Kind = upgrade,
                Position = track.PositionAt(track.Length * (0.64f + Lateral(0.04f)), Lateral(14)),
            });
        }

        return pickups;
    }
}
