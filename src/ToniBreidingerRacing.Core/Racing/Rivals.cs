using ToniBreidingerRacing.Core.Rendering;

namespace ToniBreidingerRacing.Core.Racing;

/// <summary>Toni and the three fictional rivals she races against all season.</summary>
public static class Rivals
{
    public const string ToniName = "Toni Breidinger";

    public static readonly CarLivery ToniLivery = new(Pal.HotPink, Pal.White);

    public static IReadOnlyList<(string Name, CarLivery Livery)> Roster { get; } =
    [
        ("Rex Rocket", new CarLivery(Pal.Blue, Pal.White)),
        ("Sonny Sparks", new CarLivery(Pal.Yellow, Pal.Black)),
        ("Dusty Diesel", new CarLivery(Pal.Green, Pal.Yellow)),
    ];

    /// <summary>
    /// Rival field for a season round. Rivals get a little faster every round and much faster each
    /// time the season loops, but Toni (fully upgraded) is always the quickest car on the track.
    /// </summary>
    public static IReadOnlyList<RivalSetup> ForRound(int roundIndex, int tier)
    {
        var rivals = new List<RivalSetup>(Roster.Count);
        for (var i = 0; i < Roster.Count; i++)
        {
            var (name, livery) = Roster[i];
            var factor = 0.84f + 0.014f * roundIndex + 0.07f * tier + 0.025f * i;
            var stats = CarStats.Rookie.Scaled(MathF.Min(factor, 1.12f));
            var caution = MathF.Max(0.55f, 1.15f - 0.04f * roundIndex - 0.15f * tier - 0.05f * i);
            rivals.Add(new RivalSetup(name, livery, stats, caution));
        }

        return rivals;
    }
}
