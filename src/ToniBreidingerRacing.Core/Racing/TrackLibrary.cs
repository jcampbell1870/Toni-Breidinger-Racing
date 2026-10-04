using System.Numerics;

namespace ToniBreidingerRacing.Core.Racing;

/// <summary>The eight-course Toni Breidinger Racing season. Courses get longer and twistier as the season goes on.</summary>
public static class TrackLibrary
{
    public const float StandardRoadWidth = 64;

    private static readonly Lazy<IReadOnlyList<Track>> AllTracks = new(CreateAll);

    public static IReadOnlyList<Track> All => AllTracks.Value;

    public static Track Get(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"Unknown track '{id}'.");

    private static IReadOnlyList<Track> CreateAll() =>
    [
        Build("sunshine-speedway", "Sunshine Speedway", TrackTheme.Grass, 3,
            [(100, 100), (500, 80), (900, 100), (1000, 300), (900, 500), (500, 520), (100, 500), (0, 300)],
            Zip(0.15f, 0), Zip(0.62f, 0), Pud(0.38f, -14), Pud(0.86f, 14)),

        Build("desert-dunes", "Desert Dunes", TrackTheme.Desert, 3,
            [(100, 100), (700, 100), (900, 200), (900, 450), (700, 500), (550, 400), (400, 450), (300, 650), (100, 650), (0, 400)],
            Zip(0.08f, -10), Zip(0.55f, 10), Oil(0.3f, 12), Pud(0.72f, -12), Oil(0.9f, -14)),

        Build("pine-ridge", "Pine Ridge Raceway", TrackTheme.Grass, 3,
            [(0, 0), (400, -50), (700, 50), (900, 0), (1100, 150), (1000, 400), (750, 400), (600, 550), (750, 700), (600, 850), (250, 850), (100, 650), (200, 450), (0, 300)],
            Zip(0.05f, 0), Zip(0.48f, -12), Zip(0.8f, 12), Pud(0.25f, 10), Oil(0.6f, -10), Pud(0.92f, -14)),

        Build("harbor-hairpins", "Harbor Hairpins", TrackTheme.Dirt, 3,
            [(0, 0), (900, 0), (1000, 120), (900, 240), (300, 240), (200, 360), (300, 480), (900, 480), (1000, 600), (900, 720), (0, 720), (-100, 360)],
            Zip(0.04f, 0), Zip(0.3f, 10), Zip(0.7f, -10), Pud(0.17f, -12), Oil(0.45f, 12), Pud(0.58f, 0), Oil(0.86f, -12)),

        Build("midnight-mile", "Midnight Mile", TrackTheme.Night, 3,
            [(0, 0), (600, 0), (800, 150), (700, 350), (500, 300), (350, 400), (500, 550), (800, 550), (900, 750), (600, 850), (100, 800), (-50, 500), (100, 300), (-50, 150)],
            Zip(0.06f, 0), Zip(0.52f, 12), Oil(0.22f, -12), Oil(0.4f, 12), Pud(0.66f, -10), Zip(0.82f, -12)),

        Build("thunder-valley", "Thunder Valley", TrackTheme.Desert, 3,
            [(0, 0), (300, -100), (600, 0), (800, -150), (1100, 0), (1150, 300), (950, 450), (1100, 650), (900, 850), (500, 750), (300, 900), (0, 800), (100, 550), (-100, 350)],
            Zip(0.1f, 0), Zip(0.45f, 0), Zip(0.78f, 12), Oil(0.28f, 0), Pud(0.6f, -12), Oil(0.9f, 12)),

        Build("snowbird-summit", "Snowbird Summit", TrackTheme.Snow, 3,
            [(0, 0), (500, 0), (700, 180), (500, 330), (700, 470), (950, 400), (1120, 600), (870, 800), (450, 740), (250, 900), (-60, 740), (60, 470), (-100, 250)],
            Zip(0.05f, 0), Zip(0.5f, -12), Pud(0.2f, 12), Pud(0.35f, -12), Oil(0.66f, 10), Pud(0.86f, 0)),

        Build("breidinger-grand-prix", "Breidinger Grand Prix", TrackTheme.Night, 4,
            [(0, 0), (800, 0), (1100, 100), (1200, 350), (1000, 450), (800, 350), (600, 450), (700, 650), (1000, 700), (1200, 900), (900, 1050), (400, 1000), (200, 850), (300, 650), (100, 450), (-100, 250)],
            Zip(0.04f, 0), Zip(0.33f, 12), Zip(0.6f, -12), Zip(0.88f, 0), Oil(0.18f, -12), Oil(0.47f, 12), Pud(0.72f, -12), Oil(0.8f, 12)),
    ];

    private static Track Build(
        string id,
        string name,
        TrackTheme theme,
        int laps,
        (float X, float Y)[] controlPoints,
        params TrackFeature[] fractionalFeatures)
    {
        var points = controlPoints.Select(p => new Vector2(p.X, p.Y)).ToArray();
        var length = new Track(id, name, points, StandardRoadWidth, laps).Length;
        var features = fractionalFeatures.Select(f => f with { Distance = f.Distance * length }).ToArray();
        return new Track(id, name, points, StandardRoadWidth, laps, features) { Theme = theme };
    }

    private static TrackFeature Zip(float fraction, float lateral) => new(TrackFeatureKind.Zipper, fraction, lateral, 10);

    private static TrackFeature Pud(float fraction, float lateral) => new(TrackFeatureKind.Puddle, fraction, lateral, 12);

    private static TrackFeature Oil(float fraction, float lateral) => new(TrackFeatureKind.Oil, fraction, lateral, 9);
}
