using System;
using System.Collections.Generic;

namespace Huuray;

/// <summary>
/// The resolved retry knobs, plus the two decisions the send loop needs from them.
/// </summary>
internal sealed class RetryPolicy
{
    /// <summary>
    /// HTTP statuses worth repeating a <em>read</em> for.
    /// </summary>
    /// <remarks>
    /// <c>429</c> is included defensively: it is not a documented response on any v4
    /// endpoint, so this client never assumes rate limiting exists — but if one appears,
    /// backing off is strictly better than hammering.
    /// </remarks>
    private static readonly HashSet<int> RetryableStatuses = new() { 408, 425, 429, 500, 502, 503, 504 };

    private RetryPolicy(int maxRetries, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        MaxRetries = maxRetries;
        BaseDelay = baseDelay;
        MaxDelay = maxDelay;
    }

    internal int MaxRetries { get; }

    internal TimeSpan BaseDelay { get; }

    internal TimeSpan MaxDelay { get; }

    /// <summary>
    /// Resolves user-supplied options against the defaults, one property at a time.
    /// </summary>
    /// <remarks>
    /// Per-property fallback, not an all-or-nothing swap: an options object that sets
    /// only <see cref="RetryOptions.BaseDelay"/> must keep the default
    /// <see cref="RetryOptions.MaxRetries"/>, never silently take zero. A clobbered
    /// <c>MaxRetries</c> would be invisible until the first transient failure.
    /// </remarks>
    internal static RetryPolicy Resolve(RetryOptions? options)
    {
        RetryOptions defaults = RetryOptions.Default;

        int maxRetries = options?.MaxRetries ?? defaults.MaxRetries!.Value;
        TimeSpan baseDelay = options?.BaseDelay ?? defaults.BaseDelay!.Value;
        TimeSpan maxDelay = options?.MaxDelay ?? defaults.MaxDelay!.Value;

        return new RetryPolicy(
            Math.Max(0, maxRetries),
            baseDelay < TimeSpan.Zero ? TimeSpan.Zero : baseDelay,
            maxDelay < TimeSpan.Zero ? TimeSpan.Zero : maxDelay);
    }

    /// <summary>
    /// Whether a response status should be retried, given the operation is already
    /// known to be safe to repeat.
    /// </summary>
    internal static bool IsRetryableStatus(int status) => RetryableStatuses.Contains(status);

    /// <summary>Exponential backoff with full jitter, so parallel clients do not resonate.</summary>
    internal TimeSpan BackoffDelay(int attempt)
    {
        double exponential = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        double capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capped);
    }
}
