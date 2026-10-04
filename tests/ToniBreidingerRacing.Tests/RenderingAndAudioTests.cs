using ToniBreidingerRacing.Core.Audio;
using ToniBreidingerRacing.Core.Racing;
using ToniBreidingerRacing.Core.Rendering;

namespace ToniBreidingerRacing.Tests;

public class RenderingAndAudioTests
{
    [Fact]
    public void FrameBuffer_ClipsDrawingAndConvertsToArgb()
    {
        var frame = new FrameBuffer(8, 8);
        frame.Clear(Pal.Black);
        frame.FillRect(-4, -4, 6, 6, Pal.White);
        frame.SetPixel(100, 100, Pal.Red);
        Assert.Equal(Pal.White, frame[1, 1]);
        Assert.Equal(Pal.Black, frame[2, 2]);

        var argb = new int[64];
        frame.ToArgb(argb);
        Assert.Equal(Pal.ToArgb(Pal.White), argb[9]);
        Assert.Equal(255, (argb[0] >> 24) & 0xFF);
    }

    [Fact]
    public void Blit_SkipsTransparentPixelsAndSupportsSolidColor()
    {
        var sprite = Sprite.FromRows(["x.", ".x"], new Dictionary<char, byte> { ['x'] = Pal.Red });
        var frame = new FrameBuffer(4, 4);
        frame.Clear(Pal.Black);
        frame.Blit(sprite, 0, 0, 2);
        Assert.Equal(Pal.Red, frame[1, 1]);
        Assert.Equal(Pal.Black, frame[2, 0]);
        frame.Blit(sprite, 0, 0, solidColor: Pal.White);
        Assert.Equal(Pal.White, frame[0, 0]);
        Assert.Equal(Pal.Red, frame[1, 0]);
    }

    [Fact]
    public void PixelFont_MeasuresAndWraps()
    {
        Assert.True(PixelFont.Measure("AB") > PixelFont.Measure("A") * 2);
        Assert.Equal(PixelFont.Measure("A") * 2, PixelFont.Measure("A", 2));
        Assert.Equal(0, PixelFont.Measure(string.Empty));
        var lines = PixelFont.Wrap("EVERY RACE EARNS TEN ARCADE1870 TOKENS FOR PLAYING", 80);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(PixelFont.Measure(l) <= 80, l));
        Assert.True(PixelFont.Supports('a'));
    }

    [Fact]
    public void CarSprites_AreCachedAndCoverAllHeadings()
    {
        var first = Sprites.Car(Rivals.ToniLivery, 0);
        Assert.Same(first, Sprites.Car(Rivals.ToniLivery, MathF.Tau));
        Assert.NotSame(first, Sprites.Car(Rivals.ToniLivery, MathF.PI));
        Assert.Contains(first.Pixels, p => p == Rivals.ToniLivery.Body);
    }

    [Fact]
    public void TrackRenderer_DrawsRoadUnderCenterLine()
    {
        var track = TrackLibrary.All[0];
        var map = TrackRenderer.Render(track);
        Assert.Equal(track.WorldWidth, map.Width);
        Assert.Equal(track.WorldHeight, map.Height);
        var (light, dark) = TrackRenderer.GroundColors(track.Theme);
        var p = track.PointAt(track.Length * 0.4f);
        Assert.NotEqual(light, map[(int)p.X, (int)p.Y]);
        Assert.NotEqual(dark, map[(int)p.X, (int)p.Y]);
    }

    [Theory]
    [MemberData(nameof(AllEffects))]
    public void ChipSound_ProducesValidWav(SoundEffect effect)
    {
        var wav = ChipSound.Get(effect);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(ChipSound.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.True(wav.Length > 100);
        Assert.Same(wav, ChipSound.Get(effect));
    }

    public static TheoryData<SoundEffect> AllEffects() => new(Enum.GetValues<SoundEffect>());
}
