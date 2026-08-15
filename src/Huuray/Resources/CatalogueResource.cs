using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Huuray.Serialization;

namespace Huuray;

/// <summary>
/// The Huuray product catalogue.
/// </summary>
public sealed class CatalogueResource
{
    private readonly HuurayClient _client;

    internal CatalogueResource(HuurayClient client) => _client = client;

    /// <summary>
    /// Lists available products.
    /// </summary>
    /// <param name="all">
    /// <see langword="false"/> (the default) returns only the products your account can
    /// order, including your discount and each product token.
    /// <see langword="true"/> returns the entire Huuray catalogue, without tokens or discounts.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The products the API returned.</returns>
    /// <remarks>
    /// <c>POST /v4/Catalogue</c> — a read, despite being a POST. It takes a request body
    /// but changes nothing, so it is safe to repeat and is retried.
    /// </remarks>
    /// <exception cref="HuurayApiException">The API returned a non-2xx response.</exception>
    /// <exception cref="HuurayConnectionException">The request never completed, or the response was unusable.</exception>
    public async Task<ListCatalogueResult> ListAsync(
        bool all = false,
        CancellationToken cancellationToken = default)
    {
        string body = JsonSerializer.Serialize(
            new CatalogueRequestWire { All = all },
            HuurayJsonContext.Default.CatalogueRequestWire);

        HuurayResponse<CatalogueResponseWire> response = await _client.SendAsync(
                HttpMethod.Post,
                "/v4/Catalogue",
                body,
                query: null,
                retryable: true,
                HuurayJsonContext.Default.CatalogueResponseWire,
                cancellationToken)
            .ConfigureAwait(false);

        List<CatalogueProductWire> items = response.Data?.Products ?? new List<CatalogueProductWire>();
        CatalogueProduct[] products = new CatalogueProduct[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            CatalogueProductWire p = items[i];
            products[i] = new CatalogueProduct(
                p.ProductToken,
                p.BrandName,
                p.Country,
                p.CountryCode,
                p.Discount,
                p.Denominations,
                p.Currency,
                p.RealTimeStock,
                p.Categories,
                p.LanguageCode,
                p.Active,
                p.BrandDescription,
                p.RedemptionInstructions,
                p.LogoFile);
        }

        return new ListCatalogueResult(products);
    }
}
