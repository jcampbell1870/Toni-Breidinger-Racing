using System.Numerics;
using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Server;

public sealed class OnlinePlayer(string id, string name)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public bool Connected { get; set; } = true;
    public bool Ready { get; set; }
    public CarInput Input { get; set; }
    public double LastInputAt { get; set; } = double.NegativeInfinity;
}

public sealed class OnlineMatch
{
    private readonly ServerOptions _options;
    private readonly Track _track;
    private readonly (OnlinePlayer Player, Car Car)[] _racers;
    private readonly Dictionary<Car, float> _oilImmunity = [];
    private readonly List<Missile> _missiles = [];
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public int Round { get; }
    public int Index { get; }
    public OnlinePlayer? Player1 { get; }
    public OnlinePlayer? Player2 { get; }
    public OnlinePlayer? Winner { get; private set; }
    public string State { get; private set; } = "countdown";
    public double Countdown { get; private set; }
    public double Elapsed { get; private set; }
    public bool Finished => State == "finished";

    public OnlineMatch(Track track, OnlinePlayer? player1, OnlinePlayer? player2, int round, int index, ServerOptions options)
    {
        _track = track;
        _options = options;
        Player1 = player1;
        Player2 = player2;
        Round = round;
        Index = index;
        Countdown = options.CountdownSeconds;
        _racers = new[] { player1, player2 }.Where(p => p is not null).Select((p, slot) =>
        {
            var distance = track.Wrap(-22);
            var tangent = track.TangentAt(distance);
            var car = new Car(slot, p!.Name, new CarLivery(6, 10), CarStats.Rookie, true)
            {
                Position = track.PositionAt(distance, slot == 0 ? -13 : 13),
                Heading = MathF.Atan2(tangent.Y, tangent.X),
                TotalDistance = -22,
                LastTrackDistance = distance,
                Missiles = 3,
            };
            return (p!, car);
        }).ToArray();
        ResolveForfeits();
    }

    public void Step(float dt, double now)
    {
        if (Finished) return;
        ResolveForfeits();
        if (Finished) return;
        if (Countdown > 0)
        {
            Countdown = Math.Max(0, Countdown - dt);
            if (Countdown <= 0) State = "racing";
            return;
        }
        Elapsed += dt;
        if (Elapsed >= _options.RaceTimeoutSeconds)
        {
            // A timeout is DNF for both unfinished humans, not a progress-based fabricated win.
            Complete(null);
            return;
        }
        foreach (var (player, car) in _racers)
        {
            var input = now - player.LastInputAt <= _options.InputTimeoutSeconds ? player.Input : CarInput.None;
            car.FireCooldown = MathF.Max(0, car.FireCooldown - dt);
            if (input.Fire && car.Missiles > 0 && car.FireCooldown <= 0 && car.SpinTimer <= 0)
            {
                car.Missiles--;
                car.FireCooldown = .35f;
                _missiles.Add(new Missile
                {
                    Owner = car,
                    Position = car.Position + car.Forward * (Car.Radius + 4),
                    Velocity = car.Forward * (Missile.Speed + MathF.Max(0, car.Speed)),
                });
            }
            CarPhysics.Step(car, input, _track, dt);
            ApplyFeatures(car, dt);
        }
        if (_racers.Length == 2)
        {
            CarPhysics.ResolveCarCollision(_racers[0].Car, _racers[1].Car);
            foreach (var (_, car) in _racers) CarPhysics.ResolveWalls(car, _track);
        }
        StepMissiles(dt);
        foreach (var (_, car) in _racers) UpdateProgress(car, dt);
        var finisher = _racers.Where(r => r.Car.Finished)
            .OrderByDescending(r => r.Car.TotalDistance).ThenBy(r => r.Car.Id).FirstOrDefault();
        if (finisher.Player is not null) Complete(finisher.Player);
    }

    public void ResolveForfeits()
    {
        if (Finished) return;
        var connected = _racers.Where(r => r.Player.Connected).Select(r => r.Player).ToArray();
        if (connected.Length < 2) Complete(connected.SingleOrDefault());
    }

    private void Complete(OnlinePlayer? winner)
    {
        Winner = winner;
        State = "finished";
        Countdown = 0;
        foreach (var (_, car) in _racers) car.Velocity = Vector2.Zero;
    }

    private void UpdateProgress(Car car, float dt)
    {
        var along = _track.Project(car.Position).Distance;
        var delta = along - car.LastTrackDistance;
        if (delta > _track.Length / 2) delta -= _track.Length;
        if (delta < -_track.Length / 2) delta += _track.Length;
        // Nearest-segment jumps at hairpins cannot manufacture lap progress.
        var maximum = MathF.Max(car.Velocity.Length(), car.Stats.TopSpeed * CarPhysics.BoostSpeedFactor) * dt + Car.Radius * 2;
        car.TotalDistance += Math.Clamp(delta, -maximum, maximum);
        car.LastTrackDistance = along;
        car.LapsCompleted = Math.Max(0, (int)MathF.Floor(car.TotalDistance / _track.Length));
        if (car.LapsCompleted >= _track.Laps)
            car.FinishTime = Elapsed;
    }

    private void ApplyFeatures(Car car, float dt)
    {
        car.OnPuddle = false;
        _oilImmunity[car] = MathF.Max(0, _oilImmunity.GetValueOrDefault(car) - dt);
        foreach (var feature in _track.Features)
        {
            if (Vector2.Distance(car.Position, _track.FeaturePosition(feature)) > feature.Radius + Car.Radius / 2) continue;
            switch (feature.Kind)
            {
                case TrackFeatureKind.Zipper when car.BoostTimer < .6f && car.SpinTimer <= 0:
                    car.BoostTimer = .9f;
                    car.Velocity = car.Forward * car.Stats.TopSpeed * CarPhysics.BoostSpeedFactor;
                    break;
                case TrackFeatureKind.Puddle:
                    car.OnPuddle = true;
                    break;
                case TrackFeatureKind.Oil when car.SpinTimer <= 0 && _oilImmunity[car] <= 0:
                    car.SpinTimer = .8f;
                    _oilImmunity[car] = 2;
                    break;
            }
        }
    }

    private void StepMissiles(float dt)
    {
        for (var i = _missiles.Count - 1; i >= 0; i--)
        {
            var missile = _missiles[i];
            missile.Position += missile.Velocity * dt;
            missile.Life -= dt;
            var hit = _racers.Select(r => r.Car).FirstOrDefault(car =>
                car != missile.Owner && Vector2.Distance(car.Position, missile.Position) <= Car.Radius + 3);
            if (hit is not null)
            {
                hit.SpinTimer = 1.1f;
                hit.Velocity *= .3f;
                hit.BoostTimer = 0;
            }
            if (hit is not null || missile.Life <= 0 || !_track.IsOnRoad(missile.Position))
                _missiles.RemoveAt(i);
        }
    }

    public SnapshotDto Snapshot(string roomId)
    {
        var ordered = _racers.OrderBy(r => r.Player == Winner ? 0 : 1)
            .ThenByDescending(r => r.Car.TotalDistance).ThenBy(r => r.Car.Id).ToArray();
        return new("snapshot", roomId, Id, Round, State, Countdown, Elapsed, _track.Id,
            _racers.Select(r => new CarDto(r.Player.Id, r.Player.Name, r.Car.Position.X, r.Car.Position.Y,
                r.Car.Heading, r.Car.Speed, Math.Clamp(r.Car.LapsCompleted + 1, 1, _track.Laps),
                r.Car.Finished, Array.FindIndex(ordered, x => x.Player == r.Player) + 1)).ToArray(), Winner?.Id);
    }
}
