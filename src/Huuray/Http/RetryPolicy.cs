using System;
using System.Collections.Generic;
using System.Globalization;

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

    /// <summary>The longest delay <see cref="System.Threading.Tasks.Task.Delay(TimeSpan)"/> accepts.</summary>
    private static readonly TimeSpan MaxDelayValue = TimeSpan.FromMilliseconds(4_294_967_294);

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
    /// <exception cref="HuurayConfigurationException">A delay is above 4294967294 milliseconds.</exception>
    internal static RetryPolicy Resolve(RetryOptions? options)
    {
        RetryOptions defaults = RetryOptions.Default;

        int maxRetries = options?.MaxRetries ?? defaults.MaxRetries!.Value;
        TimeSpan baseDelay = options?.BaseDelay ?? defaults.BaseDelay!.Value;
        TimeSpan maxDelay = options?.MaxDelay ?? defaults.MaxDelay!.Value;

        // Task.Delay throws above 4294967294 ms, and the wait runs only after a first
        // attempt has already been sent, so the limit is checked here, at construction.
        // Every computed wait is at most MaxDelay; BaseDelay shares the limit so that one
        // rule covers both.
        CheckDelay(nameof(RetryOptions.BaseDelay), baseDelay);
        CheckDelay(nameof(RetryOptions.MaxDelay), maxDelay);

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
        // From attempt 1024, 2^attempt is infinity, and zero times infinity is NaN,
        // which TimeSpan refuses.
        if (BaseDelay == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        double exponential = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        double capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * capped);
    }

    private static void CheckDelay(string name, TimeSpan delay)
    {
        if (delay > MaxDelayValue)
        {
            throw new HuurayConfigurationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Retry.{0} must be at most 4294967294 milliseconds (about 49.7 days), received {1} ms.",
                    name,
                    delay.TotalMilliseconds));
        }
    }
}
