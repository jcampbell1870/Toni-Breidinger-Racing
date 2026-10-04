using System.Buffers;
using System.Numerics;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.ABI.Model;

namespace ToniBreidingerRacing.Core.Rewards;

/// <summary>Validation and ABI encoding for Arcade1870RewardVault claims (mirrors Crypto Hockey).</summary>
public static class RewardClaimEncoder
{
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    public static bool IsValidAddress(string? address) =>
        address is { Length: 42 }
        && address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && !address.AsSpan(2).ContainsAnyExcept(HexDigits);

    /// <summary>Normalizes a 65-byte ECDSA signature to hex with a 27/28 recovery byte.</summary>
    public static bool TryNormalizeSignature(string? signature, out string normalizedHex)
    {
        normalizedHex = string.Empty;
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var normalized = signature.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        if (normalized.Length % 2 != 0)
        {
            normalized = $"0{normalized}";
        }

        if (normalized.Length != 130 || normalized.AsSpan().ContainsAnyExcept(HexDigits))
        {
            return false;
        }

        var v = Convert.ToByte(normalized[^2..], 16);
        if (v is 0 or 1)
        {
            normalized = $"{normalized[..128]}{v + 27:x2}";
        }
        else if (v is not 27 and not 28)
        {
            return false;
        }

        normalizedHex = normalized.ToLowerInvariant();
        return true;
    }

    public static bool TryValidate(
        RewardClaimPayload? payload,
        RewardTreasuryOptions options,
        DateTimeOffset now,
        out string error)
    {
        if (payload is null)
        {
            error = "Reward issuer response was empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(payload.Nonce) || !BigInteger.TryParse(payload.Nonce, out var nonce) || nonce < 0)
        {
            error = "Reward claim payload has an invalid nonce.";
            return false;
        }

        if (!TryNormalizeSignature(payload.Signature, out _))
        {
            error = "Reward claim payload contains an invalid signature.";
            return false;
        }

        if (!BigInteger.TryParse(payload.Amount, out var amount) || amount <= 0)
        {
            error = "Reward claim payload contains an invalid amount.";
            return false;
        }

        if (!IsValidAddress(payload.VaultAddress))
        {
            error = "Reward claim payload contains an invalid vault address.";
            return false;
        }

        if (!string.Equals(options.RewardVaultAddress, payload.VaultAddress, StringComparison.OrdinalIgnoreCase))
        {
            error = "Reward claim payload vault does not match the Arcade1870 reward treasury.";
            return false;
        }

        if (payload.ChainId <= 0
            || (options.SupportedChainIds.Length > 0 && !options.SupportedChainIds.Contains(payload.ChainId)))
        {
            error = "Reward claim payload chain id is not supported.";
            return false;
        }

        if (payload.Deadline <= now.ToUnixTimeSeconds())
        {
            error = "Reward claim payload has expired.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>Encodes <c>claim(uint256 amount, uint256 nonce, uint256 deadline, bytes signature)</c>.</summary>
    public static string EncodeClaimCall(RewardClaimPayload payload)
    {
        if (!BigInteger.TryParse(payload.Amount, out var amount) || amount <= 0)
        {
            throw new InvalidOperationException("Reward claim amount is invalid.");
        }

        if (!BigInteger.TryParse(payload.Nonce, out var nonce) || nonce < 0)
        {
            throw new InvalidOperationException("Reward claim nonce is invalid.");
        }

        if (payload.Deadline <= 0)
        {
            throw new InvalidOperationException("Reward claim deadline is invalid.");
        }

        if (!TryNormalizeSignature(payload.Signature, out var signatureHex))
        {
            throw new InvalidOperationException("Reward claim signature is invalid.");
        }

        var parameters = new[]
        {
            new Parameter("uint256", 1),
            new Parameter("uint256", 2),
            new Parameter("uint256", 3),
            new Parameter("bytes", 4),
        };

        return new FunctionCallEncoder().EncodeRequest(
            new FunctionABI("claim", false) { InputParameters = parameters }.Sha3Signature,
            parameters,
            amount,
            nonce,
            new BigInteger(payload.Deadline),
            Convert.FromHexString(signatureHex));
    }

    /// <summary>Formats a raw token amount (smallest units) using the token's decimals, e.g. 10000000000000000000 → "10".</summary>
    public static string FormatTokenAmount(string rawAmount, int decimals)
    {
        if (!BigInteger.TryParse(rawAmount, out var value) || value < 0)
        {
            return rawAmount;
        }

        var divisor = BigInteger.Pow(10, Math.Max(decimals, 0));
        var whole = BigInteger.DivRem(value, divisor, out var remainder);
        if (remainder.IsZero)
        {
            return whole.ToString();
        }

        var fraction = remainder.ToString().PadLeft(decimals, '0').TrimEnd('0');
        return $"{whole}.{fraction}";
    }
}
