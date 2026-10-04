using ToniBreidingerRacing.Server;

var builder = WebApplication.CreateBuilder(args);
var options = new ServerOptions();
builder.Configuration.GetSection("Multiplayer").Bind(options);
options.Validate();
var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
var originPolicy = new OriginPolicy(origins, builder.Environment.IsDevelopment());
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<MultiplayerHub>();
builder.Services.AddHostedService<SimulationService>();

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/tracks", () => Results.Json(Protocol.Tracks));
app.Map("/ws", async (HttpContext context, MultiplayerHub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    if (!originPolicy.Allows(context.Request.Headers.Origin.ToString()))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    // Reserve capacity before the upgrade, so rejected sockets cannot exceed the connection cap.
    var peer = new SocketPeer();
    if (!hub.TryConnect(peer, out var playerId))
    {
        peer.Dispose();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }
    try
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await peer.RunAsync(socket, text => hub.Receive(playerId, text), context.RequestAborted);
    }
    finally
    {
        hub.Disconnect(playerId);
        peer.Dispose();
    }
});
app.Run();

public partial class Program;
