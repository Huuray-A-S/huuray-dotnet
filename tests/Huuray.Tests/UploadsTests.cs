using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Huuray.Serialization;
using Xunit;

namespace Huuray.Tests;

internal static class UploadsTestData
{
    internal const string Created =
        "{\"Token\":\"60050460-7a2d-42a8-a4dd-5cef88ad8374\",\"FileName\":\"purchase-order-4711.pdf\"," +
        "\"ContentType\":\"application/pdf\",\"Size\":48213,\"Status\":201,\"StatusMessage\":\"OK\"}";

    internal static CreateUploadRequest Pdf => new()
    {
        File = Fake.PdfBytes,
        FileName = "purchase-order-4711.pdf",
        ContentType = "application/pdf",
    };

    internal static MockResponse Response201 => new() { Status = 201, Json = Fake.Json(Created) };
}

public class UploadRequestShapeTests
{
    [Fact]
    public async Task SendsOneFilePart_WithItsFileNameContentTypeAndBytesIntact()
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf);

        CapturedRequest call = Assert.Single(harness.Calls);
        Assert.Equal("POST", call.Method);
        Assert.Equal("/v4/Upload", call.Path);
        Assert.Empty(call.Query);
        Assert.Equal(Fake.ApiToken, call.Headers["X-API-TOKEN"]);
        Assert.False(string.IsNullOrEmpty(call.Headers["X-API-NONCE"]));
        Assert.Matches("^[0-9a-f]{128}$", call.Headers["X-API-HASH"]);

        MediaTypeHeaderValue contentType = MediaTypeHeaderValue.Parse(call.Headers["Content-Type"]);
        Assert.Equal("multipart/form-data", contentType.MediaType);
        Assert.Contains(contentType.Parameters, p => p.Name == "boundary" && !string.IsNullOrEmpty(p.Value));
        Assert.Null(call.PartsError);

        CapturedPart part = Assert.Single(call.Parts!);
        Assert.Equal("form-data", part.Disposition);
        Assert.Equal("File", part.Name);
        Assert.Equal("purchase-order-4711.pdf", part.FileNameParameter);
        Assert.Equal("purchase-order-4711.pdf", part.FileName);
        Assert.Equal("application/pdf", part.ContentType);
        Assert.Equal(Fake.PdfBytes, part.Content);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SendsApplicationOctetStreamWhenNoContentTypeIsGiven(string? contentType)
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf with { ContentType = contentType });

        CapturedPart part = Assert.Single(harness.First.Parts!);
        Assert.Equal("application/octet-stream", part.ContentType);
        Assert.Equal(Fake.PdfBytes, part.Content);
    }

    [Theory]
    // No type guard: which files are accepted is the API's decision.
    [InlineData("image/svg+xml")]
    [InlineData("text/plain; charset=utf-8")]
    public async Task SendsAnyMediaType_TheApiDecidesWhatItAccepts(string contentType)
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf with { ContentType = contentType });

        Assert.Equal(contentType, Assert.Single(harness.First.Parts!).ContentType);
    }

    [Fact]
    public async Task SendsAnEmptyFile_NoSizeGuard()
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf with { File = ReadOnlyMemory<byte>.Empty });

        Assert.Empty(Assert.Single(harness.First.Parts!).Content);
    }

    [Fact]
    public async Task SendsANonAsciiFileNameSoTheServerReadsItBack()
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf with { FileName = "indkøbsordre 4711.pdf" });

        CapturedPart part = Assert.Single(harness.First.Parts!);
        Assert.Equal("indkøbsordre 4711.pdf", part.FileNameStarParameter);
        Assert.False(string.IsNullOrEmpty(part.FileNameParameter));
    }

    [Fact]
    public async Task SendsNoJsonAndNoCharsetOnTheRequest()
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf);

        Assert.Equal("multipart/form-data", harness.First.MediaType);
        Assert.Null(harness.First.BodyJson);
        Assert.DoesNotContain("charset", harness.First.Headers["Content-Type"], StringComparison.OrdinalIgnoreCase);
    }
}

public class UploadInputTests
{
    [Fact]
    public async Task RejectsANullRequest()
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.Uploads.CreateAsync(null!));
        Assert.Equal(0, harness.Handler.Invocations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequiresAFileName_BeforeSending(string? fileName)
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf with { FileName = fileName! }));

        Assert.Contains("FileName is required", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Handler.Invocations);
    }

    [Theory]
    [InlineData("jane\"doe.pdf")]
    [InlineData("\"jane-doe.pdf\"")]
    [InlineData("jane\r\nX-Injected: 1.pdf")]
    [InlineData("jane\ndoe.pdf")]
    [InlineData("jane\0doe.pdf")]
    [InlineData("jane\tdoe.pdf")]
    [InlineData("jane\u007fdoe.pdf")]
    public async Task RejectsAFileNameThatCannotBeSentInThePartHeader_WithoutQuotingIt(string fileName)
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf with { FileName = fileName }));

        Assert.Contains("double quote or a control character", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("jane", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Handler.Invocations);
    }

    [Theory]
    [InlineData("not a type")]
    [InlineData("application/pdf\r\nX-Injected: 1")]
    [InlineData("application/pdf, image/png")]
    public async Task RejectsAContentTypeThatIsNotAMediaType_WithoutQuotingIt(string contentType)
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf with { ContentType = contentType }));

        Assert.Contains("ContentType is not a media type", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(contentType, error.Message, StringComparison.Ordinal);
        Assert.Equal(0, harness.Handler.Invocations);
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenSendsNothing_AndIsPlainCancellation()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf, cts.Token));

        Assert.Equal(cts.Token, error.CancellationToken);
        Assert.Equal(0, harness.Handler.Invocations);
    }
}

public class UploadResultTests
{
    [Fact]
    public async Task Maps201OntoTheResult_ItIsSuccess()
    {
        TestHarness harness = Fake.Client(UploadsTestData.Response201);

        UploadResult result = await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf);

        Assert.Equal("60050460-7a2d-42a8-a4dd-5cef88ad8374", result.Token);
        Assert.Equal("purchase-order-4711.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(48213L, result.Size);
    }

    [Fact]
    public async Task MapsASizeBeyondInt32()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 201,
            Json = Fake.Json("{\"Token\":\"t\",\"Size\":4294967296}"),
        });

        UploadResult result = await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf);

        Assert.Equal(4_294_967_296L, result.Size);
    }

    [Fact]
    public async Task MapsNullAndMissingFieldsAsNull()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 201,
            Json = Fake.Json("{\"Token\":\"t\",\"ContentType\":null,\"Status\":201}"),
        });

        UploadResult result = await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf);

        Assert.Equal(new UploadResult("t", null, null, null), result);
    }

    [Fact]
    public async Task ThrowsTheGenericApiExceptionFor413_NoDedicatedMapping()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 413, Text = string.Empty });

        HuurayApiException error = await Assert.ThrowsAsync<HuurayApiException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        Assert.Equal(413, error.HttpStatus);
        Assert.Null(error.Body);
    }

    [Fact]
    public async Task ThrowsValidationExceptionFor422_WithTheStatusMessage()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 422,
            Json = Fake.Json(
                "{\"Token\":null,\"FileName\":null,\"ContentType\":null,\"Size\":null,\"Status\":422," +
                "\"StatusMessage\":\"The file type is not supported\"}"),
        });

        HuurayValidationException error = await Assert.ThrowsAsync<HuurayValidationException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        Assert.Equal("The file type is not supported", error.StatusMessage);
    }
}

public class UploadIsNeverRetriedTests
{
    private static RetryOptions EagerRetries => new() { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) };

    [Theory]
    [InlineData(503)]
    [InlineData(500)]
    [InlineData(429)]
    public async Task SendsOnceOnARetryableStatus(int status)
    {
        TestHarness harness = Fake.ClientWithQueue(new[] { new MockResponse { Status = status } }, retry: EagerRetries);

        HuurayApiException error = await Assert.ThrowsAnyAsync<HuurayApiException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        Assert.Equal(status, error.HttpStatus);
        Assert.Equal(1, harness.Handler.Invocations);
    }

    [Fact]
    public async Task SendsOnceOnAConnectionFailure_AndSaysTheUploadMayBeStored()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[] { new MockResponse { Throws = new HttpRequestException("socket hang up") } },
            retry: EagerRetries);

        HuurayConnectionException error = await Assert.ThrowsAsync<HuurayConnectionException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        Assert.Equal(1, harness.Handler.Invocations);
        Assert.Equal("POST", error.Method);
        Assert.Equal("/v4/Upload", error.Path);
        Assert.Contains("socket hang up", error.Message, StringComparison.Ordinal);
        Assert.EndsWith(UploadsResource.MayBeStoredNote, error.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task SendsOnceOnAMidBodyDrop_AndSaysTheUploadMayBeStored()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[] { new MockResponse { Status = 201, BodyThrows = new IOException("terminated") } },
            retry: EagerRetries);

        HuurayConnectionException error = await Assert.ThrowsAsync<HuurayConnectionException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        Assert.Equal(1, harness.Handler.Invocations);
        Assert.EndsWith(UploadsResource.MayBeStoredNote, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsOnceOnATimeout_AndThrowsTheOrdinaryTimeoutException()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[] { new MockResponse { Hangs = true } },
            retry: EagerRetries,
            timeout: TimeSpan.FromMilliseconds(50));

        Exception? error = await Record.ExceptionAsync(() => harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        // The ordinary timeout — not HuurayIndeterminateOrderException, which points at an
        // order search that has no counterpart for uploads.
        HuurayTimeoutException timeout = Assert.IsType<HuurayTimeoutException>(error);
        Assert.Equal(1, harness.Handler.Invocations);
        Assert.Equal(TimeSpan.FromMilliseconds(50), timeout.Timeout);
        Assert.Equal(
            "POST /v4/Upload timed out after 50ms. The upload may still have been stored, and may hold a pending " +
            "upload slot until it is used or cleaned up. Uploads are never retried automatically.",
            timeout.Message);
    }

    [Fact]
    public async Task SendsOnceOnAGarbled201Body_AndSaysTheUploadMayBeStored()
    {
        TestHarness harness = Fake.ClientWithQueue(
            new[] { new MockResponse { Status = 201, Text = "<html>gateway</html>" } },
            retry: EagerRetries);

        HuurayConnectionException error = await Assert.ThrowsAsync<HuurayConnectionException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        Assert.Equal(1, harness.Handler.Invocations);
        Assert.Contains("returned HTTP 201", error.Message, StringComparison.Ordinal);
        Assert.EndsWith(UploadsResource.MayBeStoredNote, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNoteIsOnlyEverAddedToUploads()
    {
        TestHarness harness = Fake.Client(new MockResponse { Hangs = true }, timeout: TimeSpan.FromMilliseconds(50));

        HuurayTimeoutException error =
            await Assert.ThrowsAsync<HuurayTimeoutException>(() => harness.Client.Balances.ListAsync());

        Assert.Equal("GET /v4/Balance timed out after 50ms.", error.Message);
    }

    [Fact]
    public async Task TheBodyIsBuiltAfreshForEveryAttempt_ShouldAnyCallerEverRetryIt()
    {
        // Upload is sent with retryable: false. The content factory still has to produce a
        // new body per attempt, because an HttpContent cannot be sent twice; this drives the
        // same internal path with retrying switched on.
        TestHarness harness = Fake.ClientWithQueue(
            new[] { new MockResponse { Status = 503 }, UploadsTestData.Response201 },
            retry: EagerRetries);
        int built = 0;

        HuurayResponse<UploadResponseWire> response = await harness.Client.SendAsync(
            HttpMethod.Post,
            "/v4/Upload",
            () =>
            {
                built++;
                MultipartFormDataContent content = new();
                ByteArrayContent part = new(Fake.PdfBytes);
                part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                content.Add(part, "File", "po.pdf");
                return content;
            },
            retryable: true,
            failureNote: null,
            HuurayJsonContext.Default.UploadResponseWire,
            CancellationToken.None);

        Assert.Equal(201, response.HttpStatus);
        Assert.Equal(2, built);
        Assert.Equal(2, harness.Calls.Count);
        foreach (CapturedRequest call in harness.Calls)
        {
            Assert.Equal(Fake.PdfBytes, Assert.Single(call.Parts!).Content);
        }

        Assert.NotEqual(harness.Calls[0].Headers["X-API-NONCE"], harness.Calls[1].Headers["X-API-NONCE"]);
    }
}

public class UploadRedactionTests
{
    private const string PersonalFileName = "purchase-order-jane-doe.pdf";

    [Fact]
    public void RequestToStringShowsTheLengthNeverTheBytes_AndMasksTheFileName()
    {
        CreateUploadRequest request = new()
        {
            File = Encoding.ASCII.GetBytes("SECRET-PDF-CONTENT"),
            FileName = PersonalFileName,
            ContentType = "application/pdf",
        };

        Assert.Equal(
            "CreateUploadRequest { File = [18 bytes], FileName = pu***df, ContentType = application/pdf }",
            request.ToString());
    }

    [Fact]
    public void RequestLeaksNeitherBytesNorFileNameThroughAnyFormattingPath()
    {
        CreateUploadRequest request = new()
        {
            File = Encoding.ASCII.GetBytes("SECRET-PDF-CONTENT"),
            FileName = PersonalFileName,
        };

        foreach (string rendered in Renderings(request, (request with { ContentType = "image/png" }).ToString()))
        {
            Assert.DoesNotContain("SECRET", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("jane", rendered, StringComparison.Ordinal);
            Assert.Contains("File = [18 bytes]", rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ResultToStringMasksTheFileName_AndKeepsTheRest()
    {
        UploadResult result = new("60050460-7a2d-42a8-a4dd-5cef88ad8374", PersonalFileName, "application/pdf", 48213);

        Assert.Equal(
            "UploadResult { Token = 60050460-7a2d-42a8-a4dd-5cef88ad8374, FileName = pu***df, " +
            "ContentType = application/pdf, Size = 48213 }",
            result.ToString());

        foreach (string rendered in Renderings(result, (result with { Size = 1 }).ToString()))
        {
            Assert.DoesNotContain("jane", rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ResultToStringPrintsNullsAsTheCompilerWould()
    {
        Assert.Equal(
            "UploadResult { Token = , FileName = , ContentType = , Size =  }",
            new UploadResult(null, null, null, null).ToString());
    }

    [Fact]
    public async Task AnUploadErrorBodyKeepsNoFileName()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 400,
            Json = Fake.Json(
                "{\"Token\":null,\"FileName\":\"" + PersonalFileName + "\",\"Status\":400,\"StatusMessage\":\"bad\"}"),
        });

        HuurayApiException error = await Assert.ThrowsAsync<HuurayApiException>(() =>
            harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf));

        Assert.DoesNotContain("jane", error.Body!.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("jane", error.ToString(), StringComparison.Ordinal);
    }

    private static string[] Renderings(object value, string copy) => new[]
    {
        value.ToString()!,
        $"{value}",
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value),
        new StringBuilder().Append(value).ToString(),
        copy,
        new { Value = value }.ToString()!,
    };
}
