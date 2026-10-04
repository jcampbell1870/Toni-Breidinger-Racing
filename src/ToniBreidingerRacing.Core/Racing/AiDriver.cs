namespace ToniBreidingerRacing.Core.Racing;

/// <summary>
/// Rival driver: chases a point a little way down the racing line, lifts for tight corners and
/// reverses out if it ends up facing a wall. Also used to auto-drive Toni's car after the flag.
/// </summary>
public sealed class AiDriver
{
    private readonly float _laneSeed;
    private float _stuckTime;
    private float _reverseTime;

    public AiDriver(int seed, float cornerCaution = 1f)
    {
        _laneSeed = seed * 1.7f;
        CornerCaution = cornerCaution;
    }

    /// <summary>How early the driver lifts for corners. Lower values drive more aggressively.</summary>
    public float CornerCaution { get; set; }

    public CarInput Drive(Car car, Track track, double raceTime, float dt)
    {
        var projection = track.Project(car.Position);
        var speed = car.Speed;
        var lookahead = 34 + MathF.Max(0, speed) * 0.3f;
        var lane = ChooseLane(track, projection.Distance, MathF.Sin((float)raceTime * 0.35f + _laneSeed) * track.HalfWidth * 0.3f);
        var target = track.PositionAt(projection.Distance + lookahead, lane);

        var toTarget = target - car.Position;
        var desired = MathF.Atan2(toTarget.Y, toTarget.X);
        var error = CarPhysics.NormalizeAngle(desired - car.Heading);

        if (_reverseTime > 0)
        {
            _reverseTime -= dt;
            return new CarInput(Accelerate: false, Brake: true, Steer: -MathF.Sign(error), Fire: false);
        }

        if (car.SpinTimer <= 0 && MathF.Abs(speed) < 12)
        {
            _stuckTime += dt;
            if (_stuckTime > 1.2f || MathF.Abs(error) > 2.2f)
            {
                _stuckTime = 0;
                _reverseTime = 0.6f;
            }
        }
        else
        {
            _stuckTime = 0;
        }

        var turnAhead = MathF.Abs(track.TurnBetween(projection.Distance, projection.Distance + 40 + MathF.Max(0, speed) * 0.7f));
        var cornerSpeed = car.Stats.TopSpeed * MathF.Max(0.5f, 1f - turnAhead * 0.32f * CornerCaution);
        var accelerate = speed < cornerSpeed && MathF.Abs(error) < 1.2f;
        var brake = speed > cornerSpeed + 18;

        return new CarInput(accelerate, brake, Math.Clamp(error * 2.6f, -1f, 1f), Fire: false);
    }

    /// <summary>Lines up for zipper pads and steers around oil and puddles coming up on the racing line.</summary>
    private static float ChooseLane(Track track, float distance, float preferredLane)
    {
        var lane = preferredLane;
        var limit = track.HalfWidth - Car.Radius - 2;
        var nearest = float.MaxValue;
        foreach (var feature in track.Features)
        {
            var ahead = track.Wrap(feature.Distance - distance);
            if (ahead > 130 || ahead >= nearest)
            {
                continue;
            }

            if (feature.Kind == TrackFeatureKind.Zipper)
            {
                nearest = ahead;
                lane = feature.Lateral;
            }
            else if (MathF.Abs(lane - feature.Lateral) < feature.Radius + Car.Radius + 4)
            {
                nearest = ahead;
                var clearance = feature.Radius + Car.Radius + 5;
                var left = feature.Lateral - clearance;
                var right = feature.Lateral + clearance;
                lane = MathF.Abs(left) <= limit && (MathF.Abs(left - preferredLane) < MathF.Abs(right - preferredLane) || MathF.Abs(right) > limit)
                    ? left
                    : right;
            }
        }

        return Math.Clamp(lane, -limit, limit);
    }
}
