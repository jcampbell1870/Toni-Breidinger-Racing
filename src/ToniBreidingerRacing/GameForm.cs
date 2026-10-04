using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Media;
using System.Runtime.InteropServices;
using ToniBreidingerRacing.Core.Audio;
using ToniBreidingerRacing.Core.Configuration;
using ToniBreidingerRacing.Core.Game;
using ToniBreidingerRacing.Core.Rendering;
using ToniBreidingerRacing.Core.Rewards;

namespace ToniBreidingerRacing;

/// <summary>
/// The Windows shell: runs a fixed-step game loop, maps the keyboard to NES-style buttons and
/// scales the 256x240 frame up with crisp nearest-neighbour pixels.
/// </summary>
internal sealed class GameForm : Form, IGameHost
{
    private const double StepSeconds = 1.0 / 60.0;
    private const int MaxStepsPerTick = 5;

    private readonly GameSettings _settings;
    private readonly ToniRacingGame _game;
    private readonly Bitmap _bitmap = new(FrameBuffer.ScreenWidth, FrameBuffer.ScreenHeight, PixelFormat.Format32bppArgb);
    private readonly int[] _argb = new int[FrameBuffer.ScreenWidth * FrameBuffer.ScreenHeight];
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 8 };
    private readonly Stopwatch _clock = new();
    private SoundPlayer? _soundPlayer;
    private Buttons _held;
    private Buttons _previous;
    private double _lastTime;
    private double _accumulator;
    private bool _fullScreen;
    private bool _shutdownComplete;
    private FormWindowState _windowedState = FormWindowState.Normal;

    public GameForm(GameSettings settings, PlayerProfile profile, string profilePath, RewardIssuerClient rewardClient)
    {
        _settings = settings;
        _game = new ToniRacingGame(settings, profile, () => SaveProfile(profile, profilePath), rewardClient, this);

        Text = "Toni Breidinger Racing";
        BackColor = Color.Black;
        ClientSize = new Size(FrameBuffer.ScreenWidth * 3, FrameBuffer.ScreenHeight * 3);
        MinimumSize = SizeFromClientSize(new Size(FrameBuffer.ScreenWidth, FrameBuffer.ScreenHeight));
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

        _timer.Tick += (_, _) => Tick();
        Deactivate += (_, _) => ClearKeys();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_settings.StartFullScreen)
        {
            ToggleFullScreen();
        }

        _clock.Start();
        _timer.Start();
    }

    private void Tick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        _accumulator += Math.Min(now - _lastTime, 0.25);
        _lastTime = now;

        var steps = 0;
        while (_accumulator >= StepSeconds && steps < MaxStepsPerTick && !IsDisposed)
        {
            var input = new InputState(_held, _held & ~_previous);
            _previous = _held;
            _game.Update(StepSeconds, input);
            _accumulator -= StepSeconds;
            steps++;
        }

        if (steps == MaxStepsPerTick)
        {
            _accumulator = 0;
        }

        if (steps > 0 && !IsDisposed)
        {
            _game.Render();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        _game.Frame.ToArgb(_argb);
        var data = _bitmap.LockBits(new Rectangle(0, 0, _bitmap.Width, _bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < _bitmap.Height; y++)
            {
                Marshal.Copy(_argb, y * _bitmap.Width, data.Scan0 + y * data.Stride, _bitmap.Width);
            }
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }

        var g = e.Graphics;
        g.Clear(Color.Black);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingQuality = CompositingQuality.HighSpeed;

        var client = ClientSize;
        var scale = Math.Min(client.Width / (float)_bitmap.Width, client.Height / (float)_bitmap.Height);
        if (scale >= 1)
        {
            scale = MathF.Floor(scale * 4) / 4;
        }

        var width = (int)(_bitmap.Width * scale);
        var height = (int)(_bitmap.Height * scale);
        g.DrawImage(_bitmap, new Rectangle((client.Width - width) / 2, (client.Height - height) / 2, width, height));
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11 || keyData == (Keys.Alt | Keys.Enter))
        {
            ToggleFullScreen();
            return true;
        }

        var button = MapKey(keyData & Keys.KeyCode);
        if (button != Buttons.None && (keyData & (Keys.Control | Keys.Alt)) == 0)
        {
            _held |= button;
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _held &= ~MapKey(e.KeyCode);
        e.Handled = true;
        base.OnKeyUp(e);
    }

    private static Buttons MapKey(Keys key) => key switch
    {
        Keys.Up or Keys.Z => Buttons.Up | Buttons.Gas,
        Keys.Down => Buttons.Down | Buttons.Brake,
        Keys.Left => Buttons.Left,
        Keys.Right => Buttons.Right,
        Keys.Space or Keys.X => Buttons.Fire,
        Keys.Enter => Buttons.Confirm,
        Keys.Escape => Buttons.Back,
        Keys.W => Buttons.Wallet,
        Keys.C => Buttons.Claim,
        Keys.R => Buttons.Retry,
        _ => Buttons.None,
    };

    private void ClearKeys()
    {
        _held = Buttons.None;
        _previous = Buttons.None;
    }

    private void ToggleFullScreen()
    {
        _fullScreen = !_fullScreen;
        if (_fullScreen)
        {
            _windowedState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _windowedState;
            ClientSize = new Size(FrameBuffer.ScreenWidth * 3, FrameBuffer.ScreenHeight * 3);
            CenterToScreen();
        }

        Invalidate();
    }

    private static void SaveProfile(PlayerProfile profile, string path)
    {
        try
        {
            profile.Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Could not save the player profile: {ex.Message}");
        }
    }

    public string? PromptText(string title, string message, string initialValue, int maxLength)
    {
        _timer.Stop();
        try
        {
            using var dialog = new TextPromptDialog(title, message, initialValue, maxLength);
            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Value.Trim() : null;
        }
        finally
        {
            ClearKeys();
            _lastTime = _clock.Elapsed.TotalSeconds;
            _accumulator = 0;
            if (!IsDisposed)
            {
                _timer.Start();
            }
        }
    }

    public void OpenUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Open this page in your browser to claim your reward:\n\n{url}", Text);
        }
    }

    public void PlaySound(SoundEffect effect)
    {
        if (!_settings.SoundEnabled)
        {
            return;
        }

        try
        {
            _soundPlayer?.Stop();
            _soundPlayer?.Dispose();
            _soundPlayer = new SoundPlayer(new MemoryStream(ChipSound.Get(effect), writable: false));
            _soundPlayer.Play();
        }
        catch (InvalidOperationException)
        {
            // No audio device: keep racing silently.
        }
    }

    public void Quit() => BeginInvoke(Close);

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_shutdownComplete || e.Cancel)
        {
            return;
        }

        // Finish shutting down the reward desk (claim page server, pending requests) first.
        e.Cancel = true;
        _timer.Stop();
        Hide();
        try
        {
            await _game.DisposeAsync();
        }
        finally
        {
            _shutdownComplete = true;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _bitmap.Dispose();
            _soundPlayer?.Dispose();
        }

        base.Dispose(disposing);
    }
}
