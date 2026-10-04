namespace ToniBreidingerRacing.Core.Rendering;

/// <summary>
/// The game's 8-bit palette, taken from the NES master palette. The frame buffer stores one palette
/// index per pixel, just like the console's picture processing unit.
/// </summary>
public static class Pal
{
    public const byte Transparent = 255;

    public const byte Black = 0;
    public const byte White = 1;
    public const byte Gray = 2;
    public const byte LightGray = 3;
    public const byte DarkGray = 4;
    public const byte Asphalt = 5;
    public const byte AsphaltDark = 6;
    public const byte Red = 7;
    public const byte Pink = 8;
    public const byte HotPink = 9;
    public const byte Blue = 10;
    public const byte SkyBlue = 11;
    public const byte Navy = 12;
    public const byte Yellow = 13;
    public const byte Orange = 14;
    public const byte Green = 15;
    public const byte DarkGreen = 16;
    public const byte Grass = 17;
    public const byte GrassDark = 18;
    public const byte Lime = 19;
    public const byte Sand = 20;
    public const byte SandDark = 21;
    public const byte Brown = 22;
    public const byte DarkBrown = 23;
    public const byte Skin = 24;
    public const byte SkinShade = 25;
    public const byte Ice = 26;
    public const byte NightGrass = 27;
    public const byte NightGrassDark = 28;
    public const byte Purple = 29;
    public const byte Teal = 30;
    public const byte Cream = 31;
    public const byte Hair = 32;
    public const byte HairLight = 33;
    public const byte Water = 34;
    public const byte Dirt = 35;
    public const byte DirtDark = 36;
    public const byte Maroon = 37;
    public const byte LightPink = 38;
    public const byte Snow = 39;

    /// <summary>0xRRGGBB colour for each palette index.</summary>
    public static ReadOnlySpan<int> Colors =>
    [
        0x000000, // Black
        0xFCFCFC, // White
        0x7C7C7C, // Gray
        0xBCBCBC, // LightGray
        0x545454, // DarkGray (NES 0x2D-ish)
        0x747474, // Asphalt
        0x606060, // AsphaltDark
        0xD82800, // Red
        0xF878F8, // Pink
        0xE40058, // HotPink
        0x0058F8, // Blue
        0x3CBCFC, // SkyBlue
        0x0000BC, // Navy
        0xF8B800, // Yellow
        0xE45C10, // Orange
        0x00A800, // Green
        0x005800, // DarkGreen
        0x00B800, // Grass
        0x009400, // GrassDark
        0xB8F818, // Lime
        0xF8D878, // Sand
        0xE4B060, // SandDark
        0xAC7C00, // Brown
        0x503000, // DarkBrown
        0xFCC8A8, // Skin
        0xE49870, // SkinShade
        0xA4E4FC, // Ice
        0x004058, // NightGrass
        0x002838, // NightGrassDark
        0x6844FC, // Purple
        0x008888, // Teal
        0xFCE0A8, // Cream
        0x3C1C00, // Hair
        0x7C4010, // HairLight
        0x0078F8, // Water
        0xC86C30, // Dirt
        0x985020, // DirtDark
        0x881400, // Maroon
        0xF8B8F8, // LightPink
        0xE8F0FC, // Snow
    ];

    public static int Count => Colors.Length;

    /// <summary>Opaque 0xAARRGGBB value for a palette index (transparent maps to black).</summary>
    public static int ToArgb(byte index) =>
        index < Colors.Length ? unchecked((int)0xFF000000) | Colors[index] : unchecked((int)0xFF000000);
}
