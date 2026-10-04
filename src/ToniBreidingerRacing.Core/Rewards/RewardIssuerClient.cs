using System.Net.Http.Json;
using System.Text.Json;

namespace ToniBreidingerRacing.Core.Rewards;

/// <summary>
/// Requests A1870 reward claims from the shared Arcade1870 reward issuer (the Crypto Hockey
/// <c>POST /api/reward-claim</c> endpoint) and turns them into a ready-to-send vault transaction.
/// </summary>
public sealed class RewardIssuerClient
{
    private const string RewardClaimPath = "/api/reward-claim";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly RewardTreasuryOptions _options;
    private readonly TimeProvider _timeProvider;

    public RewardIssuerClient(HttpClient httpClient, RewardTreasuryOptions options, TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RewardTreasuryOptions Options => _options;

    /// <summary>Same resolution rules as Crypto Hockey: a bare host also tries <c>/api/reward-claim</c>.</summary>
    public static bool TryGetCandidateUris(string? configuredUrl, out IReadOnlyList<Uri> candidates, out string error)
    {
        candidates = [];
        error = string.Empty;

        var normalized = (configuredUrl ?? string.Empty).Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "Reward issuer URL is not configured.";
            return false;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var issuerUri)
            && !Uri.TryCreate($"https://{normalized}", UriKind.Absolute, out issuerUri))
        {
            error = $"Reward issuer URL is invalid: {configuredUrl}";
            return false;
        }

        if (issuerUri.Scheme != Uri.UriSchemeHttps && issuerUri.Scheme != Uri.UriSchemeHttp)
        {
            error = $"Reward issuer URL must use HTTP or HTTPS: {configuredUrl}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(issuerUri.Host))
        {
            error = $"Reward issuer URL must include a host: {configuredUrl}";
            return false;
        }

        var list = new List<Uri> { issuerUri };
        var path = issuerUri.AbsolutePath.Trim();
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            var withPath = new UriBuilder(issuerUri) { Path = RewardClaimPath, Query = string.Empty, Fragment = string.Empty }.Uri;
            if (!list.Contains(withPath))
            {
                list.Add(withPath);
            }
        }

        candidates = list;
        return true;
    }

    public async Task<RewardClaimResult> RequestClaimAsync(
        string walletAddress,
        RewardGameProof proof,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return RewardClaimResult.Failure("Arcade1870 rewards are disabled in appsettings.json.");
        }

        if (!RewardClaimEncoder.IsValidAddress(walletAddress))
        {
            return RewardClaimResult.Failure("Enter a valid wallet address (0x followed by 40 hex characters) to receive A1870.");
        }

        if (string.IsNullOrWhiteSpace(proof.GameId))
        {
            return RewardClaimResult.Failure("A completed race id is required.");
        }

        if (!TryGetCandidateUris(_options.RewardIssuerUrl, out var uris, out var configurationError))
        {
            return RewardClaimResult.Failure(configurationError);
        }

        var request = new RewardClaimRequest { Recipient = walletAddress, Game = proof };
        string? lastError = null;

        foreach (var uri in uris)
        {
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(uri, request, JsonOptions, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = await ReadIssuerErrorAsync(response, cancellationToken);
                    continue;
                }

                var payload = await response.Content.ReadFromJsonAsync<RewardClaimPayload>(JsonOptions, cancellationToken);
                if (!RewardClaimEncoder.TryValidate(payload, _options, _timeProvider.GetUtcNow(), out var validationError))
                {
                    return RewardClaimResult.Failure(validationError);
                }

                return new RewardClaimResult
                {
                    IsSuccessful = true,
                    Payload = payload,
                    Transaction = new RewardClaimTransaction
                    {
                        Recipient = walletAddress,
                        VaultAddress = payload!.VaultAddress,
                        ChainId = payload.ChainId,
                        Data = RewardClaimEncoder.EncodeClaimCall(payload),
                        TokenAddress = _options.Arcade1870ContractAddress,
                        TokenSymbol = _options.TokenSymbol,
                        TokenDecimals = _options.RewardTokenDecimals,
                        DisplayAmount = RewardClaimEncoder.FormatTokenAmount(payload.Amount, _options.RewardTokenDecimals),
                        Deadline = payload.Deadline,
                    },
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or NotSupportedException)
            {
                lastError = $"Could not reach the reward issuer: {ex.Message}";
            }
        }

        return RewardClaimResult.Failure(lastError ?? "Reward issuer did not provide a valid claim.");
    }

    private static async Task<string> ReadIssuerErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String)
            {
                return $"Reward issuer: {error.GetString()}";
            }
        }
        catch (JsonException)
        {
        }

        return $"Reward issuer error {(int)response.StatusCode}.";
    }
}
