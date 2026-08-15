using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Huuray.Tests;

/// <summary>
/// A client wired to a recording transport, with throwaway credentials.
/// </summary>
public sealed class TestHarness
{
    public TestHarness(HuurayClient client, RecordingHandler handler)
    {
        Client = client;
        Handler = handler;
    }

    public HuurayClient Client { get; }

    public RecordingHandler Handler { get; }

    public IReadOnlyList<CapturedRequest> Calls => Handler.Calls;

    public CapturedRequest First => Handler.Calls[0];
}

/// <summary>
/// Builds clients that never touch the network.
/// </summary>
public static class Fake
{
    /// <summary>The credentials every test uses. They are not real, and never were.</summary>
    public const string ApiToken = "test-token";

    /// <summary>The secret every test signs with. Not real.</summary>
    public const string ApiSecret = "test-secret";

    public static TestHarness Client(
        MockResponse? response = null,
        RetryOptions? retry = null,
        string? baseUrl = null,
        HashEncoding? hashEncoding = null,
        TimeSpan? timeout = null,
        Func<string>? nonceFactory = null,
        string? userAgent = null)
    {
        RecordingHandler handler = new(response ?? new MockResponse());
        return Build(handler, retry, baseUrl, hashEncoding, timeout, nonceFactory, userAgent);
    }

    public static TestHarness ClientWithQueue(
        IEnumerable<MockResponse> responses,
        RetryOptions? retry = null,
        string? baseUrl = null,
        HashEncoding? hashEncoding = null,
        TimeSpan? timeout = null,
        Func<string>? nonceFactory = null)
    {
        RecordingHandler handler = new(responses);
        return Build(handler, retry, baseUrl, hashEncoding, timeout, nonceFactory, userAgent: null);
    }

    /// <summary>Parses a JSON literal for use as a canned response body.</summary>
    public static JsonNode Json(string json) => JsonNode.Parse(json)!;

    private static TestHarness Build(
        RecordingHandler handler,
        RetryOptions? retry,
        string? baseUrl,
        HashEncoding? hashEncoding,
        TimeSpan? timeout,
        Func<string>? nonceFactory,
        string? userAgent)
    {
        HttpClient httpClient = new(handler, disposeHandler: false)
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        HuurayClientOptions options = new()
        {
            ApiToken = ApiToken,
            ApiSecret = ApiSecret,
            BaseUrl = baseUrl,
            Retry = retry ?? RetryOptions.None,
            HashEncoding = hashEncoding ?? RequestSigner.DefaultHashEncoding,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
            NonceFactory = nonceFactory,
            UserAgent = userAgent,
        };

        return new TestHarness(new HuurayClient(options, httpClient), handler);
    }
}
