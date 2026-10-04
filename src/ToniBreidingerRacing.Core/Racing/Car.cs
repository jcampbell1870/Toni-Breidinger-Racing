using System.Numerics;

namespace ToniBreidingerRacing.Core.Racing;

/// <summary>Driver controls for one simulation step. AI drivers produce the same input as the keyboard.</summary>
public readonly record struct CarInput(bool Accelerate, bool Brake, float Steer, bool Fire)
{
    public static CarInput None => default;
}

/// <summary>Handling numbers for a car. Upgrades raise these between races.</summary>
public readonly record struct CarStats(float TopSpeed, float Acceleration, float TurnRate, float Grip)
{
    public static CarStats Rookie => new(TopSpeed: 150, Acceleration: 135, TurnRate: 3.4f, Grip: 7.5f);

    public CarStats Scaled(float speedFactor) =>
        this with { TopSpeed = TopSpeed * speedFactor, Acceleration = Acceleration * speedFactor };
}

/// <summary>Livery of a car: main body colour and stripe colour (palette indices).</summary>
public readonly record struct CarLivery(byte Body, byte Stripe);

public sealed class Car
{
    public const float Radius = 6;

    public Car(int id, string driverName, CarLivery livery, CarStats stats, bool isPlayer)
    {
        Id = id;
        DriverName = driverName;
        Livery = livery;
        Stats = stats;
        IsPlayer = isPlayer;
    }

    public int Id { get; }

    public string DriverName { get; }

    public CarLivery Livery { get; }

    public bool IsPlayer { get; }

    public CarStats Stats { get; set; }

    public Vector2 Position { get; set; }

    public Vector2 Velocity { get; set; }

    /// <summary>Heading in radians. 0 points right (+X); positive angles turn clockwise on screen.</summary>
    public float Heading { get; set; }

    public Vector2 Forward => new(MathF.Cos(Heading), MathF.Sin(Heading));

    public float Speed => Vector2.Dot(Velocity, Forward);

    /// <summary>Seconds left in a spin-out (missile hit or oil slick). The driver has no control while spinning.</summary>
    public float SpinTimer { get; set; }

    /// <summary>Seconds left on a zipper boost, during which the car may exceed its top speed.</summary>
    public float BoostTimer { get; set; }

    public float FireCooldown { get; set; }

    public int Missiles { get; set; }

    /// <summary>Distance driven along the racing line since the start; negative while still behind the start line.</summary>
    public float TotalDistance { get; set; }

    public float LastTrackDistance { get; set; }

    public int LapsCompleted { get; set; }

    public double LapStartTime { get; set; }

    public double? BestLapTime { get; set; }

    public double? FinishTime { get; set; }

    public int? FinishPlace { get; set; }

    public bool Finished => FinishTime is not null;

    public bool OnPuddle { get; set; }
}
