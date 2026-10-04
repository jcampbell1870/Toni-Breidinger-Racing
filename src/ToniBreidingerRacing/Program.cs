using System.Net.Http.Headers;
using ToniBreidingerRacing.Core.Configuration;
using ToniBreidingerRacing.Core.Rewards;

namespace ToniBreidingerRacing;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var settings = GameSettings.Load(Path.Combine(AppContext.BaseDirectory, GameSettings.FileName));
        var profilePath = PlayerProfile.DefaultPath;
        var profile = PlayerProfile.Load(profilePath);

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ToniBreidingerRacing", "1.0"));
        var rewardClient = new RewardIssuerClient(httpClient, settings.Rewards);

        using var form = new GameForm(settings, profile, profilePath, rewardClient);
        Application.Run(form);
    }
}
