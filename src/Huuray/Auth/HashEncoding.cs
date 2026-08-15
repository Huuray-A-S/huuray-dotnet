namespace Huuray;

/// <summary>
/// How the SHA-512 digest is encoded into the <c>X-API-HASH</c> header.
/// </summary>
/// <remarks>
/// The v4 specification states the construction — <c>SHA512( API-SECRET + NONCE )</c> —
/// but not the encoding of the digest, so this client makes it configurable.
/// <see cref="HashEncoding.Hex"/> is the default, and is the encoding confirmed against
/// the live API.
/// </remarks>
public enum HashEncoding
{
    /// <summary>Lowercase hexadecimal. The default, and live-confirmed against <c>GET /v4/Balance</c>.</summary>
    Hex = 0,

    /// <summary>Uppercase hexadecimal.</summary>
    HexUpper = 1,

    /// <summary>Standard base64, with padding.</summary>
    Base64 = 2,

    /// <summary>URL-safe base64, without padding.</summary>
    Base64Url = 3,
}
