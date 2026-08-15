using System;

namespace Huuray;

/// <summary>
/// Retry behaviour for the operations that are safe to repeat.
/// </summary>
/// <remarks>
/// <para>
/// The v4 API exposes no idempotency key. <c>RefID</c> is a reference you choose for
/// your own reconciliation; it is <em>not</em> a server-side deduplication key. So a
/// retried <c>POST /v4/Order</c> can create a second order, and a retried
/// <c>POST /v4/Resend</c> can re-deliver a live gift card.
/// </para>
/// <para>
/// Because of that, retries are <strong>opt-in per operation</strong> and are never
/// inferred from the HTTP method:
/// </para>
/// <list type="table">
///   <listheader><term>Retried</term><description>Never retried</description></listheader>
///   <item>
///     <term>Balance, Catalogue, Template, Stock, ExchangeRates, Search</term>
///     <description>Order, Resend, Cancel</description>
///   </item>
/// </list>
/// <para>
/// Note that four of the retried operations are POSTs. They are POSTs because they take
/// a request body, not because they change anything.
/// </para>
/// <para>
/// Every property is nullable so that a partially-populated instance falls back to the
/// defaults rather than clobbering them with zero.
/// </para>
/// </remarks>
public sealed record RetryOptions
{
    /// <summary>Attempts after the first. <c>0</c> disables retrying entirely. Default <c>2</c>.</summary>
    /// <remarks>A negative value is clamped to zero rather than skipping the request.</remarks>
    public int? MaxRetries { get; init; }

    /// <summary>Base backoff delay; doubles per attempt, with full jitter. Default 250ms.</summary>
    public TimeSpan? BaseDelay { get; init; }

    /// <summary>Ceiling for a single backoff wait. Default 4s.</summary>
    public TimeSpan? MaxDelay { get; init; }

    /// <summary>The defaults, which are deliberately conservative.</summary>
    public static RetryOptions Default { get; } = new()
    {
        MaxRetries = 2,
        BaseDelay = TimeSpan.FromMilliseconds(250),
        MaxDelay = TimeSpan.FromSeconds(4),
    };

    /// <summary>No retries at all — every operation is attempted exactly once.</summary>
    public static RetryOptions None { get; } = new() { MaxRetries = 0 };
}
