using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Huuray.Serialization;

namespace Huuray;

/// <summary>
/// Balances on your B2B account.
/// </summary>
public sealed class BalancesResource
{
    private readonly HuurayClient _client;

    internal BalancesResource(HuurayClient client) => _client = client;

    /// <summary>
    /// Available balances on your B2B account, per currency.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>One entry per currency held on the account.</returns>
    /// <remarks>
    /// <c>GET /v4/Balance</c>. Amounts are in <strong>minor units</strong>:
    /// <c>50000</c> means 500.00, not 50000.00.
    /// <para>Safe to repeat, so this call is retried on transient failures.</para>
    /// </remarks>
    /// <exception cref="HuurayApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<ListBalancesResult> ListAsync(CancellationToken cancellationToken = default)
    {
        HuurayResponse<BalanceResponseWire> response = await _client.SendAsync(
                HttpMethod.Get,
                "/v4/Balance",
                jsonBody: null,
                query: null,
                retryable: true,
                HuurayJsonContext.Default.BalanceResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        List<BalanceItemWire> items = response.Data?.Balances ?? new List<BalanceItemWire>();
        BalanceItem[] balances = new BalanceItem[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            balances[i] = new BalanceItem(items[i].Currency, items[i].Balance, items[i].Master);
        }

        return new ListBalancesResult(balances);
    }
}
