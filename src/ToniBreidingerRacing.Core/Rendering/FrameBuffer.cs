namespace ToniBreidingerRacing.Core.Rendering;

/// <summary>A paletted sprite. Pixels equal to <see cref="Pal.Transparent"/> are not drawn.</summary>
public sealed class Sprite
{
    public Sprite(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height)
        {
            throw new ArgumentException("Pixel data does not match sprite size.", nameof(pixels));
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte this[int x, int y] => Pixels[y * Width + x];

    /// <summary>
    /// Builds a sprite from rows of characters. Each character is looked up in <paramref name="colors"/>;
    /// unknown characters (such as '.') are transparent. Short rows are padded with transparency.
    /// </summary>
    public static Sprite FromRows(IReadOnlyList<string> rows, IReadOnlyDictionary<char, byte> colors)
    {
        var width = rows.Max(r => r.Length);
        var pixels = new byte[width * rows.Count];
        Array.Fill(pixels, Pal.Transparent);
        for (var y = 0; y < rows.Count; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (colors.TryGetValue(rows[y][x], out var color))
                {
                    pixels[y * width + x] = color;
                }
            }
        }

        return new Sprite(width, rows.Count, pixels);
    }

    /// <summary>Rows mirrored left-to-right and appended, for symmetric pixel art drawn as a left half.</summary>
    public static IReadOnlyList<string> Mirror(IReadOnlyList<string> leftHalves) =>
        leftHalves.Select(r => r + new string(r.Reverse().ToArray())).ToList();
}

/// <summary>256×240 paletted frame buffer, the resolution of the NES picture.</summary>
public sealed class FrameBuffer
{
    public const int ScreenWidth = 256;
    public const int ScreenHeight = 240;

    public FrameBuffer(int width = ScreenWidth, int height = ScreenHeight)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte this[int x, int y] => Pixels[y * Width + x];

    public void Clear(byte color) => Array.Fill(Pixels, color);

    public void SetPixel(int x, int y, byte color)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height && color != Pal.Transparent)
        {
            Pixels[y * Width + x] = color;
        }
    }

    public void FillRect(int x, int y, int width, int height, byte color)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(Width, x + width);
        var y1 = Math.Min(Height, y + height);
        if (x0 >= x1 || color == Pal.Transparent)
        {
            return;
        }

        for (var row = y0; row < y1; row++)
        {
            Array.Fill(Pixels, color, row * Width + x0, x1 - x0);
        }
    }

    public void DrawRect(int x, int y, int width, int height, byte color)
    {
        FillRect(x, y, width, 1, color);
        FillRect(x, y + height - 1, width, 1, color);
        FillRect(x, y, 1, height, color);
        FillRect(x + width - 1, y, 1, height, color);
    }

    public void FillCircle(int cx, int cy, int radius, byte color)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            var span = (int)MathF.Sqrt(radius * radius - dy * dy + radius * 0.8f);
            FillRect(cx - span, cy + dy, span * 2 + 1, 1, color);
        }
    }

    public void DrawLine(int x0, int y0, int x1, int y1, byte color)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;
        while (true)
        {
            SetPixel(x0, y0, color);
            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    /// <summary>Draws a sprite (optionally scaled by an integer factor), optionally replacing every opaque pixel with one colour.</summary>
    public void Blit(Sprite sprite, int x, int y, int scale = 1, byte? solidColor = null)
    {
        scale = Math.Max(1, scale);
        for (var sy = 0; sy < sprite.Height; sy++)
        {
            for (var sx = 0; sx < sprite.Width; sx++)
            {
                var color = sprite[sx, sy];
                if (color == Pal.Transparent)
                {
                    continue;
                }

                FillRect(x + sx * scale, y + sy * scale, scale, scale, solidColor ?? color);
            }
        }
    }

    /// <summary>Copies a window of a larger paletted map (the pre-rendered track) to the screen.</summary>
    public void CopyFrom(byte[] source, int sourceWidth, int sourceHeight, int sourceX, int sourceY, byte outside)
    {
        for (var y = 0; y < Height; y++)
        {
            var my = sourceY + y;
            for (var x = 0; x < Width; x++)
            {
                var mx = sourceX + x;
                Pixels[y * Width + x] = (uint)mx < (uint)sourceWidth && (uint)my < (uint)sourceHeight
                    ? source[my * sourceWidth + mx]
                    : outside;
            }
        }
    }

    /// <summary>Converts the frame to 32-bit ARGB pixels for display.</summary>
    public void ToArgb(Span<int> destination)
    {
        if (destination.Length < Pixels.Length)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        Span<int> lookup = stackalloc int[256];
        for (var i = 0; i < 256; i++)
        {
            lookup[i] = Pal.ToArgb((byte)i);
        }

        for (var i = 0; i < Pixels.Length; i++)
        {
            destination[i] = lookup[Pixels[i]];
        }
    }
}
