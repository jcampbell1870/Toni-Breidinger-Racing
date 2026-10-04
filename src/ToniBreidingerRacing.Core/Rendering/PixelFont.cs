namespace ToniBreidingerRacing.Core.Rendering;

/// <summary>Chunky 5×7 arcade font drawn in 6×8 cells (like an 8-bit console's character tiles). Text is upper-cased.</summary>
public static class PixelFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    public const int Advance = 6;
    public const int LineHeight = 9;

    private static readonly Dictionary<char, ulong> Glyphs = BuildGlyphs();

    public static bool Supports(char c) => Glyphs.ContainsKey(char.ToUpperInvariant(c));

    public static int Measure(string text, int scale = 1) =>
        string.IsNullOrEmpty(text) ? 0 : (text.Length * Advance - 1) * Math.Max(1, scale);

    /// <summary>Draws a single line of text. Returns the x coordinate after the last character.</summary>
    public static int Draw(FrameBuffer frame, string text, int x, int y, byte color, int scale = 1, byte? shadow = null)
    {
        scale = Math.Max(1, scale);
        if (shadow is { } shadowColor)
        {
            Draw(frame, text, x + scale, y + scale, shadowColor, scale);
        }

        foreach (var raw in text)
        {
            var glyph = Glyphs.TryGetValue(char.ToUpperInvariant(raw), out var bits) ? bits : Glyphs['?'];
            for (var row = 0; row < GlyphHeight; row++)
            {
                for (var col = 0; col < GlyphWidth; col++)
                {
                    if ((glyph >> (row * GlyphWidth + col) & 1) != 0)
                    {
                        frame.FillRect(x + col * scale, y + row * scale, scale, scale, color);
                    }
                }
            }

            x += Advance * scale;
        }

        return x;
    }

    public static void DrawCentered(FrameBuffer frame, string text, int centerX, int y, byte color, int scale = 1, byte? shadow = null) =>
        Draw(frame, text, centerX - Measure(text, scale) / 2, y, color, scale, shadow);

    /// <summary>Splits text into lines no wider than <paramref name="maxWidth"/> pixels, breaking on spaces.</summary>
    public static IReadOnlyList<string> Wrap(string text, int maxWidth, int scale = 1)
    {
        var maxChars = Math.Max(1, (maxWidth / Math.Max(1, scale) + 1) / Advance);
        var lines = new List<string>();
        foreach (var paragraph in (text ?? string.Empty).Split('\n'))
        {
            var line = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var remaining = word;
                while (remaining.Length > maxChars)
                {
                    if (line.Length > 0)
                    {
                        lines.Add(line);
                        line = string.Empty;
                    }

                    lines.Add(remaining[..maxChars]);
                    remaining = remaining[maxChars..];
                }

                var candidate = line.Length == 0 ? remaining : $"{line} {remaining}";
                if (candidate.Length > maxChars)
                {
                    lines.Add(line);
                    line = remaining;
                }
                else
                {
                    line = candidate;
                }
            }

            if (line.Length > 0 || paragraph.Length == 0)
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    private static Dictionary<char, ulong> BuildGlyphs()
    {
        var source = new Dictionary<char, string[]>
        {
            ['A'] = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
            ['B'] = ["####.", "#...#", "#...#", "####.", "#...#", "#...#", "####."],
            ['C'] = [".###.", "#...#", "#....", "#....", "#....", "#...#", ".###."],
            ['D'] = ["####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####."],
            ['E'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#####"],
            ['F'] = ["#####", "#....", "#....", "####.", "#....", "#....", "#...."],
            ['G'] = [".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####"],
            ['H'] = ["#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
            ['I'] = [".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###."],
            ['J'] = ["..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.."],
            ['K'] = ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
            ['L'] = ["#....", "#....", "#....", "#....", "#....", "#....", "#####"],
            ['M'] = ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
            ['N'] = ["#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#"],
            ['O'] = [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
            ['P'] = ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
            ['Q'] = [".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#"],
            ['R'] = ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
            ['S'] = [".####", "#....", "#....", ".###.", "....#", "....#", "####."],
            ['T'] = ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.."],
            ['U'] = ["#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
            ['V'] = ["#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.."],
            ['W'] = ["#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#."],
            ['X'] = ["#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#"],
            ['Y'] = ["#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.."],
            ['Z'] = ["#####", "....#", "...#.", "..#..", ".#...", "#....", "#####"],
            ['0'] = [".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###."],
            ['1'] = ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
            ['2'] = [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
            ['3'] = ["####.", "....#", "....#", ".###.", "....#", "....#", "####."],
            ['4'] = ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
            ['5'] = ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
            ['6'] = [".###.", "#....", "#....", "####.", "#...#", "#...#", ".###."],
            ['7'] = ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
            ['8'] = [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
            ['9'] = [".###.", "#...#", "#...#", ".####", "....#", "....#", ".###."],
            [' '] = [".....", ".....", ".....", ".....", ".....", ".....", "....."],
            ['.'] = [".....", ".....", ".....", ".....", ".....", ".##..", ".##.."],
            [','] = [".....", ".....", ".....", ".....", ".##..", "..#..", ".#..."],
            [':'] = [".....", ".##..", ".##..", ".....", ".##..", ".##..", "....."],
            [';'] = [".....", ".##..", ".##..", ".....", ".##..", "..#..", ".#..."],
            ['!'] = ["..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.."],
            ['?'] = [".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.."],
            ['-'] = [".....", ".....", ".....", "#####", ".....", ".....", "....."],
            ['+'] = [".....", "..#..", "..#..", "#####", "..#..", "..#..", "....."],
            ['='] = [".....", ".....", "#####", ".....", "#####", ".....", "....."],
            ['/'] = ["....#", "....#", "...#.", "..#..", ".#...", "#....", "#...."],
            ['\''] = ["..#..", "..#..", ".#...", ".....", ".....", ".....", "....."],
            ['"'] = [".#.#.", ".#.#.", ".....", ".....", ".....", ".....", "....."],
            ['('] = ["...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#."],
            [')'] = [".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..."],
            ['%'] = ["##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##"],
            ['#'] = [".#.#.", ".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".#.#."],
            ['<'] = ["...#.", "..#..", ".#...", "#....", ".#...", "..#..", "...#."],
            ['>'] = [".#...", "..#..", "...#.", "....#", "...#.", "..#..", ".#..."],
            ['_'] = [".....", ".....", ".....", ".....", ".....", ".....", "#####"],
            ['*'] = [".....", "#.#.#", ".###.", "#####", ".###.", "#.#.#", "....."],
            ['@'] = [".###.", "#...#", "#.###", "#.#.#", "#.###", "#....", ".###."],
            ['&'] = [".##..", "#..#.", "#.#..", ".#...", "#.#.#", "#..#.", ".##.#"],
            ['$'] = ["..#..", ".####", "#.#..", ".###.", "..#.#", "####.", "..#.."],
            ['['] = [".###.", ".#...", ".#...", ".#...", ".#...", ".#...", ".###."],
            [']'] = [".###.", "...#.", "...#.", "...#.", "...#.", "...#.", ".###."],
            ['©'] = [".###.", "#...#", "#.###", "#.#.#", "#.###", "#...#", ".###."],
        };

        var glyphs = new Dictionary<char, ulong>();
        foreach (var (key, rows) in source)
        {
            ulong bits = 0;
            for (var row = 0; row < GlyphHeight; row++)
            {
                for (var col = 0; col < GlyphWidth; col++)
                {
                    if (rows[row][col] == '#')
                    {
                        bits |= 1UL << (row * GlyphWidth + col);
                    }
                }
            }

            glyphs[key] = bits;
        }

        return glyphs;
    }
}
