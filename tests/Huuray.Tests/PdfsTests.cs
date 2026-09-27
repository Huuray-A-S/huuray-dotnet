using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Huuray.Tests;

internal static class PdfsTestData
{
    internal const string OrderUid = "0f8a3c52-1d6e-4b7a-9c2f-5e4d3b2a1c90";

    internal const string TemplateUid = "c7d1e2f3-4a5b-4c6d-8e9f-0a1b2c3d4e5f";

    internal const string StillProcessing = "The order is still being processed, retry in 30 seconds";

    /// <summary>A second stand-in PDF, fresh on every read, whose base64 starts with <c>+/+/</c>.</summary>
    internal static byte[] OtherPdfBytes => [0xFB, 0xFF, 0xBF, .. "%PDF-1.7\n"u8, 0x00, .. "\n%%EOF"u8];

    internal static GetPdfRequest Request => new() { OrderUid = OrderUid };

    internal static JsonObject Document(int[] voucherIds, string? templateUid, string fileName, byte[] content) => new()
    {
        ["VoucherIDs"] = new JsonArray(voucherIds.Select(id => (JsonNode?)id).ToArray()),
        ["PDFTemplateUid"] = templateUid,
        ["FileName"] = fileName,
        ["ContentType"] = "application/pdf",
        ["Content"] = Convert.ToBase64String(content),
    };

    internal static MockResponse Ready(params JsonObject[] documents) => new()
    {
        Status = 200,
        Json = new JsonObject
        {
            ["OrderUID"] = OrderUid,
            ["Documents"] = new JsonArray(documents.Select(d => (JsonNode?)d).ToArray()),
            ["Status"] = 200,
            ["Message"] = "OK",
            ["StatusMessage"] = "OK",
        },
    };

    internal static MockResponse ReadyOne => Ready(Document(new[] { 5123401 }, TemplateUid, "giftcard-5123401.pdf", Fake.PdfBytes));

    internal static MockResponse NotReady(string? retryAfter = "30", string? statusMessage = StillProcessing) => new()
    {
        Status = 202,
        RetryAfter = retryAfter,
        Json = new JsonObject
        {
            ["OrderUID"] = OrderUid,
            ["Documents"] = new JsonArray(),
            ["Status"] = 202,
            ["Message"] = "OK, order still processing",
            ["StatusMessage"] = statusMessage,
        },
    };

    internal static MockResponse Error(int status, string message, string statusMessage) => new()
    {
        Status = status,
        Json = new JsonObject
        {
            ["OrderUID"] = OrderUid,
            ["Documents"] = new JsonArray(),
            ["Status"] = status,
            ["Message"] = message,
            ["StatusMessage"] = statusMessage,
        },
    };

    internal static RetryOptions EagerRetries => new() { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) };
}

/// <summary>
/// A clock that moves only when <see cref="PdfsResource.GetWhenReadyAsync"/> waits, so no test
/// sleeps and every wait is recorded.
/// </summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks;

    /// <summary>Every wait asked for, in order.</summary>
    internal List<TimeSpan> Waits { get; } = new();

    /// <summary>Added to the clock with every wait: the time the following request takes.</summary>
    internal TimeSpan RequestTime { get; init; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    internal Task DelayAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        Waits.Add(wait);
        _ticks += (wait + RequestTime).Ticks;
        return cancellationToken.IsCancellationRequested ? Task.FromCanceled(cancellationToken) : Task.CompletedTask;
    }

    internal PdfsResource For(TestHarness harness) => new(harness.Client, this, DelayAsync);
}

public class PdfRequestShapeTests
{
    [Fact]
    public async Task SendsOnlyTheOrderUid_WhenNothingElseIsGiven()
    {
        TestHarness harness = Fake.Client(PdfsTestData.ReadyOne);

        await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        CapturedRequest call = Assert.Single(harness.Calls);
        Assert.Equal("POST", call.Method);
        Assert.Equal("/v4/Pdf", call.Path);
        Assert.Empty(call.Query);
        Assert.Equal(Fake.ApiToken, call.Headers["X-API-TOKEN"]);
        Assert.False(string.IsNullOrEmpty(call.Headers["X-API-NONCE"]));
        Assert.Matches("^[0-9a-f]{128}$", call.Headers["X-API-HASH"]);
        Assert.Equal("application/json", call.MediaType);
        Assert.Equal("{\"OrderUID\":\"" + PdfsTestData.OrderUid + "\"}", call.Body);
    }

    [Fact]
    public async Task SendsEveryField_SpelledAsTheSpecSpellsIt()
    {
        TestHarness harness = Fake.Client(PdfsTestData.ReadyOne);

        await harness.Client.Pdfs.GetAsync(new GetPdfRequest
        {
            OrderUid = PdfsTestData.OrderUid,
            VoucherId = 5123402,
            PdfTemplateUid = PdfsTestData.TemplateUid,
            Combine = true,
        });

        Assert.Equal(
            "{\"OrderUID\":\"" + PdfsTestData.OrderUid + "\",\"VoucherID\":5123402,\"PDFTemplateUid\":\"" +
            PdfsTestData.TemplateUid + "\",\"Combine\":true}",
            harness.First.Body);
    }

    [Fact]
    public async Task SendsCombineFalse_WhenItIsSetToFalse()
    {
        TestHarness harness = Fake.Client(PdfsTestData.ReadyOne);

        await harness.Client.Pdfs.GetAsync(PdfsTestData.Request with { Combine = false });

        Assert.Equal("{\"OrderUID\":\"" + PdfsTestData.OrderUid + "\",\"Combine\":false}", harness.First.Body);
    }

    [Fact]
    public async Task SendsEveryValueAsGiven_TheApiDecidesWhatItAccepts()
    {
        // No GUID check and no receiver count: an order with more than three receivers is the
        // API's 422 to give.
        TestHarness harness = Fake.Client(PdfsTestData.ReadyOne);

        await harness.Client.Pdfs.GetAsync(new GetPdfRequest
        {
            OrderUid = "  not-a-guid  ",
            VoucherId = -1,
            PdfTemplateUid = "not-a-guid-either",
        });

        Assert.Equal(1, harness.Handler.Invocations);
        Assert.Equal(
            "{\"OrderUID\":\"  not-a-guid  \",\"VoucherID\":-1,\"PDFTemplateUid\":\"not-a-guid-either\"}",
            harness.First.Body);
    }

    [Fact]
    public async Task RejectsANullRequest_OnBothMethods()
    {
        TestHarness harness = Fake.Client(PdfsTestData.ReadyOne);

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.Pdfs.GetAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.Pdfs.GetWhenReadyAsync(null!));
        Assert.Equal(0, harness.Handler.Invocations);
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenSendsNothing_AndIsPlainCancellation()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        TestHarness harness = Fake.Client(PdfsTestData.ReadyOne);

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Client.Pdfs.GetAsync(PdfsTestData.Request, cts.Token));

        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.Equal(0, harness.Handler.Invocations);
    }
}

public class PdfResultTests
{
    [Fact]
    public async Task Maps200_DecodingEachDocumentToItsExactBytes()
    {
        TestHarness harness = Fake.Client(PdfsTestData.Ready(
            PdfsTestData.Document(new[] { 5123401 }, PdfsTestData.TemplateUid, "giftcard-5123401.pdf", Fake.PdfBytes),
            PdfsTestData.Document(new[] { 5123402 }, PdfsTestData.TemplateUid, "giftcard-5123402.pdf", PdfsTestData.OtherPdfBytes)));

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.True(result.Ready);
        Assert.Equal(PdfsTestData.OrderUid, result.OrderUid);
        Assert.Null(result.RetryAfter);
        Assert.Equal(2, result.Documents.Count);

        PdfDocument first = result.Documents[0];
        Assert.Equal(new[] { 5123401 }, first.VoucherIds);
        Assert.Equal(PdfsTestData.TemplateUid, first.PdfTemplateUid);
        Assert.Equal("giftcard-5123401.pdf", first.FileName);
        Assert.Equal("application/pdf", first.ContentType);
        Assert.Equal(Fake.PdfBytes, first.Content);

        Assert.Equal(new[] { 5123402 }, result.Documents[1].VoucherIds);
        Assert.Equal(PdfsTestData.OtherPdfBytes, result.Documents[1].Content);
    }

    [Fact]
    public async Task MapsACombinedDocument_WithSeveralVoucherIdsAndANullTemplate()
    {
        TestHarness harness = Fake.Client(PdfsTestData.Ready(PdfsTestData.Document(
            new[] { 5123401, 5123402, 5123403 },
            null,
            "giftcard-order-" + PdfsTestData.OrderUid + ".pdf",
            PdfsTestData.OtherPdfBytes)));

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request with { Combine = true });

        PdfDocument combined = Assert.Single(result.Documents);
        Assert.Equal(new[] { 5123401, 5123402, 5123403 }, combined.VoucherIds);
        Assert.Null(combined.PdfTemplateUid);
        Assert.Equal("giftcard-order-" + PdfsTestData.OrderUid + ".pdf", combined.FileName);
        Assert.Equal(PdfsTestData.OtherPdfBytes, combined.Content);
    }

    [Theory]
    [InlineData("+/+/JVBERi0xLjcKAAolJUVPRg==")]
    [InlineData("\\u002B\\/\\u002B\\/JVBERi0xLjcKAAolJUVPRg\\u003D\\u003D")]
    public async Task DecodesBase64WhetherOrNotItsCharactersAreJsonEscaped(string content)
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Text = "{\"OrderUID\":\"u\",\"Documents\":[{\"VoucherIDs\":[1],\"Content\":\"" + content + "\"}],\"Status\":200}",
        });

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.Equal(PdfsTestData.OtherPdfBytes, Assert.Single(result.Documents).Content);
    }

    [Fact]
    public async Task Maps202AsNotReady_WithRetryAfterInWholeSeconds()
    {
        TestHarness harness = Fake.Client(PdfsTestData.NotReady("30"));

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.False(result.Ready);
        Assert.Equal(PdfsTestData.OrderUid, result.OrderUid);
        Assert.Empty(result.Documents);
        Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter);
    }

    [Fact]
    public async Task Maps202WithoutRetryAfter_ToNull()
    {
        TestHarness harness = Fake.Client(PdfsTestData.NotReady(retryAfter: null));

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.False(result.Ready);
        Assert.Null(result.RetryAfter);
    }

    [Theory]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("30s")]
    [InlineData("99999999999")]
    public async Task ReadsAnUnparseableRetryAfterAsNull(string retryAfter)
    {
        TestHarness harness = Fake.Client(PdfsTestData.NotReady(retryAfter));

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.Null(result.RetryAfter);
    }

    [Theory]
    [InlineData("Fri, 01 Jan 2100 00:00:00 GMT")]
    [InlineData("Sat, 26 Sep 2026 12:00:00 GMT")]
    public async Task ReadsAnHttpDateRetryAfter_InTheFutureOrThePast_AsNull_WholeSecondsOnly(string date)
    {
        TestHarness harness = Fake.Client(PdfsTestData.NotReady(date));

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task MapsNullAndMissingFields()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json(
                "{\"Documents\":[{\"VoucherIDs\":null,\"PDFTemplateUid\":null,\"FileName\":null," +
                "\"ContentType\":null,\"Content\":null},{}],\"Status\":200}"),
        });

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.True(result.Ready);
        Assert.Null(result.OrderUid);
        Assert.Equal(2, result.Documents.Count);
        foreach (PdfDocument document in result.Documents)
        {
            Assert.Empty(document.VoucherIds);
            Assert.Null(document.PdfTemplateUid);
            Assert.Null(document.FileName);
            Assert.Null(document.ContentType);
            Assert.Null(document.Content);
        }
    }

    [Fact]
    public async Task MapsA200WithoutDocuments_ReadyMeansHttp200()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"Status\":200}") });

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.True(result.Ready);
        Assert.Empty(result.Documents);
    }

    [Fact]
    public async Task DecodesADocumentOver20MB_NothingCapsTheResponseSize()
    {
        byte[] large = new byte[(20 * 1024 * 1024) + 1];
        for (int i = 0; i < large.Length; i++)
        {
            large[i] = (byte)(i % 251);
        }

        TestHarness harness = Fake.Client(new MockResponse
        {
            Text = "{\"OrderUID\":\"u\",\"Documents\":[{\"VoucherIDs\":[1],\"Content\":\"" +
                Convert.ToBase64String(large) + "\"}],\"Status\":200}",
        });

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.Equal(large, Assert.Single(result.Documents).Content);
    }
}

public class PdfErrorTests
{
    [Theory]
    [InlineData(400, "Invalid request", typeof(HuurayApiException))]
    [InlineData(401, "Restricted Access", typeof(HuurayAuthException))]
    [InlineData(404, "No order was found with the given OrderUID", typeof(HuurayNotFoundException))]
    [InlineData(422, "The PDF can only be fetched for orders with at most 3 receivers", typeof(HuurayValidationException))]
    [InlineData(500, "The PDF could not be generated", typeof(HuurayServerException))]
    public async Task MapsTheEnvelopeOntoTheExistingExceptionTypes(int status, string statusMessage, Type expected)
    {
        TestHarness harness = Fake.Client(PdfsTestData.Error(status, "short form", statusMessage));

        Exception? error = await Record.ExceptionAsync(() => harness.Client.Pdfs.GetAsync(PdfsTestData.Request));

        HuurayApiException apiError = Assert.IsAssignableFrom<HuurayApiException>(error);
        Assert.IsType(expected, error);
        Assert.Equal(status, apiError.HttpStatus);
        Assert.Equal(statusMessage, apiError.StatusMessage);
        Assert.Equal("POST", apiError.Method);
        Assert.Equal("/v4/Pdf", apiError.Path);
    }

    [Fact]
    public async Task MapsTheFrameworksProblemDetails400_WhichIsNotTheEnvelope()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 400,
            Text = "{\"title\":\"One or more validation errors occurred.\",\"status\":400,\"errors\":{\"$.VoucherID\":[\"bad\"]}}",
        });

        HuurayApiException error = await Assert.ThrowsAsync<HuurayApiException>(() =>
            harness.Client.Pdfs.GetAsync(PdfsTestData.Request));

        Assert.Equal(400, error.HttpStatus);
        Assert.Null(error.StatusMessage);
    }

    [Theory]
    [InlineData("JVBERi0x-NOT*BASE64!")]
    [InlineData("JVBERi0")]
    public async Task AGarbledBase64On200_IsAConnectionError_ThatNeverQuotesTheContent(string content)
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Text = "{\"OrderUID\":\"u\",\"Documents\":[{\"VoucherIDs\":[1],\"Content\":\"" + content + "\"}],\"Status\":200}",
        });

        HuurayConnectionException error = await Assert.ThrowsAsync<HuurayConnectionException>(() =>
            harness.Client.Pdfs.GetAsync(PdfsTestData.Request));

        Assert.Equal("POST", error.Method);
        Assert.Equal("/v4/Pdf", error.Path);
        Assert.Contains("returned HTTP 200 but the body was not usable", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(content, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(content, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("JVBERi0", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGarbledBase64On200_IsRetriedLikeAnyUnusableReadBody()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[]
            {
                new MockResponse { Text = "{\"Documents\":[{\"Content\":\"%%%\"}]}" },
                PdfsTestData.ReadyOne,
            },
            retry: PdfsTestData.EagerRetries);

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.Equal(2, harness.Handler.Invocations);
        Assert.Equal(Fake.PdfBytes, Assert.Single(result.Documents).Content);
    }

    [Fact]
    public async Task RetriesARead_On503_WithAFreshNonceEachAttempt()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[] { new MockResponse { Status = 503 }, PdfsTestData.ReadyOne },
            retry: PdfsTestData.EagerRetries);

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.True(result.Ready);
        Assert.Equal(2, harness.Calls.Count);
        Assert.NotEqual(harness.Calls[0].Headers["X-API-NONCE"], harness.Calls[1].Headers["X-API-NONCE"]);
        Assert.NotEqual(harness.Calls[0].Headers["X-API-HASH"], harness.Calls[1].Headers["X-API-HASH"]);
        Assert.Equal(harness.Calls[0].Body, harness.Calls[1].Body);
    }

    [Fact]
    public async Task RetriesAConnectionFailure()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[] { new MockResponse { Throws = new HttpRequestException("socket hang up") }, PdfsTestData.ReadyOne },
            retry: PdfsTestData.EagerRetries);

        PdfResult result = await harness.Client.Pdfs.GetAsync(PdfsTestData.Request);

        Assert.True(result.Ready);
        Assert.Equal(2, harness.Handler.Invocations);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(422)]
    public async Task DoesNotRetryA4xx(int status)
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[] { PdfsTestData.Error(status, "no", "no") },
            retry: PdfsTestData.EagerRetries);

        await Assert.ThrowsAnyAsync<HuurayApiException>(() => harness.Client.Pdfs.GetAsync(PdfsTestData.Request));

        Assert.Equal(1, harness.Handler.Invocations);
    }

    [Fact]
    public async Task ATimeoutIsTheOrdinaryTimeoutException_NeverAnIndeterminateOrder()
    {
        TestHarness harness = Fake.Client(new MockResponse { Hangs = true }, timeout: TimeSpan.FromMilliseconds(50));

        Exception? error = await Record.ExceptionAsync(() => harness.Client.Pdfs.GetAsync(PdfsTestData.Request));

        HuurayTimeoutException timeout = Assert.IsType<HuurayTimeoutException>(error);
        Assert.Equal("POST /v4/Pdf timed out after 50ms.", timeout.Message);
    }

    [Fact]
    public async Task AMidBodyDropIsAConnectionError()
    {
        TestHarness harness = Fake.Client(new MockResponse { BodyThrows = new IOException("terminated") });

        HuurayConnectionException error = await Assert.ThrowsAsync<HuurayConnectionException>(() =>
            harness.Client.Pdfs.GetAsync(PdfsTestData.Request));

        Assert.IsNotType<HuurayTimeoutException>(error);
        Assert.Equal("POST", error.Method);
        Assert.Equal("/v4/Pdf", error.Path);
    }
}

public class PdfGetWhenReadyTests
{
    [Fact]
    public async Task ReturnsAtOnce_WhenTheFirstAnswerIsReady()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[] { PdfsTestData.ReadyOne });

        PdfResult result = await clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request);

        Assert.True(result.Ready);
        Assert.Equal(Fake.PdfBytes, Assert.Single(result.Documents).Content);
        Assert.Empty(clock.Waits);
    }

    [Fact]
    public async Task WaitsRetryAfterOnEach202_ThenReturnsThe200_WithANewSignedRequestEachTime()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[]
        {
            PdfsTestData.NotReady("30"),
            PdfsTestData.NotReady("5"),
            PdfsTestData.ReadyOne,
        });

        PdfResult result = await clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request with { VoucherId = 5123401 });

        Assert.True(result.Ready);
        Assert.Equal(Fake.PdfBytes, Assert.Single(result.Documents).Content);
        Assert.Equal(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5) }, clock.Waits);

        Assert.Equal(3, harness.Calls.Count);
        Assert.Equal(3, harness.Calls.Select(c => c.Headers["X-API-NONCE"]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, harness.Calls.Select(c => c.Headers["X-API-HASH"]).Distinct(StringComparer.Ordinal).Count());
        Assert.All(harness.Calls, call => Assert.Equal(harness.First.Body, call.Body));
    }

    [Fact]
    public async Task Waits30Seconds_WhenA202CarriesNoUsableRetryAfter()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[]
        {
            PdfsTestData.NotReady(retryAfter: null),
            PdfsTestData.NotReady("soon"),
            PdfsTestData.ReadyOne,
        });

        await clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request);

        Assert.Equal(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) }, clock.Waits);
    }

    [Fact]
    public async Task NeverWaitsANegativeTime()
    {
        // A negative value and a date in the past both read as null, so each waits the
        // default rather than no time at all.
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[]
        {
            PdfsTestData.NotReady("-5"),
            PdfsTestData.NotReady("Sat, 26 Sep 2026 12:00:00 GMT"),
            PdfsTestData.ReadyOne,
        });

        await clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request);

        Assert.Equal(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) }, clock.Waits);
    }

    [Fact]
    public async Task GivesUpBeforeTheNextWaitWouldPassMaxWait_WithTheOrdinaryTimeoutException()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[]
        {
            PdfsTestData.NotReady("30"),
            PdfsTestData.NotReady("30"),
            PdfsTestData.NotReady("30"),
        });

        Exception? error = await Record.ExceptionAsync(() =>
            clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request, TimeSpan.FromSeconds(60)));

        // Waited 30 + 30, exactly the budget; a third wait would pass it.
        HuurayTimeoutException timeout = Assert.IsType<HuurayTimeoutException>(error);
        Assert.Equal(new[] { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) }, clock.Waits);
        Assert.Equal(3, harness.Calls.Count);
        Assert.Equal(TimeSpan.FromSeconds(60), timeout.Timeout);
        Assert.Equal("POST", timeout.Method);
        Assert.Equal("/v4/Pdf", timeout.Path);
        Assert.Null(timeout.InnerException);
        Assert.Equal(
            "POST /v4/Pdf timed out after 60000ms. The gift card PDF was still not ready, and waiting another " +
            "30 seconds would pass maxWait. Last status: " + PdfsTestData.StillProcessing,
            timeout.Message);
    }

    [Fact]
    public async Task CountsTheTimeTheRequestsTake_NotJustTheWaits()
    {
        // 20 s waits and 10 s requests: after two rounds 60 s have passed, so it gives up
        // after the third answer rather than the fourth.
        ManualClock clock = new() { RequestTime = TimeSpan.FromSeconds(10) };
        TestHarness harness = Fake.ClientWithQueue(new[]
        {
            PdfsTestData.NotReady("20"),
            PdfsTestData.NotReady("20"),
            PdfsTestData.NotReady("20"),
        });

        await Assert.ThrowsAsync<HuurayTimeoutException>(() =>
            clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request, TimeSpan.FromSeconds(60)));

        Assert.Equal(3, harness.Calls.Count);
        Assert.Equal(2, clock.Waits.Count);
    }

    [Fact]
    public async Task GivesUpAfterTheFirstAnswer_WhenItsWaitAlreadyPassesMaxWait()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[] { PdfsTestData.NotReady("30") });

        await Assert.ThrowsAsync<HuurayTimeoutException>(() =>
            clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request, TimeSpan.FromSeconds(10)));

        Assert.Single(harness.Calls);
        Assert.Empty(clock.Waits);
    }

    [Fact]
    public async Task KeepsAskingForTenMinutesByDefault()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[]
        {
            PdfsTestData.NotReady("300"),
            PdfsTestData.NotReady("300"),
            PdfsTestData.NotReady("300"),
        });

        HuurayTimeoutException error = await Assert.ThrowsAsync<HuurayTimeoutException>(() =>
            clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request));

        Assert.Equal(TimeSpan.FromMinutes(10), PdfsResource.DefaultMaxWait);
        Assert.Equal(TimeSpan.FromMinutes(10), error.Timeout);
        Assert.Equal(new[] { TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(300) }, clock.Waits);
        Assert.StartsWith("POST /v4/Pdf timed out after 600000ms.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeavesOutTheLastStatus_WhenThe202HadNone()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[] { PdfsTestData.NotReady("30", statusMessage: null) });

        HuurayTimeoutException error = await Assert.ThrowsAsync<HuurayTimeoutException>(() =>
            clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request, TimeSpan.Zero));

        Assert.EndsWith("waiting another 30 seconds would pass maxWait.", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(42_949_672_940_000L)]
    public async Task AcceptsAMaxWaitAtEitherEndOfTheRange(long ticks)
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[] { PdfsTestData.ReadyOne });

        PdfResult result = await clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request, TimeSpan.FromTicks(ticks));

        Assert.True(result.Ready);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-10_000L)]
    [InlineData(42_949_672_950_000L)]
    [InlineData(long.MaxValue)]
    public async Task RejectsAMaxWaitTheRuntimeCannotWait_BeforeSending(long ticks)
    {
        TestHarness harness = Fake.Client(PdfsTestData.ReadyOne);

        ArgumentOutOfRangeException error = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            harness.Client.Pdfs.GetWhenReadyAsync(PdfsTestData.Request, TimeSpan.FromTicks(ticks)));

        Assert.Equal("maxWait", error.ParamName);
        Assert.Equal(0, harness.Handler.Invocations);
    }

    [Fact]
    public async Task StopsAtOnceOnAnError()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[]
        {
            PdfsTestData.NotReady("5"),
            PdfsTestData.Error(404, "Order cancelled", "The order is cancelled"),
        });

        HuurayNotFoundException error = await Assert.ThrowsAsync<HuurayNotFoundException>(() =>
            clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request));

        Assert.Equal("The order is cancelled", error.StatusMessage);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, clock.Waits);
        Assert.Equal(2, harness.Calls.Count);
    }

    [Fact]
    public async Task TreatsA429AsAnyOtherError_TheSpecDeclaresNone()
    {
        ManualClock clock = new();
        TestHarness harness = Fake.ClientWithQueue(new[] { new MockResponse { Status = 429, RetryAfter = "1" } });

        HuurayApiException error = await Assert.ThrowsAsync<HuurayApiException>(() =>
            clock.For(harness).GetWhenReadyAsync(PdfsTestData.Request));

        Assert.Equal(429, error.HttpStatus);
        Assert.Empty(clock.Waits);
        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task CancellingDuringAWaitIsPlainCancellation()
    {
        using CancellationTokenSource cts = new();
        TestHarness harness = Fake.ClientWithQueue(new[] { PdfsTestData.NotReady("30") });
        PdfsResource pdfs = new(
            harness.Client,
            new ManualClock(),
            (wait, cancellationToken) =>
            {
                cts.Cancel();
                return Task.Delay(Timeout.Infinite, cancellationToken);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pdfs.GetWhenReadyAsync(PdfsTestData.Request, cancellationToken: cts.Token));

        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task WaitsForRealThroughTheClientsOwnResource()
    {
        // A zero Retry-After, so the real wait takes no time.
        TestHarness harness = Fake.ClientWithQueue(new[] { PdfsTestData.NotReady("0"), PdfsTestData.ReadyOne });

        PdfResult result = await harness.Client.Pdfs.GetWhenReadyAsync(PdfsTestData.Request);

        Assert.True(result.Ready);
        Assert.Equal(2, harness.Calls.Count);
    }
}

public class PdfRedactionTests
{
    private static readonly byte[] Secret = Encoding.ASCII.GetBytes("SECRET-PDF-CONTENT");

    private static readonly string SecretBase64 = Convert.ToBase64String(Secret);

    [Fact]
    public void RedactsContentInEitherSpelling_ItIsABearerInstrument()
    {
        string output = Redaction.RedactJson(
            "{\"Documents\":[{\"VoucherIDs\":[1],\"Content\":\"" + SecretBase64 + "\"}]," +
            "\"documents\":[{\"content\":\"" + SecretBase64 + "\"}]}");

        Assert.DoesNotContain(SecretBase64, output, StringComparison.Ordinal);
        Assert.Contains(Redaction.SecretMarker, output, StringComparison.Ordinal);
        Assert.Contains("Content", Redaction.SecretFieldNames);
    }

    [Fact]
    public void DocumentToStringShowsTheLengthNeverTheBytes()
    {
        PdfDocument document = new(new[] { 5123401 }, PdfsTestData.TemplateUid, "giftcard-5123401.pdf", "application/pdf", Secret);

        Assert.Equal(
            "PdfDocument { VoucherIds = System.Int32[], PdfTemplateUid = " + PdfsTestData.TemplateUid +
            ", FileName = giftcard-5123401.pdf, ContentType = application/pdf, Content = [18 bytes] }",
            document.ToString());
    }

    [Fact]
    public void DocumentToStringPrintsANullContentAsTheCompilerWould()
    {
        Assert.Equal(
            "PdfDocument { VoucherIds = System.Int32[], PdfTemplateUid = , FileName = , ContentType = , Content =  }",
            new PdfDocument(Array.Empty<int>(), null, null, null, null).ToString());
    }

    [Fact]
    public void NeitherDocumentNorResultLeaksTheContentThroughAnyFormattingPath()
    {
        PdfDocument document = new(new[] { 1 }, null, "giftcard-1.pdf", "application/pdf", Secret);
        PdfResult result = new(true, PdfsTestData.OrderUid, new[] { document }, null);

        string[] renderings = Renderings(document, (document with { FileName = "copy.pdf" }).ToString())
            .Concat(Renderings(result, (result with { RetryAfter = TimeSpan.FromSeconds(1) }).ToString()))
            .Append(string.Join(", ", result.Documents))
            .ToArray();

        foreach (string rendered in renderings)
        {
            Assert.DoesNotContain("SECRET", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(SecretBase64, rendered, StringComparison.Ordinal);
        }

        Assert.Contains("Content = [18 bytes]", renderings[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnErrorBodyKeepsNoContent()
    {
        // Error bodies are the same envelope with no documents, but a document there is
        // redacted all the same.
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 422,
            Json = new JsonObject
            {
                ["OrderUID"] = PdfsTestData.OrderUid,
                ["Documents"] = new JsonArray(PdfsTestData.Document(new[] { 1 }, null, "giftcard-1.pdf", Secret)),
                ["Status"] = 422,
                ["StatusMessage"] = "rejected",
            },
        });

        HuurayValidationException error = await Assert.ThrowsAsync<HuurayValidationException>(() =>
            harness.Client.Pdfs.GetAsync(PdfsTestData.Request));

        Assert.Equal(Redaction.SecretMarker, error.Body!["Documents"]![0]!["Content"]!.GetValue<string>());
        foreach (string rendered in new[] { error.Body.ToJsonString(), error.Message, error.ToString() })
        {
            Assert.DoesNotContain(SecretBase64, rendered, StringComparison.Ordinal);
        }
    }

    private static string[] Renderings(object value, string copy) => new[]
    {
        value.ToString()!,
        $"{value}",
        string.Format(CultureInfo.InvariantCulture, "{0}", value),
        new StringBuilder().Append(value).ToString(),
        copy,
        new { Value = value }.ToString()!,
    };
}
