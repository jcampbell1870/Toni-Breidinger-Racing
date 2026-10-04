using System.Text.Json;
using ToniBreidingerRacing.Core.Configuration;

namespace ToniBreidingerRacing.Tests;

public class DisplaySettingsTests
{
    [Fact]
    public void DisplayOptions_HasDefaultValues()
    {
        var display = new GameSettings.DisplayOptions();
        Assert.Equal(3, display.InitialScale);
        Assert.True(display.PixelPerfect);
        Assert.Equal("NearestNeighbor", display.InterpolationMode);
    }

    [Fact]
    public void DisplayOptions_CanBeModified()
    {
        var display = new GameSettings.DisplayOptions
        {
            InitialScale = 4,
            PixelPerfect = false,
            InterpolationMode = "Bilinear",
        };

        Assert.Equal(4, display.InitialScale);
        Assert.False(display.PixelPerfect);
        Assert.Equal("Bilinear", display.InterpolationMode);
    }

    [Fact]
    public void GameSettings_DeserializesDisplayOptions()
    {
        var json = """
        {
          "Display": {
            "InitialScale": 4,
            "PixelPerfect": false,
            "InterpolationMode": "Bilinear"
          }
        }
        """;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var settings = JsonSerializer.Deserialize<GameSettings>(json, options);
        Assert.NotNull(settings);
        Assert.NotNull(settings.Display);
        Assert.Equal(4, settings.Display.InitialScale);
        Assert.False(settings.Display.PixelPerfect);
        Assert.Equal("Bilinear", settings.Display.InterpolationMode);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void DisplayOptions_AcceptsValidScales(int scale)
    {
        var display = new GameSettings.DisplayOptions { InitialScale = scale };
        Assert.Equal(scale, display.InitialScale);
    }

    [Theory]
    [InlineData("NearestNeighbor")]
    [InlineData("Bilinear")]
    public void DisplayOptions_AcceptsValidInterpolationModes(string mode)
    {
        var display = new GameSettings.DisplayOptions { InterpolationMode = mode };
        Assert.Equal(mode, display.InterpolationMode);
    }

    [Fact]
    public void GameSettings_HandlesMissingDisplaySection()
    {
        var json = "{}";

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        var settings = JsonSerializer.Deserialize<GameSettings>(json, options);
        Assert.NotNull(settings);
        Assert.Null(settings.Display);
    }
}
