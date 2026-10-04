using ToniBreidingerRacing.Core.Audio;
using ToniBreidingerRacing.Core.Game;
using ToniBreidingerRacing.Core.Racing;

namespace ToniBreidingerRacing.Tests;

internal static class TestHelpers
{
    public const float Step = 1f / 60f;

    /// <summary>Lets the AI autopilot drive Toni until the race is over (or the time limit hits).</summary>
    public static void AutopilotToFinish(Race race, double maxSeconds = 600, bool fire = false)
    {
        var pilot = new AiDriver(7);
        var t = 0.0;
        while (race.State != RaceState.Finished && t < maxSeconds)
        {
            var input = pilot.Drive(race.Player, race.Track, race.Elapsed, Step);
            race.Update(Step, fire ? input with { Fire = true } : input);
            t += Step;
        }
    }

    public static Buttons ToButtons(CarInput input)
    {
        var buttons = Buttons.None;
        if (input.Accelerate)
        {
            buttons |= Buttons.Gas | Buttons.Up;
        }

        if (input.Brake)
        {
            buttons |= Buttons.Brake | Buttons.Down;
        }

        if (input.Steer > 0.2f)
        {
            buttons |= Buttons.Right;
        }
        else if (input.Steer < -0.2f)
        {
            buttons |= Buttons.Left;
        }

        return buttons;
    }
}

internal sealed class FakeHost : IGameHost
{
    public string? NextPromptAnswer { get; set; }

    public List<Uri> OpenedUrls { get; } = [];

    public List<SoundEffect> Sounds { get; } = [];

    public int QuitCount { get; private set; }

    public string? PromptText(string title, string message, string initialValue, int maxLength) => NextPromptAnswer;

    public void OpenUrl(Uri url) => OpenedUrls.Add(url);

    public void PlaySound(SoundEffect effect) => Sounds.Add(effect);

    public void Quit() => QuitCount++;
}
