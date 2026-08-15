using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Huuray.Serialization;

namespace Huuray;

/// <summary>
/// Stock levels for a product.
/// </summary>
public sealed class StockResource
{
    private readonly HuurayClient _client;

    internal StockResource(HuurayClient client) => _client = client;

    /// <summary>
    /// Current stock for a product.
    /// </summary>
    /// <param name="request">The product, and optionally the denomination to check.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>How many gift cards are available.</returns>
    /// <remarks>
    /// <c>POST /v4/Stock</c> — a read, despite being a POST, so it is retried.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HuurayApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<CheckStockResult> CheckAsync(
        CheckStockRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string body = JsonSerializer.Serialize(
            new StockRequestWire { ProductToken = request.ProductToken, Value = request.Value },
            HuurayJsonContext.Default.StockRequestWire);

        HuurayResponse<StockResponseWire> response = await _client.SendAsync(
                HttpMethod.Post,
                "/v4/Stock",
                body,
                query: null,
                retryable: true,
                HuurayJsonContext.Default.StockResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        return new CheckStockResult(response.Data?.Stock);
    }
}
