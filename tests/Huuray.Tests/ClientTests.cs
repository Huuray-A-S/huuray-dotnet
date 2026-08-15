using System;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Huuray.Tests;

public class ConstructionTests
{
    [Fact]
    public void RequiresAnApiToken()
    {
        Assert.Throws<HuurayConfigurationException>(() =>
            new HuurayClient(new HuurayClientOptions { ApiToken = string.Empty, ApiSecret = "s" }));
    }

    [Fact]
    public void RequiresAnApiSecret()
    {
        Assert.Throws<HuurayConfigurationException>(() =>
            new HuurayClient(new HuurayClientOptions { ApiToken = "t", ApiSecret = string.Empty }));
    }

    [Theory]
    // "/v4" is the case that differs by platform: not absolute on Windows, but a
    // valid file:// URI on Linux and macOS. Validating the scheme makes the
    // behaviour identical everywhere.
    [InlineData("/v4")]
    [InlineData("v4")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.test")]
    public void RejectsABaseUrlThatIsNotAbsoluteHttp(string baseUrl)
    {
        Assert.Throws<HuurayConfigurationException>(() =>
            new HuurayClient(new HuurayClientOptions { ApiToken = "t", ApiSecret = "s", BaseUrl = baseUrl }));
    }

    [Theory]
    [InlineData("https://api.huuray.com")]
    [InlineData("http://localhost:8080")]
    public void AcceptsAnAbsoluteHttpBaseUrl(string baseUrl)
    {
        HuurayClient client = new(new HuurayClientOptions
        {
            ApiToken = "t",
            ApiSecret = "s",
            BaseUrl = baseUrl,
        });

        Assert.NotNull(client);
    }

    [Fact]
    public async Task DefaultsToTheProductionHost()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"Balances\":[]}") });

        await harness.Client.Balances.ListAsync();

        // Pins the actual origin, not just the path — a typo in DefaultBaseUrl must not
        // ship green.
        Assert.Equal("https://api.huuray.com", harness.First.Origin);
        Assert.Equal("/v4/Balance", harness.First.Path);
    }

    [Fact]
    public async Task AcceptsABaseUrlWithATrailingSlash()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"Balances\":[]}") },
            baseUrl: "https://example.test/");

        await harness.Client.Balances.ListAsync();

        Assert.Equal("https://example.test", harness.First.Origin);
        Assert.Equal("/v4/Balance", harness.First.Path);
    }
}

public class SigningPerRequestTests
{
    [Fact]
    public async Task SendsTheThreeAuthHeadersOnEveryCall()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{}") });

        await harness.Client.Balances.ListAsync();
        await harness.Client.Templates.ListAsync();

        Assert.Equal(2, harness.Calls.Count);
        foreach (CapturedRequest call in harness.Calls)
        {
            Assert.Equal(Fake.ApiToken, call.Headers["X-API-TOKEN"]);
            Assert.False(string.IsNullOrEmpty(call.Headers["X-API-NONCE"]));
            Assert.Matches("^[0-9a-f]{128}$", call.Headers["X-API-HASH"]);
        }
    }

    [Fact]
    public async Task UsesAFreshNonceForEveryRequest()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{}") });

        await harness.Client.Balances.ListAsync();
        await harness.Client.Balances.ListAsync();
        await harness.Client.Balances.ListAsync();

        string a = harness.Calls[0].Headers["X-API-NONCE"];
        string b = harness.Calls[1].Headers["X-API-NONCE"];
        string c = harness.Calls[2].Headers["X-API-NONCE"];

        Assert.NotEqual(a, b);
        Assert.NotEqual(b, c);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public async Task NeverSendsTheSecret()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{}") });

        await harness.Client.Balances.ListAsync();

        foreach (System.Collections.Generic.KeyValuePair<string, string> header in harness.First.Headers)
        {
            Assert.DoesNotContain(Fake.ApiSecret, header.Value, StringComparison.Ordinal);
        }

        Assert.Null(harness.First.Body);
    }

    [Fact]
    public async Task HonoursAHashEncodingOverride()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{}") },
            hashEncoding: HashEncoding.Base64);

        await harness.Client.Balances.ListAsync();

        Assert.DoesNotMatch("^[0-9a-f]{128}$", harness.First.Headers["X-API-HASH"]);
    }

    [Fact]
    public async Task SendsAUserAgentNamingThisSdk()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{}") }, userAgent: "my-app/2.0");

        await harness.Client.Balances.ListAsync();

        Assert.StartsWith("huuray-dotnet/", harness.First.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.EndsWith("my-app/2.0", harness.First.Headers["User-Agent"], StringComparison.Ordinal);
    }
}

public class ErrorMappingTests
{
    [Theory]
    [InlineData(401, typeof(HuurayAuthException))]
    [InlineData(403, typeof(HuurayAuthException))]
    [InlineData(404, typeof(HuurayNotFoundException))]
    [InlineData(422, typeof(HuurayValidationException))]
    [InlineData(500, typeof(HuurayServerException))]
    [InlineData(400, typeof(HuurayApiException))]
    public async Task MapsEachHttpStatusToTheRightExceptionType(int status, Type expected)
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = status,
            Json = Fake.Json($"{{\"Status\":{status},\"StatusMessage\":\"nope\"}}"),
        });

        Exception? error = await Record.ExceptionAsync(() => harness.Client.Balances.ListAsync());

        Assert.NotNull(error);
        Assert.IsType(expected, error);
    }

    [Fact]
    public async Task PrefersStatusMessageOverTheDeprecatedMessageField()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 400,
            Json = Fake.Json("{\"Status\":400,\"Message\":\"old text\",\"StatusMessage\":\"new text\"}"),
        });

        HuurayApiException error =
            await Assert.ThrowsAsync<HuurayApiException>(() => harness.Client.Balances.ListAsync());

        Assert.Equal("new text", error.StatusMessage);
    }

    [Fact]
    public async Task FallsBackToMessageWhenStatusMessageIsAbsent()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 400,
            Json = Fake.Json("{\"Status\":400,\"Message\":\"old text\"}"),
        });

        HuurayApiException error =
            await Assert.ThrowsAsync<HuurayApiException>(() => harness.Client.Balances.ListAsync());

        Assert.Equal("old text", error.StatusMessage);
    }

    [Fact]
    public async Task ExposesTheHttpStatusAndTheParsedBody()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 422,
            Json = Fake.Json("{\"Status\":422,\"StatusMessage\":\"bad\"}"),
        });

        HuurayValidationException error =
            await Assert.ThrowsAsync<HuurayValidationException>(() => harness.Client.Balances.ListAsync());

        Assert.Equal(422, error.HttpStatus);
        Assert.Equal(422, error.Status);
        Assert.Equal("GET", error.Method);
        Assert.Equal("/v4/Balance", error.Path);
    }

    [Fact]
    public async Task RedactsBearerAndContactFieldsFromTheRetainedErrorBody()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 400,
            Json = Fake.Json(
                "{\"Status\":400,\"StatusMessage\":\"bad\",\"Code\":\"LEAKED-CODE\",\"Email\":\"jane@example.com\"}"),
        });

        HuurayApiException error =
            await Assert.ThrowsAsync<HuurayApiException>(() => harness.Client.Balances.ListAsync());

        string dumped = error.Body!.ToJsonString();

        Assert.DoesNotContain("LEAKED-CODE", dumped, StringComparison.Ordinal);
        Assert.DoesNotContain("jane@example.com", dumped, StringComparison.Ordinal);
    }
}

public class RetryPolicyTests
{
    [Fact]
    public async Task RetriesAReadOn503()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[]
            {
                new MockResponse { Status = 503 },
                new MockResponse { Status = 200, Json = Fake.Json("{\"Balances\":[]}") },
            },
            retry: new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await harness.Client.Balances.ListAsync();

        Assert.Equal(2, harness.Calls.Count);
    }

    [Fact]
    public async Task NeverRetriesAnOrder_EvenOn503()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Status = 503 },
            retry: new RetryOptions { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await Record.ExceptionAsync(() => harness.Client.Orders.CreateAsync(new CreateOrderRequest
        {
            ProductToken = "t",
            Value = 100,
            Currency = "DKK",
            Quantity = 1,
        }));

        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task NeverRetriesAResend_ItWouldRedeliverRealValue()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Status = 503 },
            retry: new RetryOptions { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await Record.ExceptionAsync(() =>
            harness.Client.Orders.ResendAsync(new ResendRequest { OrderUid = "x" }));

        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task NeverRetriesACancel()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Status = 503 },
            retry: new RetryOptions { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await Record.ExceptionAsync(() =>
            harness.Client.Orders.CancelAsync(new CancelRequest { OrderUid = "x" }));

        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task DoesNotRetryA400_TheRequestIsWrongAndRepeatingWillNotHelp()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Status = 400 },
            retry: new RetryOptions { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await Record.ExceptionAsync(() => harness.Client.Balances.ListAsync());

        Assert.Single(harness.Calls);
    }

    [Fact]
    public void TreatsAPartiallyPopulatedRetryOptionsAsDefaults_NotAsAClobber()
    {
        // `new RetryOptions { BaseDelay = ... }` is the natural result of threading
        // optional configuration. MaxRetries must fall back to the default rather than
        // silently becoming zero.
        RetryPolicy policy = RetryPolicy.Resolve(new RetryOptions { BaseDelay = TimeSpan.FromMilliseconds(5) });

        Assert.Equal(RetryOptions.Default.MaxRetries!.Value, policy.MaxRetries);
        Assert.Equal(TimeSpan.FromMilliseconds(5), policy.BaseDelay);
        Assert.Equal(RetryOptions.Default.MaxDelay!.Value, policy.MaxDelay);
    }

    [Fact]
    public void ClampsANegativeMaxRetriesToZeroInsteadOfNeverSending()
    {
        RetryPolicy policy = RetryPolicy.Resolve(new RetryOptions { MaxRetries = -3 });

        Assert.Equal(0, policy.MaxRetries);
    }

    [Fact]
    public async Task ANegativeMaxRetriesStillSendsExactlyOnce()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"Balances\":[]}") },
            retry: new RetryOptions { MaxRetries = -3 });

        await harness.Client.Balances.ListAsync();

        Assert.Single(harness.Calls);
    }

    [Theory]
    [InlineData(408, true)]
    [InlineData(425, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    [InlineData(422, false)]
    public void KnowsWhichStatusesAreWorthRepeatingAReadFor(int status, bool retryable)
    {
        Assert.Equal(retryable, RetryPolicy.IsRetryableStatus(status));
    }
}

public class TransportFaultTests
{
    [Fact]
    public async Task MapsAConnectionFailureBeforeHeadersIntoTheTaxonomy()
    {
        TestHarness harness = Fake.Client(new MockResponse { Throws = new HttpRequestException("socket hang up") });

        HuurayConnectionException error =
            await Assert.ThrowsAsync<HuurayConnectionException>(() => harness.Client.Balances.ListAsync());

        Assert.Equal("GET", error.Method);
        Assert.Equal("/v4/Balance", error.Path);
    }

    [Fact]
    public async Task MapsAMidBodyConnectionDropIntoTheTaxonomy_NotARawIoException()
    {
        TestHarness harness = Fake.Client(new MockResponse { BodyThrows = new IOException("terminated") });

        Exception? error = await Record.ExceptionAsync(() => harness.Client.Balances.ListAsync());

        Assert.NotNull(error);
        Assert.IsAssignableFrom<HuurayConnectionException>(error);
    }

    [Fact]
    public async Task MapsARequestThatNeverAnswersToATimeout()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Hangs = true },
            timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<HuurayTimeoutException>(() => harness.Client.Balances.ListAsync());
    }

    [Fact]
    public async Task MapsAMidBodyStallToATimeout()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { BodyHangs = true },
            timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<HuurayTimeoutException>(() => harness.Client.Balances.ListAsync());
    }

    [Fact]
    public async Task TreatsAGarbled200BodyAsATransportFault_NeverAsAnEmptyResult()
    {
        // An empty result from a garbled /v4/Search response would tell the
        // reconciliation flow "the order did not land" — inviting a double order.
        TestHarness harness = Fake.Client(new MockResponse { Status = 200, Text = "<html>gateway error</html>" });

        await Assert.ThrowsAsync<HuurayConnectionException>(() =>
            harness.Client.Orders.SearchAsync(new SearchOrdersRequest { RefId = "r" }));
    }

    [Fact]
    public async Task TreatsAnEmpty200BodyTheSameWay()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 200, Text = string.Empty });

        HuurayConnectionException error =
            await Assert.ThrowsAsync<HuurayConnectionException>(() => harness.Client.Balances.ListAsync());

        Assert.Contains("empty", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown rather than empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NeverPutsTheResponseBodyIntoATransportFaultMessage()
    {
        // A truncated body can still contain a voucher code.
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 200,
            Text = "{\"Vouchers\":[{\"Code\":\"LEAKED-CODE\"",
        });

        HuurayConnectionException error =
            await Assert.ThrowsAsync<HuurayConnectionException>(() => harness.Client.Balances.ListAsync());

        Assert.DoesNotContain("LEAKED-CODE", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetriesARetryableReadAfterAGarbledBody()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[]
            {
                new MockResponse { Status = 200, Text = "not json" },
                new MockResponse { Status = 200, Json = Fake.Json("{\"Balances\":[]}") },
            },
            retry: new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1) });

        ListBalancesResult result = await harness.Client.Balances.ListAsync();

        Assert.Empty(result.Balances);
        Assert.Equal(2, harness.Calls.Count);
    }
}

public class EscapeHatchTests
{
    [Fact]
    public async Task RequestAsyncCallsAnyEndpointWithSigningHandled()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"abc\"}") });

        JsonNode? result = await harness.Client.RequestAsync(
            HttpMethod.Post,
            "/v4/Search",
            new JsonObject { ["RefID"] = "payroll-2026-08-jane" });

        Assert.Equal("abc", result!["OrderUID"]!.GetValue<string>());
        Assert.Equal("{\"RefID\":\"payroll-2026-08-jane\"}", harness.First.Body);
        Assert.False(string.IsNullOrEmpty(harness.First.Headers["X-API-HASH"]));
    }

    [Fact]
    public async Task RequestAsyncDoesNotRetryByDefault()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Status = 503 },
            retry: new RetryOptions { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) });

        await Record.ExceptionAsync(() => harness.Client.RequestAsync(HttpMethod.Get, "/v4/Balance"));

        Assert.Single(harness.Calls);
    }
}
