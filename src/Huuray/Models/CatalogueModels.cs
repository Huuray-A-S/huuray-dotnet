using System.Collections.Generic;

namespace Huuray;

/// <summary>
/// A product in the Huuray catalogue.
/// </summary>
/// <param name="ProductToken">
/// Unique product identifier, used when ordering. Only present when the catalogue was
/// requested with <c>all: false</c> — the full catalogue omits tokens, because they
/// describe your account's access rather than the public list.
/// </param>
/// <param name="BrandName">The brand as Huuray names it.</param>
/// <param name="Country">Country name.</param>
/// <param name="CountryCode">ISO alpha-2 country code.</param>
/// <param name="Discount">
/// Your discount on this product, in percent. Only present when the catalogue was
/// requested with <c>all: false</c>.
/// </param>
/// <param name="Denominations">Available denominations, comma-separated, as returned by the API.</param>
/// <param name="Currency">ISO alpha-3 currency code.</param>
/// <param name="RealTimeStock">Whether codes are generated in real time or drawn from stock.</param>
/// <param name="Categories">Categories, comma-separated, as returned by the API.</param>
/// <param name="LanguageCode">ISO alpha-2 language code.</param>
/// <param name="Active">Whether the product can currently be ordered.</param>
/// <param name="BrandDescription">Marketing description of the brand.</param>
/// <param name="RedemptionInstructions">How a recipient redeems this product.</param>
/// <param name="LogoFile">Location of the brand logo, as returned by the API.</param>
public sealed record CatalogueProduct(
    string? ProductToken,
    string? BrandName,
    string? Country,
    string? CountryCode,
    decimal? Discount,
    string? Denominations,
    string? Currency,
    string? RealTimeStock,
    string? Categories,
    string? LanguageCode,
    bool Active,
    string? BrandDescription,
    string? RedemptionInstructions,
    string? LogoFile);

/// <summary>
/// The result of <c>POST /v4/Catalogue</c>.
/// </summary>
/// <param name="Products">The products returned for this request.</param>
public sealed record ListCatalogueResult(IReadOnlyList<CatalogueProduct> Products);
