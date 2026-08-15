using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Huuray.Serialization;

namespace Huuray;

/// <summary>
/// Currency conversion between two currencies.
/// </summary>
public sealed class ExchangeRatesResource
{
    private readonly HuurayClient _client;

    internal ExchangeRatesResource(HuurayClient client) => _client = client;

    /// <summary>
    /// Current exchange rate and spread between two currencies.
    /// </summary>
    /// <param name="fromCurrency">Source currency, ISO alpha-3. Sent as <c>FromCurrency</c>.</param>
    /// <param name="toCurrency">Target currency, ISO alpha-3. Sent as <c>ToCurrency</c>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The rate and the spread.</returns>
    /// <remarks><c>GET /v4/ExchangeRates</c>. A read, so it is retried.</remarks>
    /// <exception cref="ArgumentNullException">Either currency is <see langword="null"/>.</exception>
    /// <exception cref="HuurayApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<ExchangeRateResult> GetAsync(
        string fromCurrency,
        string toCurrency,
        CancellationToken cancellationToken = default)
    {
        if (fromCurrency is null)
        {
            throw new ArgumentNullException(nameof(fromCurrency));
        }

        if (toCurrency is null)
        {
            throw new ArgumentNullException(nameof(toCurrency));
        }

        string? query = HuurayClient.BuildQuery(
            new KeyValuePair<string, string?>("FromCurrency", fromCurrency),
            new KeyValuePair<string, string?>("ToCurrency", toCurrency));

        HuurayResponse<ExchangeRatesResponseWire> response = await _client.SendAsync(
                HttpMethod.Get,
                "/v4/ExchangeRates",
                jsonBody: null,
                query,
                retryable: true,
                HuurayJsonContext.Default.ExchangeRatesResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        return new ExchangeRateResult(response.Data?.ExchangeRate, response.Data?.Spread);
    }
}
