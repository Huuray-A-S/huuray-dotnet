namespace Huuray;

/// <summary>
/// Parameters for <c>POST /v4/Stock</c>.
/// </summary>
public sealed record CheckStockRequest
{
    /// <summary>The product to check. Take this from <c>Catalogue.ListAsync()</c>.</summary>
    public required string ProductToken { get; init; }

    /// <summary>
    /// The denomination to check, in <strong>minor units</strong> — 5.00 is <c>500</c>.
    /// Leave unset to use the product's default price.
    /// </summary>
    /// <remarks>
    /// See <see cref="MinorUnits"/> if your amount arrives as a <see cref="decimal"/>.
    /// </remarks>
    public int? Value { get; init; }
}

/// <summary>
/// The result of <c>POST /v4/Stock</c>.
/// </summary>
/// <param name="Stock">
/// Number of gift cards available, or <see langword="null"/> if the API did not report one.
/// </param>
public sealed record CheckStockResult(int? Stock);
