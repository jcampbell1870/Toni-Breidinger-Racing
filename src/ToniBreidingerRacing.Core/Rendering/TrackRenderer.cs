using System.Numerics;
using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Core.Rendering;

/// <summary>
/// Pre-renders a whole course into one big paletted bitmap: themed grass, asphalt, red/white curbs,
/// barriers, the checkered start line, zipper pads, puddles, oil slicks, a grandstand and scenery.
/// The race view then just scrolls a 256×240 window over it, like an NES background layer.
/// </summary>
public static class TrackRenderer
{
    public const int CurbWidth = 4;
    public const int BarrierWidth = 4;

    public static (byte Light, byte Dark) GroundColors(TrackTheme theme) => theme switch
    {
        TrackTheme.Desert => (Pal.Sand, Pal.SandDark),
        TrackTheme.Snow => (Pal.Snow, Pal.Ice),
        TrackTheme.Night => (Pal.NightGrass, Pal.NightGrassDark),
        TrackTheme.Dirt => (Pal.Dirt, Pal.DirtDark),
        _ => (Pal.Grass, Pal.GrassDark),
    };

    public static FrameBuffer Render(Track track, int seed = 1)
    {
        var width = track.WorldWidth;
        var height = track.WorldHeight;
        var map = new FrameBuffer(width, height);
        var distance = new float[width * height];
        var along = new float[width * height];
        var lateral = new float[width * height];
        Array.Fill(distance, float.MaxValue);
        BuildDistanceField(track, width, height, distance, along, lateral);

        var (light, dark) = GroundColors(track.Theme);
        var half = track.HalfWidth;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var d = distance[i];
                byte color;
                if (d <= half - CurbWidth)
                {
                    color = Hash(x, y) % 19 == 0 ? Pal.AsphaltDark : Pal.Asphalt;
                    if (IsStartLine(track, along[i], lateral[i], out var checker))
                    {
                        color = checker;
                    }
                }
                else if (d <= half)
                {
                    color = ((int)(along[i] / 8) & 1) == 0 ? Pal.Red : Pal.White;
                }
                else if (d <= half + BarrierWidth / 2f)
                {
                    color = Pal.LightGray;
                }
                else if (d <= half + BarrierWidth)
                {
                    color = Pal.DarkGray;
                }
                else
                {
                    color = (((x >> 3) + (y >> 3)) & 1) == 0 ? light : dark;
                }

                map.Pixels[i] = color;
            }
        }

        foreach (var feature in track.Features)
        {
            DrawFeature(map, track, feature);
        }

        DrawGrandstand(map, track, distance);
        DrawScenery(map, track, distance, seed);
        return map;
    }

    private static void BuildDistanceField(Track track, int width, int height, float[] distance, float[] along, float[] lateral)
    {
        var points = track.Points;
        var reach = track.HalfWidth + BarrierWidth + 2;
        var cumulative = 0f;
        for (var s = 0; s < points.Count; s++)
        {
            var a = points[s];
            var b = points[(s + 1) % points.Count];
            var ab = b - a;
            var length = ab.Length();
            if (length <= 0)
            {
                continue;
            }

            var tangent = ab / length;
            var normal = new Vector2(-tangent.Y, tangent.X);
            var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, b.X) - reach));
            var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.X, b.X) + reach));
            var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, b.Y) - reach));
            var maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, b.Y) + reach));
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var t = Math.Clamp(Vector2.Dot(p - a, tangent), 0, length);
                    var closest = a + tangent * t;
                    var d = Vector2.Distance(p, closest);
                    var i = y * width + x;
                    if (d < distance[i])
                    {
                        distance[i] = d;
                        along[i] = cumulative + t;
                        lateral[i] = Vector2.Dot(p - closest, normal);
                    }
                }
            }

            cumulative += length;
        }
    }

    private static bool IsStartLine(Track track, float along, float lateral, out byte color)
    {
        var u = along > track.Length / 2 ? along - track.Length : along;
        color = Pal.White;
        if (u < -6 || u >= 6)
        {
            return false;
        }

        var cell = (int)MathF.Floor((u + 6) / 4) + (int)MathF.Floor((lateral + track.HalfWidth) / 4);
        color = (cell & 1) == 0 ? Pal.Black : Pal.White;
        return true;
    }

    private static void DrawFeature(FrameBuffer map, Track track, TrackFeature feature)
    {
        var center = track.FeaturePosition(feature);
        var tangent = track.TangentAt(feature.Distance);
        var normal = new Vector2(-tangent.Y, tangent.X);
        var r = feature.Radius;
        var reach = (int)MathF.Ceiling(r * 1.5f);
        for (var y = (int)center.Y - reach; y <= (int)center.Y + reach; y++)
        {
            for (var x = (int)center.X - reach; x <= (int)center.X + reach; x++)
            {
                var offset = new Vector2(x + 0.5f, y + 0.5f) - center;
                var u = Vector2.Dot(offset, tangent);
                var v = Vector2.Dot(offset, normal);
                byte? color = feature.Kind switch
                {
                    TrackFeatureKind.Zipper => MathF.Abs(u) <= r && MathF.Abs(v) <= r
                        ? (((int)MathF.Floor(u - MathF.Abs(v) * 0.9f) % 8 + 8) % 8 < 3 ? Pal.Yellow : Pal.Orange)
                        : null,
                    TrackFeatureKind.Puddle => PuddleColor(u, v, r),
                    TrackFeatureKind.Oil => OilColor(u, v, r),
                    _ => null,
                };

                if (color is { } c)
                {
                    map.SetPixel(x, y, c);
                }
            }
        }
    }

    private static byte? PuddleColor(float u, float v, float r)
    {
        var e = (u * u) / (r * r * 1.3f) + (v * v) / (r * r * 0.7f);
        if (e > 1)
        {
            return null;
        }

        return e is > 0.35f and < 0.5f && u < 0 ? Pal.SkyBlue : Pal.Water;
    }

    private static byte? OilColor(float u, float v, float r)
    {
        var angle = MathF.Atan2(v, u);
        var edge = r * (0.8f + 0.2f * MathF.Sin(angle * 3) + 0.1f * MathF.Cos(angle * 5));
        var d = MathF.Sqrt(u * u + v * v);
        if (d > edge)
        {
            return null;
        }

        return d < r * 0.3f && u < 0 && v < 0 ? Pal.DarkGray : Pal.Black;
    }

    private static void DrawGrandstand(FrameBuffer map, Track track, float[] distance)
    {
        var tangent = track.TangentAt(0);
        var normal = new Vector2(-tangent.Y, tangent.X);
        var start = track.PointAt(0);
        var random = new Random(7);
        byte[] crowd = [Pal.Red, Pal.Yellow, Pal.Blue, Pal.HotPink, Pal.White, Pal.Skin, Pal.Green, Pal.Pink];
        var near = track.HalfWidth + 10;
        var far = track.HalfWidth + 42;
        for (var u = -60; u <= 60; u++)
        {
            for (var v = near; v <= far; v++)
            {
                var p = start + tangent * u - normal * v;
                var x = (int)p.X;
                var y = (int)p.Y;
                if ((uint)x >= (uint)map.Width || (uint)y >= (uint)map.Height
                    || distance[y * map.Width + x] < track.HalfWidth + 8)
                {
                    continue;
                }

                var row = (int)(v - near);
                byte color = row % 5 == 4 || Math.Abs(u) >= 59 || v >= far - 1
                    ? Pal.Gray
                    : row % 5 == 0 ? Pal.LightGray : crowd[random.Next(crowd.Length)];
                map.SetPixel(x, y, color);
            }
        }
    }

    private static void DrawScenery(FrameBuffer map, Track track, float[] distance, int seed)
    {
        var random = new Random(seed * 31 + track.Id.Length);
        var count = map.Width * map.Height / 5000;
        var clearance = track.HalfWidth + BarrierWidth + 4;
        var placed = new List<(int X, int Y)>();
        for (var attempt = 0; attempt < count * 4 && placed.Count < count; attempt++)
        {
            var variant = random.Next(1000);
            var sprite = Sprites.SceneryFor(track.Theme, variant);
            var x = random.Next(0, map.Width - sprite.Width);
            var y = random.Next(0, map.Height - sprite.Height);
            if (!IsClear(distance, map.Width, x, y, sprite.Width, sprite.Height, clearance)
                || placed.Any(p => Math.Abs(p.X - x) < 14 && Math.Abs(p.Y - y) < 14))
            {
                continue;
            }

            placed.Add((x, y));
            map.Blit(sprite, x, y);
        }
    }

    private static bool IsClear(float[] distance, int width, int x, int y, int w, int h, float clearance)
    {
        for (var sy = y; sy < y + h; sy += Math.Max(1, h / 3))
        {
            for (var sx = x; sx < x + w; sx += Math.Max(1, w / 3))
            {
                if (distance[sy * width + sx] < clearance)
                {
                    return false;
                }
            }
        }

        return distance[(y + h - 1) * width + x + w - 1] >= clearance;
    }

    private static int Hash(int x, int y)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & 0x7fffffff;
        }
    }
}
