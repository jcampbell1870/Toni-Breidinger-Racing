using System.Numerics;

namespace ToniBreidingerRacing.Core.Racing;

/// <summary>
/// Arcade top-down car handling: the car drives where it points, slides a little through corners
/// (lateral velocity bleeds off according to <see cref="CarStats.Grip"/>) and bounces off the walls.
/// </summary>
public static class CarPhysics
{
    public const float ReverseSpeedFactor = 0.4f;
    public const float PuddleSpeedFactor = 0.5f;
    public const float BoostSpeedFactor = 1.55f;
    public const float SpinTurnRate = 13f;

    /// <summary>Advances one car by <paramref name="dt"/> seconds. Returns the speed at which it hit a wall, or 0.</summary>
    public static float Step(Car car, CarInput input, Track track, float dt)
    {
        var stats = car.Stats;
        var forward = car.Forward;
        var velocity = car.Velocity;
        var topSpeed = stats.TopSpeed * (car.OnPuddle ? PuddleSpeedFactor : 1f);

        if (car.SpinTimer > 0)
        {
            car.SpinTimer = MathF.Max(0, car.SpinTimer - dt);
            car.Heading = NormalizeAngle(car.Heading + SpinTurnRate * dt);
            velocity *= MathF.Exp(-2.2f * dt);
        }
        else
        {
            var speed = Vector2.Dot(velocity, forward);
            var steer = Math.Clamp(input.Steer, -1f, 1f);
            var steerFactor = Math.Clamp(MathF.Abs(speed) / 45f, 0f, 1f) * MathF.Sign(speed);
            car.Heading = NormalizeAngle(car.Heading + steer * stats.TurnRate * steerFactor * dt);
            forward = car.Forward;

            if (input.Accelerate)
            {
                velocity += forward * stats.Acceleration * dt;
            }
            else if (input.Brake)
            {
                // Brake hard while rolling forward, then creep backwards to get unstuck.
                velocity += forward * (speed > 5 ? -stats.Acceleration * 1.8f : -stats.Acceleration * 0.6f) * dt;
            }
            else
            {
                velocity *= MathF.Exp(-0.9f * dt);
            }

            // Grip: bleed off sideways sliding so the car follows its nose.
            var forwardSpeed = Vector2.Dot(velocity, forward);
            var lateral = velocity - forward * forwardSpeed;
            velocity = forward * forwardSpeed + lateral * MathF.Exp(-stats.Grip * dt);
        }

        if (car.BoostTimer > 0)
        {
            car.BoostTimer = MathF.Max(0, car.BoostTimer - dt);
            topSpeed = stats.TopSpeed * BoostSpeedFactor;
        }

        var currentSpeed = velocity.Length();
        var reversing = Vector2.Dot(velocity, car.Forward) < 0;
        var limit = reversing ? stats.TopSpeed * ReverseSpeedFactor : topSpeed;
        if (currentSpeed > limit)
        {
            // Slow down smoothly (puddles, end of boost) instead of snapping to the limit.
            var target = MathF.Max(limit, currentSpeed * MathF.Exp(-4f * dt));
            velocity *= target / currentSpeed;
        }

        car.Velocity = velocity;
        car.Position += velocity * dt;
        return ResolveWalls(car, track);
    }

    /// <summary>Keeps the car between the barriers. Returns the impact speed into the wall (0 when no contact).</summary>
    public static float ResolveWalls(Car car, Track track)
    {
        var projection = track.Project(car.Position);
        var limit = track.HalfWidth - Car.Radius;
        if (MathF.Abs(projection.Lateral) <= limit)
        {
            return 0;
        }

        var side = MathF.Sign(projection.Lateral);
        var outward = projection.Normal * side;
        car.Position = projection.Closest + outward * limit;

        var velocity = car.Velocity;
        var impact = Vector2.Dot(velocity, outward);
        if (impact > 0)
        {
            velocity -= outward * impact * 1.4f;
            velocity *= 0.85f;
            car.Velocity = velocity;
        }

        return MathF.Max(0, impact);
    }

    /// <summary>Pushes overlapping cars apart and trades momentum along the contact line.</summary>
    public static bool ResolveCarCollision(Car a, Car b)
    {
        var delta = b.Position - a.Position;
        var distance = delta.Length();
        var minimum = Car.Radius * 2;
        if (distance >= minimum)
        {
            return false;
        }

        var normal = distance > 0.001f ? delta / distance : Vector2.UnitX;
        var overlap = minimum - distance;
        a.Position -= normal * (overlap / 2);
        b.Position += normal * (overlap / 2);

        var relative = Vector2.Dot(b.Velocity - a.Velocity, normal);
        if (relative < 0)
        {
            var impulse = -relative * 0.9f / 2;
            a.Velocity -= normal * impulse;
            b.Velocity += normal * impulse;
        }

        return true;
    }

    public static float NormalizeAngle(float angle)
    {
        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle <= -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }
}
