using System.Net;
using System.Text.Json;

namespace ToniBreidingerRacing.Core.Rewards;

/// <summary>
/// Builds the browser page that lets the player submit their A1870 claim to the Arcade1870RewardVault
/// with MetaMask (or any injected EIP-1193 wallet), exactly like the Crypto Hockey claim button.
/// </summary>
public static class ClaimPageBuilder
{
    public static string Build(RewardClaimTransaction transaction, string trackName)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (!RewardClaimEncoder.IsValidAddress(transaction.VaultAddress)
            || !RewardClaimEncoder.IsValidAddress(transaction.Recipient)
            || !RewardClaimEncoder.IsValidAddress(transaction.TokenAddress))
        {
            throw new ArgumentException("Claim transaction contains an invalid address.", nameof(transaction));
        }

        // The default System.Text.Json encoder escapes HTML-sensitive characters, so this is safe inside <script>.
        var json = JsonSerializer.Serialize(new
        {
            recipient = transaction.Recipient,
            vaultAddress = transaction.VaultAddress,
            chainId = transaction.ChainId,
            chainIdHex = $"0x{transaction.ChainId:x}",
            data = transaction.Data,
            tokenAddress = transaction.TokenAddress,
            tokenSymbol = transaction.TokenSymbol,
            tokenDecimals = transaction.TokenDecimals,
            deadline = transaction.Deadline,
        });

        var track = WebUtility.HtmlEncode(trackName);
        var amount = WebUtility.HtmlEncode(transaction.DisplayAmount);
        var symbol = WebUtility.HtmlEncode(transaction.TokenSymbol);
        var recipient = WebUtility.HtmlEncode(transaction.Recipient);
        var vault = WebUtility.HtmlEncode(transaction.VaultAddress);
        var data = WebUtility.HtmlEncode(transaction.Data);

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'">
            <title>Toni Breidinger Racing - Claim {{symbol}}</title>
            <style>
              body { font-family: Segoe UI, Arial, sans-serif; background: linear-gradient(160deg,#101030,#b0106a); color: #fff; margin: 0; padding: 40px; }
              .card { max-width: 720px; margin: auto; background: rgba(0,0,0,.35); border-radius: 18px; padding: 32px; box-shadow: 0 10px 40px rgba(0,0,0,.4); }
              h1 { margin-top: 0; color: #ffd6f5; letter-spacing: 2px; }
              button { font-size: 18px; padding: 14px 28px; border: 0; border-radius: 30px; background: #ff4fb4; color: #fff; cursor: pointer; }
              button:disabled { background: #888; cursor: default; }
              code { word-break: break-all; font-size: 12px; color: #ffe; }
              #status { margin-top: 18px; font-weight: 600; min-height: 1.4em; }
              details { margin-top: 24px; }
            </style>
            </head>
            <body>
            <div class="card">
              <h1>Toni Breidinger Racing</h1>
              <p>Thanks for racing with Toni at <strong>{{track}}</strong>! Claim your <strong>{{amount}} {{symbol}}</strong> from the Arcade1870 reward treasury.</p>
              <p>Recipient wallet: <code>{{recipient}}</code></p>
              <button id="claim">Claim with MetaMask</button>
              <div id="status"></div>
              <details>
                <summary>Manual claim details</summary>
                <p>Send a transaction from the recipient wallet to the reward vault <code>{{vault}}</code> with this data:</p>
                <code>{{data}}</code>
              </details>
            </div>
            <script>
            const claim = {{json}};
            const statusEl = document.getElementById('status');
            const button = document.getElementById('claim');
            const setStatus = text => { statusEl.textContent = text; };
            button.addEventListener('click', async () => {
              const provider = window.ethereum;
              if (!provider) { setStatus('No browser wallet found. Install MetaMask, then reload this page.'); return; }
              if (Date.now() / 1000 > claim.deadline) { setStatus('This claim has expired. Run another race to earn a new reward.'); return; }
              button.disabled = true;
              try {
                const accounts = await provider.request({ method: 'eth_requestAccounts' });
                if (!accounts || accounts.length === 0) { throw new Error('No wallet account available.'); }
                if (accounts[0].toLowerCase() !== claim.recipient.toLowerCase()) {
                  throw new Error('Switch MetaMask to the wallet ' + claim.recipient + ' that earned this reward.');
                }
                const currentChain = await provider.request({ method: 'eth_chainId' });
                if (parseInt(currentChain, 16) !== claim.chainId) {
                  await provider.request({ method: 'wallet_switchEthereumChain', params: [{ chainId: claim.chainIdHex }] });
                }
                setStatus('Confirm the claim in MetaMask...');
                const txHash = await provider.request({
                  method: 'eth_sendTransaction',
                  params: [{ from: accounts[0], to: claim.vaultAddress, data: claim.data }]
                });
                setStatus('Claim submitted! Transaction: ' + txHash);
                provider.request({
                  method: 'wallet_watchAsset',
                  params: { type: 'ERC20', options: { address: claim.tokenAddress, symbol: claim.tokenSymbol, decimals: claim.tokenDecimals } }
                }).catch(() => {});
              } catch (error) {
                button.disabled = false;
                setStatus(error && error.code === 4001 ? 'Claim was cancelled in MetaMask.' : 'Claim failed: ' + (error && error.message ? error.message : error));
              }
            });
            </script>
            </body>
            </html>
            """;
    }
}
