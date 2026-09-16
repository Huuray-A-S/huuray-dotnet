using System;
using System.Text;

namespace Huuray;

/// <summary>
/// Everything <see cref="HuurayClient"/> needs, and the few knobs worth turning.
/// </summary>
/// <example>
/// <code language="csharp">
/// HuurayClient huuray = new(new HuurayClientOptions
/// {
///     ApiToken = Environment.GetEnvironmentVariable("HUURAY_API_TOKEN")!,
///     ApiSecret = Environment.GetEnvironmentVariable("HUURAY_API_SECRET")!,
/// });
/// </code>
/// </example>
public sealed record HuurayClientOptions
{
    /// <summary>
    /// Your API token. Sent as <c>X-API-TOKEN</c>, so it must not contain a control or
    /// non-ASCII character — trim a value read from a file.
    /// </summary>
    public required string ApiToken { get; init; }

    /// <summary>Your API secret. Used to sign each request; never sent, and never logged.</summary>
    public required string ApiSecret { get; init; }

    /// <summary>Override the API host. Defaults to <see cref="HuurayClient.DefaultBaseUrl"/>.</summary>
    /// <remarks>
    /// An absolute http(s) URL without user-info, a query or a fragment. A trailing slash is fine.
    /// </remarks>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Encoding of the <c>X-API-HASH</c> digest. Defaults to lowercase hexadecimal.
    /// </summary>
    /// <remarks>
    /// If you see a 401 with credentials you know are good, this is the first thing to try.
    /// </remarks>
    public HashEncoding HashEncoding { get; init; } = RequestSigner.DefaultHashEncoding;

    /// <summary>Per-request timeout, from 1 to 4294967294 milliseconds. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Retry behaviour for read operations. Writes are never retried, whatever this says.
    /// </summary>
    public RetryOptions? Retry { get; init; }

    /// <summary>
    /// Appended to the <c>User-Agent</c>, for example your app name and version. Must not
    /// contain a control or non-ASCII character.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Supply your own nonce.
    /// </summary>
    /// <remarks>
    /// Must be unique per request, unused for 60 days, and 1 to
    /// <see cref="RequestSigner.NonceMaxLength"/> characters of visible ASCII. The
    /// default — 24 random bytes as base64url — is right for almost everyone. Avoid
    /// timestamps: they collide under concurrency and the resulting 401s are intermittent
    /// and hard to trace.
    /// </remarks>
    public Func<string>? NonceFactory { get; init; }

    /// <summary>
    /// Writes the members for the record's <c>ToString</c>, with the credentials replaced.
    /// </summary>
    /// <remarks>
    /// The compiler-generated version would print <see cref="ApiToken"/> and
    /// <see cref="ApiSecret"/> in the clear, so logging the options would log both. Every
    /// other member is printed as the compiler would print it.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ApiToken = [redacted], ApiSecret = [redacted], BaseUrl = ");
        builder.Append(BaseUrl);
        builder.Append(", HashEncoding = ");
        builder.Append(HashEncoding.ToString());
        builder.Append(", Timeout = ");
        builder.Append(Timeout.ToString());
        builder.Append(", Retry = ");
        builder.Append(Retry);
        builder.Append(", UserAgent = ");
        builder.Append(UserAgent);
        builder.Append(", NonceFactory = ");
        builder.Append(NonceFactory);
        return true;
    }
}
