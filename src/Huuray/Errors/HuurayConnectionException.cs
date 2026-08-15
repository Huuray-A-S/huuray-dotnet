using System;
using System.Globalization;

namespace Huuray;

/// <summary>
/// The request never completed: a network failure, DNS, TLS, a dropped socket,
/// a timeout — or a 2xx response whose body could not be read or parsed.
/// </summary>
/// <remarks>
/// A 2xx with an empty or unparseable body is deliberately an error rather than an
/// empty result. Coercing it into "nothing found" would let a garbled
/// <c>POST /v4/Search</c> response read as "the order did not land", and the
/// documented reconciliation flow would then order a second time.
/// </remarks>
public class HuurayConnectionException : HuurayException
{
    /// <summary>Creates a connection exception.</summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="method">The HTTP method of the request that failed.</param>
    /// <param name="path">The request path, for example <c>/v4/Order</c>.</param>
    /// <param name="innerException">The transport exception that caused this one, if any.</param>
    public HuurayConnectionException(string message, string method, string path, Exception? innerException = null)
        : base(message, innerException)
    {
        Method = method;
        Path = path;
    }

    /// <summary>The HTTP method of the request that failed.</summary>
    public string Method { get; }

    /// <summary>The path of the request that failed, for example <c>/v4/Order</c>.</summary>
    public string Path { get; }
}

/// <summary>
/// The request exceeded the client's configured timeout.
/// </summary>
/// <remarks>
/// Cancellation requested by your own <see cref="System.Threading.CancellationToken"/>
/// is <em>not</em> reported here: that surfaces as
/// <see cref="OperationCanceledException"/>, because you asked for it.
/// </remarks>
public sealed class HuurayTimeoutException : HuurayConnectionException
{
    /// <summary>Creates a timeout exception.</summary>
    /// <param name="method">The HTTP method of the request that timed out.</param>
    /// <param name="path">The request path, for example <c>/v4/Order</c>.</param>
    /// <param name="timeout">The timeout that elapsed.</param>
    /// <param name="innerException">The cancellation exception that caused this one, if any.</param>
    public HuurayTimeoutException(string method, string path, TimeSpan timeout, Exception? innerException = null)
        : base(
            string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} timed out after {2:0}ms.",
                method,
                path,
                timeout.TotalMilliseconds),
            method,
            path,
            innerException)
    {
        Timeout = timeout;
    }

    /// <summary>The timeout that elapsed before the request completed.</summary>
    public TimeSpan Timeout { get; }
}
