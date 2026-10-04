namespace ToniBreidingerRacing.Server;

public sealed class ServerOptions
{
    public int MaxConnections { get; set; } = 128;
    public int MaxRooms { get; set; } = 24;
    public int MessagesPerSecond { get; set; } = 120;
    public int InputsPerSecond { get; set; } = 90;
    public int CommandsPerSecond { get; set; } = 10;
    public double InputTimeoutSeconds { get; set; } = 0.35;
    public double ConnectionIdleSeconds { get; set; } = 120;
    public double LobbyIdleSeconds { get; set; } = 600;
    public double FinishedRetentionSeconds { get; set; } = 120;
    public double RaceTimeoutSeconds { get; set; } = 300;
    public double CountdownSeconds { get; set; } = 3;

    public void Validate()
    {
        if (MaxConnections is < 2 or > 512 || MaxRooms is < 1 or > 64 ||
            MessagesPerSecond is < 1 or > 240 || InputsPerSecond is < 1 or > 120 ||
            CommandsPerSecond is < 1 or > 30)
            throw new InvalidOperationException("Multiplayer capacity and rate limits are out of bounds.");
        foreach (var duration in new[] { InputTimeoutSeconds, ConnectionIdleSeconds, LobbyIdleSeconds,
                     FinishedRetentionSeconds, RaceTimeoutSeconds, CountdownSeconds })
            if (!double.IsFinite(duration) || duration <= 0 || duration > 3600)
                throw new InvalidOperationException("Multiplayer timeouts must be finite, positive and at most one hour.");
    }
}

public sealed class OriginPolicy
{
    private readonly HashSet<string> _allowed;
    private readonly bool _development;

    public OriginPolicy(IEnumerable<string> allowed, bool development)
    {
        _development = development;
        _allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var origin in allowed)
        {
            if (!TryOrigin(origin, out var uri))
                throw new InvalidOperationException("AllowedOrigins must contain exact HTTP(S) origins without paths.");
            _allowed.Add(uri!.GetLeftPart(UriPartial.Authority));
        }
        if (!development && _allowed.Count == 0)
            throw new InvalidOperationException("Configure AllowedOrigins__0 (and subsequent entries) in production.");
    }

    public bool Allows(string origin)
    {
        if (!TryOrigin(origin, out var uri)) return false;
        return _allowed.Contains(uri!.GetLeftPart(UriPartial.Authority)) ||
               (_development && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                 uri.Host is "127.0.0.1" or "[::1]" or "::1"));
    }

    private static bool TryOrigin(string value, out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https" &&
        uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 &&
        uri.Fragment.Length == 0 && !value.Contains('\\') &&
        value.IndexOf('/', value.IndexOf("://", StringComparison.Ordinal) + 3) < 0;
}
