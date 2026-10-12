using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

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
        string? userAgent = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        RecordingHandler handler = new(response ?? new MockResponse());
        return Build(handler, retry, baseUrl, hashEncoding, timeout, nonceFactory, userAgent, delay);
    }

    public static TestHarness ClientWithQueue(
        IEnumerable<MockResponse> responses,
        RetryOptions? retry = null,
        string? baseUrl = null,
        HashEncoding? hashEncoding = null,
        TimeSpan? timeout = null,
        Func<string>? nonceFactory = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        RecordingHandler handler = new(responses);
        return Build(handler, retry, baseUrl, hashEncoding, timeout, nonceFactory, userAgent: null, delay);
    }

    /// <summary>Parses a JSON literal for use as a canned response body.</summary>
    public static JsonNode Json(string json) => JsonNode.Parse(json)!;

    /// <summary>
    /// A stand-in purchase order file, fresh on every read. Not a real PDF: its bytes break any
    /// text decoding — NUL, 0xFF, a lone 0x80, a line break and a boundary-like <c>--</c> — so a
    /// test can tell that they arrived intact.
    /// </summary>
    public static byte[] PdfBytes => [.. "%PDF-1.7\n"u8, 0x00, 0xFF, 0x80, 0x0D, 0x0A, 0x2D, 0x2D, .. "\n%%EOF"u8];

    private static TestHarness Build(
        RecordingHandler handler,
        RetryOptions? retry,
        string? baseUrl,
        HashEncoding? hashEncoding,
        TimeSpan? timeout,
        Func<string>? nonceFactory,
        string? userAgent,
        Func<TimeSpan, CancellationToken, Task>? delay)
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

        // Without a wait of its own the client is built as a consumer builds it, on Task.Delay.
        HuurayClient client = delay is null
            ? new HuurayClient(options, httpClient)
            : new HuurayClient(options, httpClient, delay);

        return new TestHarness(client, handler);
    }
}
