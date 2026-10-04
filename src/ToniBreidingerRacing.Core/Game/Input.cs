using ToniBreidingerRacing.Core.Audio;

namespace ToniBreidingerRacing.Core.Game;

[Flags]
public enum Buttons
{
    None = 0,
    Up = 1 << 0,
    Down = 1 << 1,
    Left = 1 << 2,
    Right = 1 << 3,

    /// <summary>Fire a missile (Space / X).</summary>
    Fire = 1 << 4,

    /// <summary>Enter: confirm / start.</summary>
    Confirm = 1 << 5,

    /// <summary>Escape: pause / back.</summary>
    Back = 1 << 6,

    /// <summary>W: set the A1870 wallet address.</summary>
    Wallet = 1 << 7,

    /// <summary>C: claim the A1870 reward with MetaMask.</summary>
    Claim = 1 << 8,

    /// <summary>R: retry the reward request.</summary>
    Retry = 1 << 9,

    /// <summary>Accelerate (Z or Up arrow).</summary>
    Gas = 1 << 10,

    /// <summary>Brake / reverse (Down arrow).</summary>
    Brake = 1 << 11,
}

/// <summary>Buttons held this frame and the ones newly pressed since the last frame.</summary>
public readonly record struct InputState(Buttons Held, Buttons Pressed)
{
    public bool IsHeld(Buttons button) => (Held & button) != 0;

    public bool WasPressed(Buttons button) => (Pressed & button) != 0;
}

/// <summary>Services the Windows shell provides to the game.</summary>
public interface IGameHost
{
    /// <summary>Shows a modal text prompt and returns the trimmed text, or null if cancelled.</summary>
    string? PromptText(string title, string message, string initialValue, int maxLength);

    /// <summary>Opens a URL in the default browser.</summary>
    void OpenUrl(Uri url);

    void PlaySound(SoundEffect effect);

    void Quit();
}
