using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Huuray.Tests;

public class NonceTests
{
    [Fact]
    public void StaysWithinThe50CharacterLimitTheApiEnforces()
    {
        for (int i = 0; i < 1000; i++)
        {
            Assert.True(RequestSigner.GenerateNonce().Length <= RequestSigner.NonceMaxLength);
        }
    }

    [Fact]
    public void Produces32Base64UrlCharacters()
    {
        string nonce = RequestSigner.GenerateNonce();

        Assert.Equal(32, nonce.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", nonce);
    }

    [Fact]
    public void DoesNotRepeat_TheApiRejectsAReusedNonceFor60Days()
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < 100_000; i++)
        {
            seen.Add(RequestSigner.GenerateNonce());
        }

        Assert.Equal(100_000, seen.Count);
    }

    [Fact]
    public void RejectsACustomNonceThatWouldExceedTheApiLimit()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            RequestSigner.BuildAuthHeaders("t", "s", new string('x', 51)));

        Assert.Contains("at most 50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsANonceExactlyAtTheLimit()
    {
        IReadOnlyDictionary<string, string> headers =
            RequestSigner.BuildAuthHeaders("t", "s", new string('x', 50));

        Assert.Equal(3, headers.Count);
    }

    [Fact]
    public void Rejects32ByteHex_TheClassicOverLimitMistake()
    {
        // 32 random bytes as hexadecimal is 64 characters, silently over the limit.
        string hex64 = new('a', 64);

        Assert.Throws<ArgumentException>(() => RequestSigner.BuildAuthHeaders("t", "s", hex64));
    }
}

public class SigningTests
{
    /// <summary>
    /// Computed here independently rather than copied from the implementation, so this
    /// fails if the construction changes.
    /// </summary>
    private static string Expected(string secret, string nonce) =>
        Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(secret + nonce))).ToLowerInvariant();

    [Fact]
    public void IsSha512OverApiSecretThenNonce()
    {
        Assert.Equal(Expected("sec", "non"), RequestSigner.Sign("sec", "non"));
    }

    [Fact]
    public void IsOrderSensitive_NoncePlusSecretIsADifferentDigest()
    {
        Assert.NotEqual(RequestSigner.Sign("ab", "cd"), RequestSigner.Sign("cd", "ab"));
    }

    [Fact]
    public void DefaultsToLowercaseHex()
    {
        Assert.Equal(HashEncoding.Hex, RequestSigner.DefaultHashEncoding);
        Assert.Matches("^[0-9a-f]{128}$", RequestSigner.Sign("sec", "non"));
    }

    [Theory]
    [InlineData(HashEncoding.Hex, "^[0-9a-f]{128}$")]
    [InlineData(HashEncoding.HexUpper, "^[0-9A-F]{128}$")]
    [InlineData(HashEncoding.Base64, "^[A-Za-z0-9+/]+=*$")]
    [InlineData(HashEncoding.Base64Url, "^[A-Za-z0-9_-]+$")]
    public void SupportsEveryEncoding(HashEncoding encoding, string pattern)
    {
        Assert.Matches(new Regex(pattern), RequestSigner.Sign("sec", "non", encoding));
    }

    [Fact]
    public void UsesTheEncodingConfirmedAgainstTheLiveApi()
    {
        // The v4 specification states the construction, SHA512(API_SECRET + NONCE), but
        // not the digest encoding. Confirmed live on 2026-08-15: lowercase hex
        // authenticated against GET /v4/Balance on api.huuray.com, and the other three
        // candidate encodings returned 401. If this fails, someone changed the default —
        // which breaks every consumer unless the API changed first.
        Assert.Equal(HashEncoding.Hex, RequestSigner.DefaultHashEncoding);
    }
}

public class AuthHeaderTests
{
    [Fact]
    public void SendsExactlyTheThreeDocumentedHeaders()
    {
        IReadOnlyDictionary<string, string> headers = RequestSigner.BuildAuthHeaders("tok", "sec", "abc");

        List<string> names = new(headers.Keys);
        names.Sort(StringComparer.Ordinal);

        Assert.Equal(new[] { "X-API-HASH", "X-API-NONCE", "X-API-TOKEN" }, names);
        Assert.Equal("tok", headers["X-API-TOKEN"]);
        Assert.Equal("abc", headers["X-API-NONCE"]);
    }

    [Fact]
    public void NeverPutsTheSecretInAHeader()
    {
        IReadOnlyDictionary<string, string> headers =
            RequestSigner.BuildAuthHeaders("tok", "super-secret", "abc");

        foreach (KeyValuePair<string, string> header in headers)
        {
            Assert.DoesNotContain("super-secret", header.Value, StringComparison.Ordinal);
        }
    }
}
