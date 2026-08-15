using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Huuray;

/// <summary>
/// Builds the three authentication headers every v4 request carries.
/// </summary>
/// <remarks>
/// <para>
/// <c>X-API-TOKEN</c> — your API token.<br/>
/// <c>X-API-NONCE</c> — a random value, single-use within 60 days, at most 50 characters.<br/>
/// <c>X-API-HASH</c> — SHA-512 of ( API-SECRET + NONCE ).
/// </para>
/// <para>
/// <see cref="HuurayClient"/> does all of this for you. The methods here are public
/// because they are occasionally useful on their own — signing a request made by
/// some other transport, for example — and because they are worth being able to test.
/// </para>
/// </remarks>
public static class RequestSigner
{
    /// <summary>Name of the header carrying the API token.</summary>
    public const string TokenHeaderName = "X-API-TOKEN";

    /// <summary>Name of the header carrying the single-use nonce.</summary>
    public const string NonceHeaderName = "X-API-NONCE";

    /// <summary>Name of the header carrying the request signature.</summary>
    public const string HashHeaderName = "X-API-HASH";

    /// <summary>
    /// The specification's stated maximum length of <c>X-API-NONCE</c>.
    /// </summary>
    /// <remarks>
    /// Exceeding it is rejected by the API, and a too-long nonce is an easy mistake:
    /// 32 random bytes encoded as hexadecimal is 64 characters, silently over the limit.
    /// </remarks>
    public const int NonceMaxLength = 50;

    /// <summary>
    /// The digest encoding this client uses unless you override it: lowercase hexadecimal.
    /// </summary>
    /// <remarks>
    /// Confirmed live on 2026-08-15 against <c>GET /v4/Balance</c>; the other three
    /// candidate encodings returned <c>401</c>. If you get a 401 with credentials you
    /// know are correct, this is the first thing to try changing.
    /// </remarks>
    public const HashEncoding DefaultHashEncoding = HashEncoding.Hex;

    /// <summary>Bytes of entropy per generated nonce. 24 bytes becomes 32 base64url characters.</summary>
    private const int NonceByteCount = 24;

    /// <summary>
    /// Generates a nonce: 24 cryptographically random bytes as base64url, 32 characters.
    /// </summary>
    /// <returns>A fresh nonce, comfortably inside the API's 50-character limit.</returns>
    /// <remarks>
    /// The API stores nonces for 60 days and rejects a repeat, so the only thing that
    /// matters is that values never collide. 192 bits of entropy makes that negligible
    /// at any realistic volume.
    /// <para>
    /// Avoid timestamps: at second resolution they collide under concurrency, and the
    /// resulting 401s are intermittent and hard to trace.
    /// </para>
    /// </remarks>
    public static string GenerateNonce()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(NonceByteCount);
        return ToBase64Url(bytes);
    }

    /// <summary>
    /// Computes the <c>X-API-HASH</c> value for a secret and nonce.
    /// </summary>
    /// <param name="apiSecret">Your API secret. Never sent, and never logged by this library.</param>
    /// <param name="nonce">The same nonce sent in <c>X-API-NONCE</c>.</param>
    /// <param name="encoding">Digest encoding. Defaults to <see cref="DefaultHashEncoding"/>.</param>
    /// <returns>The encoded SHA-512 digest of <paramref name="apiSecret"/> followed by <paramref name="nonce"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="apiSecret"/> or <paramref name="nonce"/> is <see langword="null"/>.</exception>
    public static string Sign(string apiSecret, string nonce, HashEncoding encoding = DefaultHashEncoding)
    {
        if (apiSecret is null)
        {
            throw new ArgumentNullException(nameof(apiSecret));
        }

        if (nonce is null)
        {
            throw new ArgumentNullException(nameof(nonce));
        }

        byte[] digest = SHA512.HashData(Encoding.UTF8.GetBytes(apiSecret + nonce));
        return Encode(digest, encoding);
    }

    /// <summary>
    /// Builds the three authentication headers for a single request.
    /// </summary>
    /// <param name="apiToken">Your API token.</param>
    /// <param name="apiSecret">Your API secret. Used to sign; never placed in a header.</param>
    /// <param name="nonce">A fresh nonce, at most <see cref="NonceMaxLength"/> characters.</param>
    /// <param name="encoding">Digest encoding. Defaults to <see cref="DefaultHashEncoding"/>.</param>
    /// <returns>Exactly three headers: token, nonce, and hash.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="nonce"/> is longer than <see cref="NonceMaxLength"/>.</exception>
    public static IReadOnlyDictionary<string, string> BuildAuthHeaders(
        string apiToken,
        string apiSecret,
        string nonce,
        HashEncoding encoding = DefaultHashEncoding)
    {
        if (apiToken is null)
        {
            throw new ArgumentNullException(nameof(apiToken));
        }

        if (nonce is null)
        {
            throw new ArgumentNullException(nameof(nonce));
        }

        if (nonce.Length > NonceMaxLength)
        {
            throw new ArgumentException(
                $"Nonce is {nonce.Length} characters; the Huuray API accepts at most {NonceMaxLength}. " +
                "If you supplied a custom NonceFactory, shorten its output.",
                nameof(nonce));
        }

        return new Dictionary<string, string>(3, StringComparer.Ordinal)
        {
            [TokenHeaderName] = apiToken,
            [NonceHeaderName] = nonce,
            [HashHeaderName] = Sign(apiSecret, nonce, encoding),
        };
    }

    private static string Encode(byte[] digest, HashEncoding encoding) => encoding switch
    {
        HashEncoding.Hex => Convert.ToHexString(digest).ToLowerInvariant(),
        HashEncoding.HexUpper => Convert.ToHexString(digest),
        HashEncoding.Base64 => Convert.ToBase64String(digest),
        HashEncoding.Base64Url => ToBase64Url(digest),
        _ => throw new ArgumentOutOfRangeException(
            nameof(encoding),
            encoding,
            "Unknown hash encoding. Use Hex, HexUpper, Base64 or Base64Url."),
    };

    /// <summary>Base64 with the URL-safe alphabet and no padding.</summary>
    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
