namespace ToniBreidingerRacing.Core.Audio;

public enum SoundEffect
{
    MenuMove,
    MenuSelect,
    CountdownBeep,
    Go,
    Pickup,
    Letter,
    Upgrade,
    Missile,
    Hit,
    Bump,
    Boost,
    Oil,
    Lap,
    FinalLap,
    Win,
    Lose,
    Coin,
}

/// <summary>
/// Tiny 8-bit sound synthesizer: square-wave and noise channels rendered to 8-bit mono WAV files in memory,
/// so the game ships without audio assets and still sounds like a cartridge.
/// </summary>
public static class ChipSound
{
    public const int SampleRate = 22050;

    private readonly record struct Note(double Frequency, double Seconds, double Duty = 0.5, bool Noise = false, double SlideTo = 0, double Volume = 0.6);

    private static readonly Dictionary<SoundEffect, byte[]> Cache = [];
    private static readonly object Gate = new();

    public static byte[] Get(SoundEffect effect)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue(effect, out var wav))
            {
                wav = ToWav(Render(Notes(effect)));
                Cache[effect] = wav;
            }

            return wav;
        }
    }

    private static Note[] Notes(SoundEffect effect) => effect switch
    {
        SoundEffect.MenuMove => [new(880, 0.04, 0.25)],
        SoundEffect.MenuSelect => [new(660, 0.05), new(990, 0.08)],
        SoundEffect.CountdownBeep => [new(440, 0.18, 0.5)],
        SoundEffect.Go => [new(880, 0.35, 0.5)],
        SoundEffect.Pickup => [new(784, 0.05, 0.25), new(1047, 0.07, 0.25)],
        SoundEffect.Letter => [new(659, 0.06), new(784, 0.06), new(988, 0.06), new(1319, 0.12)],
        SoundEffect.Upgrade => [new(523, 0.07), new(659, 0.07), new(784, 0.07), new(1047, 0.07), new(1319, 0.16)],
        SoundEffect.Missile => [new(1200, 0.18, Noise: true, SlideTo: 300, Volume: 0.45)],
        SoundEffect.Hit => [new(400, 0.3, Noise: true, SlideTo: 60, Volume: 0.7)],
        SoundEffect.Bump => [new(110, 0.08, 0.5, Volume: 0.5)],
        SoundEffect.Boost => [new(300, 0.25, 0.125, SlideTo: 1200, Volume: 0.4)],
        SoundEffect.Oil => [new(700, 0.4, 0.25, SlideTo: 150, Volume: 0.4)],
        SoundEffect.Lap => [new(784, 0.08), new(1047, 0.14)],
        SoundEffect.FinalLap => [new(784, 0.08), new(784, 0.08), new(1047, 0.08), new(1319, 0.2)],
        SoundEffect.Win => [new(523, 0.12), new(659, 0.12), new(784, 0.12), new(1047, 0.24), new(784, 0.12), new(1047, 0.4)],
        SoundEffect.Lose => [new(392, 0.18), new(330, 0.18), new(262, 0.18), new(196, 0.4)],
        SoundEffect.Coin => [new(988, 0.07, 0.25), new(1319, 0.25, 0.25)],
        _ => [new(440, 0.05)],
    };

    private static byte[] Render(IReadOnlyList<Note> notes)
    {
        var total = notes.Sum(n => (int)(n.Seconds * SampleRate));
        var samples = new byte[total];
        var index = 0;
        var phase = 0.0;
        var lfsr = (ushort)1;
        var noiseValue = 1;
        foreach (var note in notes)
        {
            var count = (int)(note.Seconds * SampleRate);
            for (var i = 0; i < count; i++)
            {
                var progress = count > 1 ? i / (double)(count - 1) : 0;
                var frequency = note.SlideTo > 0 ? note.Frequency + (note.SlideTo - note.Frequency) * progress : note.Frequency;
                phase += frequency / SampleRate;
                int level;
                if (note.Noise)
                {
                    if (phase >= 1)
                    {
                        phase -= Math.Floor(phase);
                        var bit = (lfsr ^ (lfsr >> 1)) & 1;
                        lfsr = (ushort)((lfsr >> 1) | (bit << 14));
                        noiseValue = (lfsr & 1) == 0 ? 1 : -1;
                    }

                    level = noiseValue;
                }
                else
                {
                    phase -= Math.Floor(phase);
                    level = phase < note.Duty ? 1 : -1;
                }

                // Short attack and linear release envelope to avoid clicks.
                var envelope = Math.Min(1.0, i / 40.0) * (1.0 - 0.6 * progress);
                samples[index++] = (byte)Math.Clamp(128 + level * 127 * note.Volume * envelope, 0, 255);
            }
        }

        return samples;
    }

    private static byte[] ToWav(byte[] samples)
    {
        using var stream = new MemoryStream(44 + samples.Length);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate); // byte rate (8-bit mono)
        writer.Write((short)1); // block align
        writer.Write((short)8); // bits per sample
        writer.Write("data"u8);
        writer.Write(samples.Length);
        writer.Write(samples);
        writer.Flush();
        return stream.ToArray();
    }
}
