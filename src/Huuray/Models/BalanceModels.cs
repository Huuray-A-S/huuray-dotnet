using System.Collections.Generic;

namespace Huuray;

/// <summary>
/// One currency balance on your B2B account.
/// </summary>
/// <param name="Currency">ISO alpha-3 currency code.</param>
/// <param name="Balance">
/// Available balance in <strong>minor units</strong> — <c>50000</c> is 500.00, not 50000.00.
/// </param>
/// <param name="Master">Whether this is a master currency on the account.</param>
public sealed record BalanceItem(string? Currency, long Balance, bool Master);

/// <summary>
/// The result of <c>GET /v4/Balance</c>.
/// </summary>
/// <param name="Balances">One entry per currency held on the account.</param>
public sealed record ListBalancesResult(IReadOnlyList<BalanceItem> Balances);
