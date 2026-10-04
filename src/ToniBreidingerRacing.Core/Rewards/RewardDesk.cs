namespace ToniBreidingerRacing.Core.Rewards;

public enum RewardStatus
{
    /// <summary>No reward for this race (rewards disabled or the race was too short).</summary>
    NotEligible,

    /// <summary>The race earned A1870 but no wallet address is set yet.</summary>
    NeedsWallet,

    /// <summary>Waiting for the shared Arcade1870 reward issuer to sign the claim.</summary>
    Requesting,

    /// <summary>A signed claim is ready to submit with MetaMask.</summary>
    Ready,

    /// <summary>The claim page has been opened in the browser.</summary>
    ClaimPageOpened,

    /// <summary>The issuer could not sign a claim; the player can retry.</summary>
    Failed,
}

/// <summary>
/// Tracks the A1870 reward for the race that just finished: asks the shared issuer for a signed claim in the
/// background, then serves the MetaMask claim page on loopback when the player presses C.
/// </summary>
public sealed class RewardDesk : IAsyncDisposable
{
    private readonly RewardIssuerClient _client;
    private ClaimPageServer? _server;
    private Task<RewardClaimResult>? _pending;
    private CancellationTokenSource? _cancellation;

    public RewardDesk(RewardIssuerClient client)
    {
        _client = client;
    }

    public RewardStatus Status { get; private set; } = RewardStatus.NotEligible;

    public string Message { get; private set; } = string.Empty;

    public RewardGameProof? Proof { get; private set; }

    public string TrackName { get; private set; } = string.Empty;

    public RewardClaimTransaction? Transaction { get; private set; }

    public RewardTreasuryOptions Options => _client.Options;

    /// <summary>Fires once when a claim becomes ready (for the coin sound and profile bookkeeping).</summary>
    public event Action<RewardGameProof>? ClaimReady;

    /// <summary>Starts a new reward for a finished race. Pass null <paramref name="proof"/> when the race is not eligible.</summary>
    public void Begin(RewardGameProof? proof, string trackName, string walletAddress)
    {
        CancelPending();
        Proof = proof;
        TrackName = trackName;
        Transaction = null;
        Status = RewardStatus.NotEligible;
        if (proof is null)
        {
            Message = Options.Enabled ? "Race too short for A1870." : "A1870 rewards are turned off.";
            return;
        }

        Request(walletAddress);
    }

    /// <summary>Requests (or re-requests) the signed claim for the current race.</summary>
    public void Request(string walletAddress)
    {
        if (Proof is null || Status is RewardStatus.Requesting or RewardStatus.Ready or RewardStatus.ClaimPageOpened)
        {
            return;
        }

        if (!RewardClaimEncoder.IsValidAddress(walletAddress))
        {
            Status = RewardStatus.NeedsWallet;
            Message = $"Set a wallet (W) to receive {Options.RewardAmount} {Options.TokenSymbol}.";
            return;
        }

        _cancellation = new CancellationTokenSource();
        Status = RewardStatus.Requesting;
        Message = $"Requesting {Options.RewardAmount} {Options.TokenSymbol} from the Arcade1870 treasury...";
        _pending = _client.RequestClaimAsync(walletAddress, Proof, _cancellation.Token);
    }

    /// <summary>Call once per frame on the game thread to pick up the issuer's answer.</summary>
    public void Poll()
    {
        if (_pending is not { IsCompleted: true } task)
        {
            return;
        }

        _pending = null;
        _cancellation?.Dispose();
        _cancellation = null;
        RewardClaimResult result;
        try
        {
            result = task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = RewardClaimResult.Failure($"Reward request failed: {ex.Message}");
        }

        if (result.IsSuccessful && result.Transaction is { } transaction)
        {
            Transaction = transaction;
            Status = RewardStatus.Ready;
            Message = $"{transaction.DisplayAmount} {transaction.TokenSymbol} ready! Press C to claim with MetaMask.";
            if (Proof is not null)
            {
                ClaimReady?.Invoke(Proof);
            }
        }
        else
        {
            Status = RewardStatus.Failed;
            Message = (result.ErrorMessage ?? "Reward issuer did not respond.") + " Press R to retry.";
        }
    }

    /// <summary>Publishes the MetaMask claim page on 127.0.0.1 and returns its URL, or null if no claim is ready.</summary>
    public Uri? PublishClaimPage()
    {
        if (Transaction is null)
        {
            return null;
        }

        _server ??= new ClaimPageServer();
        var uri = _server.Publish(ClaimPageBuilder.Build(Transaction, TrackName));
        Status = RewardStatus.ClaimPageOpened;
        Message = "Claim page opened in your browser. Confirm in MetaMask.";
        return uri;
    }

    public void Reset()
    {
        CancelPending();
        Proof = null;
        Transaction = null;
        Status = RewardStatus.NotEligible;
        Message = string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        CancelPending();
        if (_server is not null)
        {
            await _server.DisposeAsync();
            _server = null;
        }
    }

    private void CancelPending()
    {
        // The in-flight request may still observe the token, so it is disposed only once that request ends.
        var cancellation = _cancellation;
        var pending = _pending;
        _cancellation = null;
        _pending = null;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        if (pending is null)
        {
            cancellation.Dispose();
        }
        else
        {
            pending.ContinueWith(_ => cancellation.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
