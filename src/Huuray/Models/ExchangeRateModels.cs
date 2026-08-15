namespace Huuray;

/// <summary>
/// The result of <c>GET /v4/ExchangeRates</c>.
/// </summary>
/// <param name="ExchangeRate">The rate from the source currency to the target currency.</param>
/// <param name="Spread">Spread, in percent.</param>
public sealed record ExchangeRateResult(double? ExchangeRate, int? Spread);
