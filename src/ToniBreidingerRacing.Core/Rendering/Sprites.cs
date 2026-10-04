using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Core.Rendering;

/// <summary>Procedurally built pixel-art sprites: rotated race cars, scenery, Toni's portrait and the trophy.</summary>
public static class Sprites
{
    public const int CarDirections = 32;
    public const int CarSpriteSize = 17;

    // Top-down race car, nose pointing right. B = body, S = stripe, K = tyre, W = windscreen, D = driver's helmet.
    private static readonly string[] CarTemplate =
    [
        "..KKK.....KKK..",
        ".BBBBBBBBBBBBB.",
        "SBBBBBWWBBBBBB.",
        "SBBBBDWWBBBBBBB",
        "SSSSSDWWSSSSSSS",
        "SBBBBDWWBBBBBBB",
        "SBBBBBWWBBBBBB.",
        ".BBBBBBBBBBBBB.",
        "..KKK.....KKK..",
    ];

    private static readonly Dictionary<CarLivery, Sprite[]> CarCache = [];
    private static readonly object CarCacheGate = new();

    /// <summary>The car sprite for a heading, from a set of <see cref="CarDirections"/> pre-rotated frames.</summary>
    public static Sprite Car(CarLivery livery, float heading)
    {
        Sprite[] frames;
        lock (CarCacheGate)
        {
            if (!CarCache.TryGetValue(livery, out frames!))
            {
                frames = Enumerable.Range(0, CarDirections)
                    .Select(i => RotateCar(livery, i * MathF.Tau / CarDirections))
                    .ToArray();
                CarCache[livery] = frames;
            }
        }

        var index = (int)MathF.Round(heading / MathF.Tau * CarDirections) % CarDirections;
        return frames[index < 0 ? index + CarDirections : index];
    }

    private static Sprite RotateCar(CarLivery livery, float angle)
    {
        var colors = new Dictionary<char, byte>
        {
            ['B'] = livery.Body,
            ['S'] = livery.Stripe,
            ['K'] = Pal.Black,
            ['W'] = Pal.SkyBlue,
            ['D'] = livery.Body == Pal.HotPink ? Pal.White : Pal.DarkGray,
        };
        var templateWidth = CarTemplate[0].Length;
        var templateHeight = CarTemplate.Length;
        var cx = (templateWidth - 1) / 2f;
        var cy = (templateHeight - 1) / 2f;
        var center = (CarSpriteSize - 1) / 2f;
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        var pixels = new byte[CarSpriteSize * CarSpriteSize];
        Array.Fill(pixels, Pal.Transparent);
        for (var y = 0; y < CarSpriteSize; y++)
        {
            for (var x = 0; x < CarSpriteSize; x++)
            {
                var rx = x - center;
                var ry = y - center;
                var sx = (int)MathF.Round(rx * cos + ry * sin + cx);
                var sy = (int)MathF.Round(-rx * sin + ry * cos + cy);
                if (sx < 0 || sy < 0 || sx >= templateWidth || sy >= templateHeight)
                {
                    continue;
                }

                if (colors.TryGetValue(CarTemplate[sy][sx], out var color))
                {
                    pixels[y * CarSpriteSize + x] = color;
                }
            }
        }

        return new Sprite(CarSpriteSize, CarSpriteSize, pixels);
    }

    public static Sprite Tree { get; } = Sprite.FromRows(
        [
            "...gggg...",
            ".gggGggg..",
            "ggGgggggg.",
            "gggggGgggg",
            "gGgggggGgg",
            "ggggGggggg",
            ".gggggggg.",
            "..gggggg..",
            "....tt....",
            "....tt....",
        ],
        new Dictionary<char, byte> { ['g'] = Pal.DarkGreen, ['G'] = Pal.Green, ['t'] = Pal.DarkBrown });

    public static Sprite Pine { get; } = Sprite.FromRows(
        [
            "....w....",
            "...gwg...",
            "...ggg...",
            "..gwggg..",
            "..ggggg..",
            ".gggwggg.",
            ".ggggggg.",
            "gggggwggg",
            "ggggggggg",
            "....t....",
            "....t....",
        ],
        new Dictionary<char, byte> { ['g'] = Pal.DarkGreen, ['w'] = Pal.Snow, ['t'] = Pal.DarkBrown });

    public static Sprite Cactus { get; } = Sprite.FromRows(
        [
            "...gg...",
            "...gG...",
            "g..gg...",
            "g..gG..g",
            "gg.gg..g",
            ".gggG.gg",
            "...ggggg",
            "...gG...",
            "...gg...",
            "...gG...",
        ],
        new Dictionary<char, byte> { ['g'] = Pal.Green, ['G'] = Pal.DarkGreen });

    public static Sprite Floodlight { get; } = Sprite.FromRows(
        [
            "yyyyyyy",
            "yYyYyYy",
            "kkkkkkk",
            "...k...",
            "...k...",
            "...k...",
            "...k...",
            "...k...",
            "..kkk..",
        ],
        new Dictionary<char, byte> { ['y'] = Pal.Yellow, ['Y'] = Pal.White, ['k'] = Pal.LightGray });

    public static Sprite Rock { get; } = Sprite.FromRows(
        [
            "..rrr...",
            ".rRrrr..",
            "rRrrrrr.",
            "rrrrrrrd",
            ".rrrrdd.",
        ],
        new Dictionary<char, byte> { ['r'] = Pal.Gray, ['R'] = Pal.LightGray, ['d'] = Pal.DarkGray });

    /// <summary>Scenery sprite for a track theme.</summary>
    public static Sprite SceneryFor(TrackTheme theme, int variant) => theme switch
    {
        TrackTheme.Desert => variant % 3 == 0 ? Rock : Cactus,
        TrackTheme.Snow => Pine,
        TrackTheme.Night => variant % 3 == 0 ? Floodlight : Tree,
        TrackTheme.Dirt => variant % 3 == 0 ? Rock : Tree,
        _ => Tree,
    };

    /// <summary>Head-and-shoulders portrait of Toni in her pink fire suit (drawn as a left half and mirrored).</summary>
    public static Sprite ToniPortrait { get; } = Sprite.FromRows(
        Sprite.Mirror(
        [
            "...........HHHHH",
            ".........HHHHHHH",
            "........HHHHHHHh",
            ".......HHHHHhhhh",
            "......HHHHHhSSSS",
            "......HHHHhSSSSS",
            ".....HHHHSSSSSSS",
            ".....HHHSSHHHSSS",
            ".....HHHSSSSSSSS",
            ".....HHHSSWEESSS",
            ".....HHHSSSSSSSS",
            ".....HHHSSSSSSSs",
            ".....HHHHSSSSSSs",
            ".....HHHHSSSSSSS",
            ".....HHHHSSSLLLL",
            ".....HHHHHSSSLLL",
            "....HHHHHHHSSSSS",
            "....HHHHHHHHsSSS",
            "...HHHHHHHHHsSSS",
            "...HHHHHHHHWWsSS",
            "..HHHHHHPPPWWWWW",
            "..HHHHHPPPPPWWWW",
            ".HHHHHPPPPPPPWWP",
            ".HHHHPPPPPPPPPPP",
            ".HHHPPPPPPPPPPPP",
            "..HPPPPPPPPPPPPP",
            "..PPPPWWPPPPPPPP",
            ".PPPPWWPPPPPPPPP",
            ".PPPWWPPPPPPPPPP",
            "PPPPPPPPPPPPPPPP",
            "PPPPPPPPPPPPPPPP",
            "PPPPPPPPPPPPPPPP",
        ]),
        new Dictionary<char, byte>
        {
            ['H'] = Pal.Hair,
            ['h'] = Pal.HairLight,
            ['S'] = Pal.Skin,
            ['s'] = Pal.SkinShade,
            ['E'] = Pal.Black,
            ['W'] = Pal.White,
            ['L'] = Pal.Red,
            ['P'] = Pal.HotPink,
        });

    public static Sprite Trophy { get; } = Sprite.FromRows(
        Sprite.Mirror(
        [
            "yyyyyyyy",
            "y..yYYYY",
            "y..yYyyy",
            ".y.yYyyy",
            "..yyyyyy",
            "....yyyy",
            "......yy",
            "......yy",
            ".....yyy",
            "...bbbbb",
            "...bbbbb",
        ]),
        new Dictionary<char, byte> { ['y'] = Pal.Yellow, ['Y'] = Pal.White, ['b'] = Pal.Brown });
}
