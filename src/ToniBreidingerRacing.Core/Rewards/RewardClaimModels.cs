using System.Text.Json.Serialization;

namespace ToniBreidingerRacing.Core.Rewards;

/// <summary>Proof of a completed race, sent to the reward issuer (same shape as Crypto Hockey's game proof).</summary>
public sealed class RewardGameProof
{
    [JsonPropertyName("gameId")]
    public string GameId { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "toni-racing";

    [JsonPropertyName("playerScore")]
    public int PlayerScore { get; set; }

    [JsonPropertyName("opponentScore")]
    public int OpponentScore { get; set; }

    [JsonPropertyName("difficultyLevel")]
    public string DifficultyLevel { get; set; } = "Pro-Am";

    [JsonPropertyName("completedAt")]
    public DateTime CompletedAt { get; set; }

    /// <summary>Players earn A1870 just for playing, so every finished race counts as a win.</summary>
    [JsonPropertyName("playerWon")]
    public bool PlayerWon { get; set; } = true;
}

public sealed class RewardClaimRequest
{
    [JsonPropertyName("recipient")]
    public string Recipient { get; set; } = string.Empty;

    [JsonPropertyName("game")]
    public RewardGameProof Game { get; set; } = new();
}

/// <summary>EIP-712 signed claim returned by the reward issuer.</summary>
public sealed class RewardClaimPayload
{
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    [JsonPropertyName("deadline")]
    public long Deadline { get; set; }

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    [JsonPropertyName("vaultAddress")]
    public string VaultAddress { get; set; } = string.Empty;

    [JsonPropertyName("chainId")]
    public int ChainId { get; set; }
}

public sealed class RewardClaimTransaction
{
    public required string Recipient { get; init; }

    public required string VaultAddress { get; init; }

    public required int ChainId { get; init; }

    /// <summary>ABI-encoded call to <c>claim(uint256 amount, uint256 nonce, uint256 deadline, bytes signature)</c>.</summary>
    public required string Data { get; init; }

    public required string TokenAddress { get; init; }

    public required string TokenSymbol { get; init; }

    public required int TokenDecimals { get; init; }

    public required string DisplayAmount { get; init; }

    public required long Deadline { get; init; }
}

public sealed class RewardClaimResult
{
    public bool IsSuccessful { get; init; }

    public string? ErrorMessage { get; init; }

    public RewardClaimPayload? Payload { get; init; }

    public RewardClaimTransaction? Transaction { get; init; }

    public static RewardClaimResult Failure(string message) => new() { IsSuccessful = false, ErrorMessage = message };
}
