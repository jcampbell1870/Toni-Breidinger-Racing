using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ToniBreidingerRacing.Core.Configuration;
using ToniBreidingerRacing.Core.Rewards;

namespace ToniBreidingerRacing.Tests;

public class RewardTests
{
    internal const string Wallet = "0x1111111111111111111111111111111111111111";
    internal static readonly string Signature = "0x" + new string('a', 128) + "1b";
    internal static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    internal static RewardClaimPayload ValidPayload() => new()
    {
        Amount = "10000000000000000000",
        Nonce = "7",
        Deadline = Now.ToUnixTimeSeconds() + 3600,
        Signature = Signature,
        VaultAddress = RewardTreasuryOptions.CryptoHockeyRewardVaultAddress,
        ChainId = 1,
    };

    [Fact]
    public void Defaults_UseTheCryptoHockeyArcade1870Treasury()
    {
        var options = new RewardTreasuryOptions();
        Assert.Equal("0x8eddD4edea39c5B5f77662453600F53A202EE47C", options.Arcade1870ContractAddress);
        Assert.Equal("0x1e4f6e4a382adbdb662733a19ae773d3ab8f497d", options.RewardVaultAddress);
        Assert.Equal("https://www.cryptohockey.org/api/reward-claim", options.RewardIssuerUrl);
        Assert.Equal("A1870", options.TokenSymbol);
    }

    [Fact]
    public void ShippedAppSettings_PointAtTheSameTreasury()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "ToniBreidingerRacing", GameSettings.FileName);
        var settings = GameSettings.Load(path);
        var defaults = new RewardTreasuryOptions();
        Assert.True(settings.Rewards.Enabled);
        Assert.Equal(defaults.Arcade1870ContractAddress, settings.Rewards.Arcade1870ContractAddress);
        Assert.Equal(defaults.RewardVaultAddress, settings.Rewards.RewardVaultAddress);
        Assert.Equal(defaults.RewardIssuerUrl, settings.Rewards.RewardIssuerUrl);
        Assert.Equal("10", settings.Rewards.RewardAmount);
    }

    [Theory]
    [InlineData(45, true)]
    [InlineData(30, true)]
    [InlineData(12, false)]
    public void PlayReward_EveryLongEnoughRaceIsEligibleWhateverThePlace(double seconds, bool expected) =>
        Assert.Equal(expected, PlayReward.IsEligible(seconds, new RewardTreasuryOptions()));

    [Fact]
    public void PlayReward_DisabledOptionsAreNeverEligible() =>
        Assert.False(PlayReward.IsEligible(300, new RewardTreasuryOptions { Enabled = false }));

    [Fact]
    public void PlayReward_ProofCountsAsPlayedEvenInLastPlace()
    {
        var proof = PlayReward.CreateProof("speedway", 2, 4, 4, Now.UtcDateTime);
        var other = PlayReward.CreateProof("speedway", 2, 1, 4, Now.UtcDateTime);
        Assert.StartsWith("toni-speedway-", proof.GameId);
        Assert.NotEqual(proof.GameId, other.GameId);
        Assert.Equal(PlayReward.Mode, proof.Mode);
        Assert.True(proof.PlayerWon);
        Assert.Equal(1, proof.PlayerScore);
        Assert.Equal(4, other.PlayerScore);
        Assert.Equal("Round 2", proof.DifficultyLevel);
    }

    [Fact]
    public void Encoder_ValidatesPayloads()
    {
        var options = new RewardTreasuryOptions();
        Assert.True(RewardClaimEncoder.TryValidate(ValidPayload(), options, Now, out _));

        var wrongVault = ValidPayload();
        wrongVault.VaultAddress = Wallet;
        Assert.False(RewardClaimEncoder.TryValidate(wrongVault, options, Now, out var error));
        Assert.Contains("treasury", error);

        var expired = ValidPayload();
        expired.Deadline = Now.ToUnixTimeSeconds() - 1;
        Assert.False(RewardClaimEncoder.TryValidate(expired, options, Now, out _));

        var badChain = ValidPayload();
        badChain.ChainId = 5;
        Assert.False(RewardClaimEncoder.TryValidate(badChain, options, Now, out _));

        var badSignature = ValidPayload();
        badSignature.Signature = "0x1234";
        Assert.False(RewardClaimEncoder.TryValidate(badSignature, options, Now, out _));
    }

    [Fact]
    public void Encoder_EncodesClaimCallAndFormatsAmounts()
    {
        var data = RewardClaimEncoder.EncodeClaimCall(ValidPayload());
        Assert.StartsWith("0x", data);
        // selector + 4 head words + bytes length + 3 words of signature data.
        Assert.Equal(2 + 8 + 64 * 8, data.Length);
        Assert.Equal("10", RewardClaimEncoder.FormatTokenAmount("10000000000000000000", 18));
        Assert.Equal("2.5", RewardClaimEncoder.FormatTokenAmount("2500000000000000000", 18));
    }

    [Theory]
    [InlineData("0x1111111111111111111111111111111111111111", true)]
    [InlineData("0x111111111111111111111111111111111111111", false)]
    [InlineData("1111111111111111111111111111111111111111aa", false)]
    [InlineData("0xZZ11111111111111111111111111111111111111", false)]
    [InlineData(null, false)]
    public void Encoder_ValidatesAddresses(string? address, bool expected) =>
        Assert.Equal(expected, RewardClaimEncoder.IsValidAddress(address));

    [Fact]
    public async Task IssuerClient_PostsProofAndBuildsTransaction()
    {
        var handler = new StubHandler(HttpStatusCode.OK, JsonSerializer.Serialize(ValidPayload(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var client = new RewardIssuerClient(new HttpClient(handler), new RewardTreasuryOptions(), new FakeTimeProvider(Now));
        var proof = PlayReward.CreateProof("speedway", 1, 3, 4, Now.UtcDateTime);

        var result = await client.RequestClaimAsync(Wallet, proof);

        Assert.True(result.IsSuccessful, result.ErrorMessage);
        Assert.Equal(new Uri(RewardTreasuryOptions.CryptoHockeyRewardIssuerUrl), handler.LastUri);
        using var body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(Wallet, body.RootElement.GetProperty("recipient").GetString());
        Assert.Equal(proof.GameId, body.RootElement.GetProperty("game").GetProperty("gameId").GetString());
        Assert.Equal("toni-racing", body.RootElement.GetProperty("game").GetProperty("mode").GetString());
        var tx = result.Transaction!;
        Assert.Equal("10", tx.DisplayAmount);
        Assert.Equal(RewardTreasuryOptions.CryptoHockeyArcade1870ContractAddress, tx.TokenAddress);
        Assert.Equal(RewardTreasuryOptions.CryptoHockeyRewardVaultAddress, tx.VaultAddress);
    }

    [Fact]
    public async Task IssuerClient_ReportsIssuerErrors()
    {
        var handler = new StubHandler(HttpStatusCode.TooManyRequests, "{\"error\":\"Daily reward limit reached\"}");
        var client = new RewardIssuerClient(new HttpClient(handler), new RewardTreasuryOptions(), new FakeTimeProvider(Now));

        var result = await client.RequestClaimAsync(Wallet, PlayReward.CreateProof("t", 1, 1, 4, Now.UtcDateTime));

        Assert.False(result.IsSuccessful);
        Assert.Equal("Reward issuer: Daily reward limit reached", result.ErrorMessage);
    }

    [Fact]
    public async Task IssuerClient_RejectsBadWalletWithoutCallingIssuer()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new RewardIssuerClient(new HttpClient(handler), new RewardTreasuryOptions(), new FakeTimeProvider(Now));

        var result = await client.RequestClaimAsync("not-a-wallet", PlayReward.CreateProof("t", 1, 1, 4, Now.UtcDateTime));

        Assert.False(result.IsSuccessful);
        Assert.Null(handler.LastUri);
    }

    [Fact]
    public void ClaimPage_EncodesTrackNameAndTargetsVault()
    {
        var tx = new RewardClaimTransaction
        {
            Recipient = Wallet,
            VaultAddress = RewardTreasuryOptions.CryptoHockeyRewardVaultAddress,
            ChainId = 1,
            Data = "0xabcdef",
            TokenAddress = RewardTreasuryOptions.CryptoHockeyArcade1870ContractAddress,
            TokenSymbol = "A1870",
            TokenDecimals = 18,
            DisplayAmount = "10",
            Deadline = Now.ToUnixTimeSeconds() + 3600,
        };

        var html = ClaimPageBuilder.Build(tx, "<script>alert(1)</script>");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains(RewardTreasuryOptions.CryptoHockeyRewardVaultAddress, html);
        Assert.Contains("Toni Breidinger Racing", html);
    }

    [Fact]
    public async Task RewardDesk_FlowsFromRequestToReadyAndPublishesClaimPage()
    {
        var handler = new StubHandler(HttpStatusCode.OK, JsonSerializer.Serialize(ValidPayload(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await using var desk = new RewardDesk(new RewardIssuerClient(new HttpClient(handler), new RewardTreasuryOptions(), new FakeTimeProvider(Now)));
        var ready = 0;
        desk.ClaimReady += _ => ready++;

        desk.Begin(PlayReward.CreateProof("t", 1, 2, 4, Now.UtcDateTime), "Test Track", string.Empty);
        Assert.Equal(RewardStatus.NeedsWallet, desk.Status);

        desk.Request(Wallet);
        Assert.Equal(RewardStatus.Requesting, desk.Status);
        await WaitFor(() => { desk.Poll(); return desk.Status != RewardStatus.Requesting; });

        Assert.Equal(RewardStatus.Ready, desk.Status);
        Assert.Equal(1, ready);
        var url = desk.PublishClaimPage();
        Assert.NotNull(url);
        Assert.True(url!.IsLoopback);
        Assert.Equal(RewardStatus.ClaimPageOpened, desk.Status);

        using var http = new HttpClient();
        var page = await http.GetStringAsync(url);
        Assert.Contains("Test Track", page);
    }

    [Fact]
    public void RewardDesk_IneligibleRaceRequestsNothing()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var desk = new RewardDesk(new RewardIssuerClient(new HttpClient(handler), new RewardTreasuryOptions()));
        desk.Begin(null, "Test", Wallet);
        Assert.Equal(RewardStatus.NotEligible, desk.Status);
        Assert.Null(handler.LastUri);
    }

    internal static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ToniBreidingerRacing.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

internal sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public Uri? LastUri { get; private set; }

    public string? LastBody { get; private set; }

    public int Calls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastUri = request.RequestUri;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
