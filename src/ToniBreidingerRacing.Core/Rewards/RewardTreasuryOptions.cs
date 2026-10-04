namespace ToniBreidingerRacing.Core.Rewards;

/// <summary>
/// Arcade1870 (A1870) reward treasury settings. The defaults point at the exact same treasury used by
/// Crypto Hockey (https://github.com/jcampbell1870/Crypto-Hockey): the A1870 token contract, the
/// <c>Arcade1870RewardVault</c> and the Crypto Hockey reward issuer that signs EIP-712 claims for it.
/// </summary>
public sealed class RewardTreasuryOptions
{
    public const string CryptoHockeyArcade1870ContractAddress = "0x8eddD4edea39c5B5f77662453600F53A202EE47C";
    public const string CryptoHockeyRewardVaultAddress = "0x1e4f6e4a382adbdb662733a19ae773d3ab8f497d";
    public const string CryptoHockeyRewardIssuerUrl = "https://www.cryptohockey.org/api/reward-claim";

    public bool Enabled { get; set; } = true;

    public string Arcade1870ContractAddress { get; set; } = CryptoHockeyArcade1870ContractAddress;

    public string RewardVaultAddress { get; set; } = CryptoHockeyRewardVaultAddress;

    public string RewardIssuerUrl { get; set; } = CryptoHockeyRewardIssuerUrl;

    public string TokenSymbol { get; set; } = "A1870";

    public int RewardTokenDecimals { get; set; } = 18;

    /// <summary>Amount shown to the player; the issuer decides the signed on-chain amount.</summary>
    public string RewardAmount { get; set; } = "10";

    public int DefaultNetworkChainId { get; set; } = 1;

    public int[] SupportedChainIds { get; set; } = [1, 11155111, 137];

    /// <summary>Minimum seconds a race must last before it earns a reward (prevents quit-and-restart spamming).</summary>
    public int MinimumRaceSeconds { get; set; } = 30;
}
