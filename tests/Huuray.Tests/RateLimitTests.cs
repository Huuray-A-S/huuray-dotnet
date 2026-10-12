using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Huuray.Tests;

/// <summary>
/// Stands in for the client's wait between attempts, so no test sleeps and every wait is recorded.
/// </summary>
internal sealed class RecordedWaits
{
    /// <summary>Every wait asked for, in order.</summary>
    internal List<TimeSpan> Waits { get; } = new();

    internal Task DelayAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        Waits.Add(wait);
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }
}

internal static class RateLimitTestData
{
    internal const string TooManyRequests = "Too Many Requests";

    internal static MockResponse Balances => new() { Json = Fake.Json("{\"Balances\":[]}") };

    /// <summary>Two retries with a backoff of a few milliseconds, under the default 4 s ceiling.</summary>
    internal static RetryOptions TwoRetries => new() { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) };

    /// <summary>The documented 429: the minimal <c>Response</c> body, and a <c>Retry-After</c> header.</summary>
    internal static MockResponse TooMany(string? retryAfter) => new()
    {
        Status = 429,
        RetryAfter = retryAfter,
        Json = new JsonObject { ["Status"] = 429, ["Message"] = TooManyRequests, ["StatusMessage"] = TooManyRequests },
    };
}

public class RateLimitMappingTests
{
    [Fact]
    public async Task Maps429ToTheRateLimitException_WithRetryAfterInWholeSeconds()
    {
        TestHarness harness = Fake.Client(RateLimitTestData.TooMany("7"));

        Exception? error = await Record.ExceptionAsync(() => harness.Client.Balances.ListAsync());

        HuurayRateLimitException rateLimit = Assert.IsType<HuurayRateLimitException>(error);
        Assert.IsAssignableFrom<HuurayApiException>(rateLimit);
        Assert.Equal(TimeSpan.FromSeconds(7), rateLimit.RetryAfter);
        Assert.Equal(429, rateLimit.HttpStatus);
        Assert.Equal(429, rateLimit.Status);
        Assert.Equal(RateLimitTestData.TooManyRequests, rateLimit.StatusMessage);
        Assert.Equal("GET", rateLimit.Method);
        Assert.Equal("/v4/Balance", rateLimit.Path);
        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task Maps429WithoutRetryAfter_ToNull()
    {
        TestHarness harness = Fake.Client(RateLimitTestData.TooMany(retryAfter: null));

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Balances.ListAsync());

        Assert.Null(error.RetryAfter);
        Assert.Equal(429, error.HttpStatus);
    }

    [Fact]
    public async Task CarriesAZeroRetryAfterAsGiven_TheOneSecondFloorIsForTheWait()
    {
        TestHarness harness = Fake.Client(RateLimitTestData.TooMany("0"));

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Balances.ListAsync());

        Assert.Equal(TimeSpan.Zero, error.RetryAfter);
    }

    [Theory]
    // The rules of the PDF's Retry-After: whole seconds, or null.
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("30s")]
    [InlineData("99999999999")]
    [InlineData("Fri, 01 Jan 2100 00:00:00 GMT")]
    [InlineData("Sat, 26 Sep 2026 12:00:00 GMT")]
    public async Task ReadsARetryAfterThatIsNotWholeSecondsAsNull(string retryAfter)
    {
        TestHarness harness = Fake.Client(RateLimitTestData.TooMany(retryAfter));

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Balances.ListAsync());

        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public void ThePublicFactoryMaps429Too_WithoutARetryAfter()
    {
        HuurayApiException error = HuurayApiException.Create(
            429,
            Fake.Json("{\"Status\":429,\"StatusMessage\":\"Too Many Requests\"}"),
            "GET",
            "/v4/Balance");

        HuurayRateLimitException rateLimit = Assert.IsType<HuurayRateLimitException>(error);
        Assert.Null(rateLimit.RetryAfter);
        Assert.Equal(RateLimitTestData.TooManyRequests, rateLimit.StatusMessage);
    }

    [Fact]
    public async Task KeepsOnlyTheRedactedBody_LikeEveryApiException()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 429,
            RetryAfter = "1",
            Json = Fake.Json("{\"Status\":429,\"Code\":\"LEAKED-CODE\"}"),
        });

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Balances.ListAsync());

        Assert.DoesNotContain("LEAKED-CODE", error.Body!.ToJsonString(), StringComparison.Ordinal);
    }
}

public class RateLimitedReadTests
{
    [Fact]
    public async Task WaitsRetryAfterInPlaceOfTheBackoff_ThenAsksAgainWithAFreshNonce()
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("2"), RateLimitTestData.Balances },
            retry: RateLimitTestData.TwoRetries,
            delay: waits.DelayAsync);

        ListBalancesResult result = await harness.Client.Balances.ListAsync();

        Assert.Empty(result.Balances);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, waits.Waits);
        Assert.Equal(2, harness.Calls.Count);
        Assert.NotEqual(harness.Calls[0].Headers["X-API-NONCE"], harness.Calls[1].Headers["X-API-NONCE"]);
        Assert.NotEqual(harness.Calls[0].Headers["X-API-HASH"], harness.Calls[1].Headers["X-API-HASH"]);
    }

    [Theory]
    // Every operation the client retries is a read, and each one waits the same way.
    [InlineData("balances")]
    [InlineData("catalogue")]
    [InlineData("templates")]
    [InlineData("stock")]
    [InlineData("rates")]
    [InlineData("search")]
    [InlineData("pdf")]
    [InlineData("retryableRequest")]
    public async Task EveryReadWaitsRetryAfter(string read)
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("3"), new MockResponse { Json = Fake.Json("{}") } },
            retry: RateLimitTestData.TwoRetries,
            delay: waits.DelayAsync);
        HuurayClient client = harness.Client;

        Task reading = read switch
        {
            "balances" => client.Balances.ListAsync(),
            "catalogue" => client.Catalogue.ListAsync(),
            "templates" => client.Templates.ListAsync(),
            "stock" => client.Stock.CheckAsync(new CheckStockRequest { ProductToken = "tok" }),
            "rates" => client.ExchangeRates.GetAsync("DKK", "EUR"),
            "search" => client.Orders.SearchAsync(new SearchOrdersRequest { RefId = "ref-1" }),
            "pdf" => client.Pdfs.GetAsync(PdfsTestData.Request),
            "retryableRequest" => client.RequestAsync(HttpMethod.Get, "/v4/Balance", retryable: true),
            _ => throw new ArgumentOutOfRangeException(nameof(read), read, null),
        };

        await reading;

        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, waits.Waits);
        Assert.Equal(2, harness.Calls.Count);
    }

    [Fact]
    public async Task WaitsAtLeastOneSecond_WhenTheApiAsksForNoWait()
    {
        // Retry-After: 0 would otherwise set off back-to-back signed requests.
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("0"), RateLimitTestData.Balances },
            retry: RateLimitTestData.TwoRetries,
            delay: waits.DelayAsync);

        await harness.Client.Balances.ListAsync();

        Assert.Equal(new[] { TimeSpan.FromSeconds(1) }, waits.Waits);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("soon")]
    [InlineData("Fri, 01 Jan 2100 00:00:00 GMT")]
    public async Task BacksOffAsUsual_WhenA429CarriesNoUsableRetryAfter(string? retryAfter)
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany(retryAfter), RateLimitTestData.Balances },
            retry: new RetryOptions
            {
                MaxRetries = 2,
                BaseDelay = TimeSpan.FromMilliseconds(10),
                MaxDelay = TimeSpan.FromMilliseconds(10),
            },
            delay: waits.DelayAsync);

        await harness.Client.Balances.ListAsync();

        TimeSpan wait = Assert.Single(waits.Waits);
        Assert.InRange(wait, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
        Assert.Equal(2, harness.Calls.Count);
    }

    [Fact]
    public async Task TakesRetryAfterForTheNextAttemptOnly()
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("3"), new MockResponse { Status = 503 }, RateLimitTestData.Balances },
            retry: RateLimitTestData.TwoRetries,
            delay: waits.DelayAsync);

        await harness.Client.Balances.ListAsync();

        Assert.Equal(2, waits.Waits.Count);
        Assert.Equal(TimeSpan.FromSeconds(3), waits.Waits[0]);
        Assert.InRange(waits.Waits[1], TimeSpan.Zero, TimeSpan.FromMilliseconds(2));
    }

    [Fact]
    public async Task ReadsRetryAfterOnA429Only_TheSpecDocumentsItNowhereElseOnAnError()
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { new MockResponse { Status = 503, RetryAfter = "3" }, RateLimitTestData.Balances },
            retry: RateLimitTestData.TwoRetries,
            delay: waits.DelayAsync);

        await harness.Client.Balances.ListAsync();

        Assert.InRange(Assert.Single(waits.Waits), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ThrowsAtOnce_WhenRetryAfterIsLongerThanMaxDelay_SoTheCallerDecides()
    {
        // 5 s against the default 4 s ceiling: no wait, and no second request.
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("5") },
            retry: RateLimitTestData.TwoRetries,
            delay: waits.DelayAsync);

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Balances.ListAsync());

        Assert.Equal(TimeSpan.FromSeconds(5), error.RetryAfter);
        Assert.Empty(waits.Waits);
        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task WaitsARetryAfterThatEqualsMaxDelay()
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("4"), RateLimitTestData.Balances },
            retry: RateLimitTestData.TwoRetries,
            delay: waits.DelayAsync);

        await harness.Client.Balances.ListAsync();

        Assert.Equal(new[] { TimeSpan.FromSeconds(4) }, waits.Waits);
    }

    [Fact]
    public async Task WaitsALongerRetryAfter_UnderALongerMaxDelay()
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("30"), RateLimitTestData.Balances },
            retry: new RetryOptions { MaxRetries = 1, MaxDelay = TimeSpan.FromSeconds(60) },
            delay: waits.DelayAsync);

        await harness.Client.Balances.ListAsync();

        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, waits.Waits);
    }

    [Fact]
    public async Task MeasuresTheWaitItWouldTake_SoTheFloorCountsAgainstMaxDelay()
    {
        // Retry-After: 0 is waited as 1 s, which a 500 ms ceiling does not allow.
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("0") },
            retry: new RetryOptions { MaxRetries = 2, MaxDelay = TimeSpan.FromMilliseconds(500) },
            delay: waits.DelayAsync);

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Balances.ListAsync());

        Assert.Equal(TimeSpan.Zero, error.RetryAfter);
        Assert.Empty(waits.Waits);
        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task ThrowsAtOnce_OnTheLargestRetryAfterTheHeaderCanCarry()
    {
        // int.MaxValue seconds: no overflow, no wait.
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("2147483647") },
            retry: new RetryOptions { MaxRetries = 2, MaxDelay = TimeSpan.FromMilliseconds(4_294_967_294) },
            delay: waits.DelayAsync);

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Balances.ListAsync());

        Assert.Equal(TimeSpan.FromSeconds(int.MaxValue), error.RetryAfter);
        Assert.Empty(waits.Waits);
    }

    [Fact]
    public async Task ThrowsTheRateLimitException_WhenTheRetriesAreUsedUp()
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("1"), RateLimitTestData.TooMany("2"), RateLimitTestData.TooMany("3") },
            retry: RateLimitTestData.TwoRetries,
            delay: waits.DelayAsync);

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Balances.ListAsync());

        // The last answer's Retry-After is carried, not waited: there is no attempt left.
        Assert.Equal(TimeSpan.FromSeconds(3), error.RetryAfter);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, waits.Waits);
        Assert.Equal(3, harness.Calls.Count);
        Assert.Equal(3, harness.Calls.Select(c => c.Headers["X-API-NONCE"]).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ThrowsOnTheFirst429_WhenRetriesAreOff()
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("1") },
            retry: RetryOptions.None,
            delay: waits.DelayAsync);

        await Assert.ThrowsAsync<HuurayRateLimitException>(() => harness.Client.Balances.ListAsync());

        Assert.Empty(waits.Waits);
        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task CancellingDuringTheWaitIsPlainCancellation()
    {
        using CancellationTokenSource cts = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("2") },
            retry: RateLimitTestData.TwoRetries,
            delay: (wait, cancellationToken) =>
            {
                cts.Cancel();
                return Task.Delay(Timeout.Infinite, cancellationToken);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Client.Balances.ListAsync(cts.Token));

        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task WaitsForRealThroughThePublicConstructor()
    {
        // A zero Retry-After, so the real wait is the 1 s floor: the one test here that sleeps.
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("0"), RateLimitTestData.Balances },
            retry: RateLimitTestData.TwoRetries);
        Stopwatch stopwatch = Stopwatch.StartNew();

        await harness.Client.Balances.ListAsync();

        Assert.Equal(2, harness.Calls.Count);

        // A little under 1 s, for the timer's granularity.
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(900), $"Waited only {stopwatch.Elapsed}.");
    }
}

public class RateLimitedWriteTests
{
    private static SendRewardRequest Reward => new()
    {
        ProductToken = "tok",
        Value = 5000,
        Currency = "DKK",
        Recipient = new Recipient { Name = "Jane", Email = "jane@example.com" },
        TemplateId = 42,
        RefId = "ref-429",
    };

    [Theory]
    // The spec says a 429 was not processed, so no write is retried for it and an order's
    // outcome is known: never HuurayIndeterminateOrderException.
    [InlineData("create")]
    [InlineData("createSync")]
    [InlineData("sendReward")]
    [InlineData("clientSendReward")]
    [InlineData("resend")]
    [InlineData("cancel")]
    [InlineData("upload")]
    [InlineData("request")]
    public async Task AWriteThrowsTheRateLimitExceptionOnTheFirst429_AndIsNeverRetried(string write)
    {
        RecordedWaits waits = new();
        TestHarness harness = Fake.ClientWithQueue(
            new[] { RateLimitTestData.TooMany("1") },
            retry: new RetryOptions { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) },
            delay: waits.DelayAsync);
        HuurayClient client = harness.Client;
        CreateOrderRequest order = OrdersTestData.Base with { RefId = "ref-429" };

        Exception? error = await Record.ExceptionAsync(() => write switch
        {
            "create" => client.Orders.CreateAsync(order),
            "createSync" => client.Orders.CreateSyncAsync(order),
            "sendReward" => client.Orders.SendRewardAsync(Reward),
            "clientSendReward" => client.SendRewardAsync(Reward),
            "resend" => client.Orders.ResendAsync(new ResendRequest { OrderUid = "uid" }),
            "cancel" => client.Orders.CancelAsync(new CancelRequest { OrderUid = "uid" }),
            "upload" => client.Uploads.CreateAsync(UploadsTestData.Pdf),
            "request" => client.RequestAsync(HttpMethod.Post, "/v4/Order", new JsonObject { ["Sync"] = false }),
            _ => throw new ArgumentOutOfRangeException(nameof(write), write, null),
        });

        HuurayRateLimitException rateLimit = Assert.IsType<HuurayRateLimitException>(error);
        Assert.Equal(TimeSpan.FromSeconds(1), rateLimit.RetryAfter);
        Assert.Equal(1, harness.Handler.Invocations);
        Assert.Empty(waits.Waits);
    }

    [Fact]
    public async Task ARateLimitedUploadIsNotSaidToBeStored()
    {
        TestHarness harness = Fake.Client(RateLimitTestData.TooMany("1"));

        HuurayRateLimitException error = await Assert.ThrowsAsync<HuurayRateLimitException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        Assert.DoesNotContain("may still have been stored", error.Message, StringComparison.Ordinal);
    }
}
