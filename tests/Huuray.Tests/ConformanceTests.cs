using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Huuray.Tests;

/// <summary>
/// Calls every public SDK method once, with every optional parameter populated, so the
/// gates below see the widest request each method can produce.
/// </summary>
public sealed class ExercisedSurface : IAsyncLifetime
{
    private TestHarness? _harness;

    public IReadOnlyList<CapturedRequest> Calls =>
        _harness?.Calls ?? Array.Empty<CapturedRequest>();

    public async Task InitializeAsync()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 200, Json = Fake.Json("{}") });
        _harness = harness;

        await harness.Client.Balances.ListAsync();
        await harness.Client.Catalogue.ListAsync(all: true);
        await harness.Client.Templates.ListAsync();
        await harness.Client.Stock.CheckAsync(new CheckStockRequest { ProductToken = "tok", Value = 5000 });
        await harness.Client.ExchangeRates.GetAsync("DKK", "EUR");

        await harness.Client.Orders.CreateAsync(new CreateOrderRequest
        {
            ProductToken = "tok",
            Value = 5000,
            Currency = "DKK",
            Quantity = 2,
            Expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RefId = "ref-1",
            TemplateId = 42,
            PdfTemplateUid = "pdf-template-uid-1",
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            PersonalMessage = "Thank you",
            Recipients = new[]
            {
                new Recipient { Name = "A", Email = "a@example.com", RefId = "r-a" },
                new Recipient { Name = "B", Phone = "+4512345678", RefId = "r-b" },
            },
            AdditionalReference = "PO-4711",
            CustomerReference = "Jane Doe",
            ArticleNumber = "ART-1",
            Description = "Two gift cards for the sales team",
            PurchaseOrderFileToken = "60050460-7a2d-42a8-a4dd-5cef88ad8374",
        });

        await harness.Client.Orders.CreateSyncAsync(new CreateOrderRequest
        {
            ProductToken = "tok",
            Value = 5000,
            Currency = "DKK",
            Quantity = 1,
            Expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RefId = "ref-sync",
            TemplateId = 42,
            PdfTemplateUid = "pdf-template-uid-1",
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            PersonalMessage = "Thanks",
            Recipients = new[] { new Recipient { Name = "C", Email = "c@example.com", RefId = "r-c" } },
            AdditionalReference = "PO-4712",
            CustomerReference = "John Doe",
            ArticleNumber = "ART-2",
            Description = "One gift card",
            PurchaseOrderFileToken = "70050460-7a2d-42a8-a4dd-5cef88ad8374",
        });

        await harness.Client.Orders.SendRewardAsync(new SendRewardRequest
        {
            ProductToken = "tok",
            Value = 5000,
            Currency = "DKK",
            Recipient = new Recipient { Name = "Jane", Email = "jane@example.com" },
            TemplateId = 42,
            PdfTemplateUid = "pdf-template-uid-1",
            RefId = "ref-2",
            PersonalMessage = "Nice work",
            Expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            AdditionalReference = "PO-4713",
            CustomerReference = "Jane Doe",
            ArticleNumber = "ART-3",
            Description = "A reward",
            PurchaseOrderFileToken = "80050460-7a2d-42a8-a4dd-5cef88ad8374",
        });

        await harness.Client.Orders.SearchAsync(new SearchOrdersRequest
        {
            OrderUid = "uid",
            VoucherId = 7,
            ProductToken = "tok",
            RefId = "ref-1",
            SmsTemplateId = 1,
            EmailTemplateId = 2,
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            RecipientName = "Jane",
            RecipientEmail = "jane@example.com",
            RecipientPhone = "+4512345678",
            RecipientRefId = "r-a",
        });

        await harness.Client.Orders.ResendAsync(new ResendRequest { OrderUid = "uid", VoucherId = 7 });
        await harness.Client.Orders.CancelAsync(new CancelRequest { OrderUid = "uid", VoucherId = 7 });

        await harness.Client.Uploads.CreateAsync(new CreateUploadRequest
        {
            File = Fake.PdfBytes,
            FileName = "purchase-order-4711.pdf",
            ContentType = "application/pdf",
        });

        // Every PdfRequest field between the two: Combine true on one and false on the other.
        await harness.Client.Pdfs.GetAsync(new GetPdfRequest
        {
            OrderUid = "uid",
            VoucherId = 7,
            PdfTemplateUid = "pdf-template-uid-1",
            Combine = true,
        });

        await harness.Client.Pdfs.GetWhenReadyAsync(
            new GetPdfRequest
            {
                OrderUid = "uid",
                VoucherId = 8,
                PdfTemplateUid = "pdf-template-uid-2",
                Combine = false,
            },
            TimeSpan.FromMinutes(1));
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class NoInventionGate : IClassFixture<ExercisedSurface>
{
    private readonly ExercisedSurface _surface;

    public NoInventionGate(ExercisedSurface surface) => _surface = surface;

    [Fact]
    public void EveryRequestTheSdkMakesIsADocumentedV4Operation()
    {
        HashSet<string> documented = Spec.Operations();

        List<string> undocumented = _surface.Calls
            .Select(call => call.Method + " " + call.Path)
            .Where(key => !documented.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            "The SDK called operations the specification does not define:\n" + string.Join("\n", undocumented));
    }

    [Fact]
    public void EveryQueryParameterTheSdkSendsIsDeclaredInTheSpec()
    {
        List<string> failures = new();

        foreach (CapturedRequest call in _surface.Calls)
        {
            if (call.Query.Count == 0)
            {
                continue;
            }

            HashSet<string> declared = Spec.DeclaredQueryParameters(call.Method, call.Path);
            foreach (KeyValuePair<string, string> parameter in call.Query)
            {
                if (!declared.Contains(parameter.Key))
                {
                    failures.Add($"{call.Method} {call.Path} sent undeclared query parameter \"{parameter.Key}\"");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void EveryRequestCarriesTheThreeAuthenticationHeadersTheSpecRequires()
    {
        foreach (CapturedRequest call in _surface.Calls)
        {
            Assert.True(call.Headers.ContainsKey("X-API-TOKEN"), $"{call.Method} {call.Path} sent no token");
            Assert.True(call.Headers.ContainsKey("X-API-NONCE"), $"{call.Method} {call.Path} sent no nonce");
            Assert.True(call.Headers.ContainsKey("X-API-HASH"), $"{call.Method} {call.Path} sent no hash");
            Assert.True(
                call.Headers["X-API-NONCE"].Length <= RequestSigner.NonceMaxLength,
                $"{call.Method} {call.Path} sent a nonce over the documented limit");
        }
    }
}

public class CoverageGate : IClassFixture<ExercisedSurface>
{
    private readonly ExercisedSurface _surface;

    public CoverageGate(ExercisedSurface surface) => _surface = surface;

    [Fact]
    public void EveryDocumentedV4OperationHasAnSdkMethod()
    {
        HashSet<string> exercised = new(
            _surface.Calls.Select(call => call.Method + " " + call.Path),
            StringComparer.Ordinal);

        List<string> missing = Spec.Operations().Where(op => !exercised.Contains(op)).ToList();

        Assert.True(
            missing.Count == 0,
            "The specification documents operations the SDK does not implement:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void CoversExactlyTheElevenV4Operations_NoMoreAndNoFewer()
    {
        Assert.Equal(11, Spec.Operations().Count);
    }
}

public class RequestConformanceGate : IClassFixture<ExercisedSurface>
{
    private readonly ExercisedSurface _surface;

    public RequestConformanceGate(ExercisedSurface surface) => _surface = surface;

    [Fact]
    public void EveryRequestBodyValidatesAgainstItsSpecSchema()
    {
        List<string> failures = new();

        foreach (CapturedRequest call in _surface.Calls)
        {
            failures.AddRange(Spec.ValidateRequestBody(Spec.RequestBody(call.Method, call.Path), call));
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void ExercisesDeliveryPDFTemplateUidOnEveryOrderCall_SoTheGateValidatesIt()
    {
        // Guards the exercise itself: if the field silently stopped being sent, the schema
        // check above would still pass, having validated nothing about it.
        Assert.True(Spec.Schemas["OrderRequest"]!["properties"]!.AsObject().ContainsKey("DeliveryPDFTemplateUid"));

        List<CapturedRequest> orders = _surface.Calls
            .Where(c => c.Method == "POST" && c.Path == "/v4/Order")
            .ToList();

        Assert.Equal(3, orders.Count);
        foreach (CapturedRequest call in orders)
        {
            Assert.Equal("pdf-template-uid-1", call.BodyJson!["DeliveryPDFTemplateUid"]?.GetValue<string>());
        }
    }

    [Fact]
    public void ExercisesTheFivePurchaseOrderFieldsOnEveryOrderCall_SoTheGateValidatesThem()
    {
        string[] fields = { "AdditionalReference", "CustomerReference", "ArticleNumber", "Description", "PurchaseOrderFileToken" };
        JsonObject declared = Spec.Schemas["OrderRequest"]!["properties"]!.AsObject();

        List<CapturedRequest> orders = _surface.Calls
            .Where(c => c.Method == "POST" && c.Path == "/v4/Order")
            .ToList();

        Assert.Equal(3, orders.Count);
        foreach (string field in fields)
        {
            Assert.True(declared.ContainsKey(field), $"OrderRequest does not declare {field}");
            foreach (CapturedRequest call in orders)
            {
                Assert.False(string.IsNullOrEmpty(call.BodyJson![field]?.GetValue<string>()), $"{field} was not exercised");
            }
        }
    }

    [Fact]
    public void ExercisesUploadAsOneFilePart_SoTheGateValidatesItsParts()
    {
        CapturedRequest call = _surface.Calls.Single(c => c.Path == "/v4/Upload");

        Assert.Equal("multipart/form-data", call.MediaType);
        CapturedPart part = Assert.Single(call.Parts!);
        Assert.Equal("File", part.Name);
        Assert.Equal("purchase-order-4711.pdf", part.FileName);
        Assert.Equal("application/pdf", part.ContentType);
        Assert.Equal(Fake.PdfBytes.ToArray(), part.Content);
    }

    [Fact]
    public void ExercisesEveryPdfRequestField_SoTheGateValidatesThem()
    {
        JsonObject schema = Spec.Schemas["PdfRequest"]!.AsObject();
        Assert.Contains(schema["required"]!.AsArray(), name => name!.GetValue<string>() == "OrderUID");

        List<JsonObject> bodies = _surface.Calls
            .Where(c => c.Method == "POST" && c.Path == "/v4/Pdf")
            .Select(c => c.BodyJson!.AsObject())
            .ToList();

        Assert.Equal(2, bodies.Count);
        foreach (KeyValuePair<string, JsonNode?> property in schema["properties"]!.AsObject())
        {
            Assert.All(bodies, body => Assert.NotNull(body[property.Key]));
        }

        Assert.Equal(new[] { true, false }, bodies.Select(body => body["Combine"]!.GetValue<bool>()));
    }

    [Fact]
    public void SendsNoBodyToTemplate_WhichDeclaresNone()
    {
        CapturedRequest call = _surface.Calls.Single(c => c.Path == "/v4/Template");

        Assert.True(call.BodyOmitted);
    }
}

public class PublicSurfaceInventory
{
    /// <summary>
    /// The gates above only inspect the requests <see cref="ExercisedSurface"/> happens to
    /// make. This inventory pins the full public method list: adding a method without
    /// updating BOTH this list and the exercise fails here, so a new method can never
    /// silently bypass the gates.
    /// </summary>
    private static readonly Dictionary<string, string[]> Expected = new(StringComparer.Ordinal)
    {
        // RequestAsync is the documented escape hatch: the caller chooses the path, so it
        // is deliberately outside the no-invention gate and is not exercised by it.
        ["HuurayClient"] = new[] { "RequestAsync", "SendRewardAsync" },
        ["BalancesResource"] = new[] { "ListAsync" },
        ["CatalogueResource"] = new[] { "ListAsync" },
        ["TemplatesResource"] = new[] { "ListAsync" },
        ["StockResource"] = new[] { "CheckAsync" },
        ["ExchangeRatesResource"] = new[] { "GetAsync" },
        ["OrdersResource"] = new[]
        {
            "CancelAsync", "CreateAsync", "CreateSyncAsync", "ResendAsync", "SearchAsync", "SendRewardAsync",
        },
        ["UploadsResource"] = new[] { "CreateAsync" },
        ["PdfsResource"] = new[] { "GetAsync", "GetWhenReadyAsync" },
    };

    [Fact]
    public void EveryPublicMethodIsOnTheExercisedInventory()
    {
        // The types come from the client itself, not from a second hand-typed list: a
        // resource dropped from Expected and from such a list together would pass unseen.
        Type[] types = typeof(HuurayClient)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.PropertyType)
            .Where(type => type.Name.EndsWith("Resource", StringComparison.Ordinal))
            .Prepend(typeof(HuurayClient))
            .ToArray();

        Dictionary<string, string[]> actual = new(StringComparer.Ordinal);
        foreach (Type type in types)
        {
            actual[type.Name] = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        Assert.Equal(Expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (KeyValuePair<string, string[]> entry in Expected)
        {
            Assert.Equal(entry.Value.OrderBy(n => n, StringComparer.Ordinal), actual[entry.Key]);
        }
    }
}

public class TheGatesThemselvesWork
{
    [Fact]
    public void FlagsAnUndocumentedProperty()
    {
        List<string> errors = Spec.Validate(
            Spec.Schemas["CancelRequest"]!,
            Fake.Json("{\"OrderUID\":\"x\",\"Invented\":true}"));

        Assert.Contains(errors, error => error.Contains("Invented", StringComparison.Ordinal)
            && error.Contains("not defined in the spec", StringComparison.Ordinal));
    }

    [Fact]
    public void FlagsAMissingRequiredProperty()
    {
        List<string> errors = Spec.Validate(Spec.Schemas["CancelRequest"]!, Fake.Json("{}"));

        Assert.Contains(errors, error => error.Contains("OrderUID", StringComparison.Ordinal)
            && error.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void FlagsAPdfRequestWithoutItsRequiredOrderUID()
    {
        List<string> errors = Spec.Validate(
            Spec.Schemas["PdfRequest"]!,
            Fake.Json("{\"VoucherID\":7,\"PDFTemplateUid\":\"x\",\"Combine\":true}"));

        Assert.Contains(errors, error => error.Contains("OrderUID", StringComparison.Ordinal)
            && error.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void FlagsAPdfRequestFieldOfTheWrongType()
    {
        List<string> errors = Spec.Validate(
            Spec.Schemas["PdfRequest"]!,
            Fake.Json("{\"OrderUID\":\"x\",\"VoucherID\":\"7\",\"Combine\":\"yes\"}"));

        Assert.Contains(errors, error => error.Contains("VoucherID", StringComparison.Ordinal)
            && error.Contains("expected integer", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Combine", StringComparison.Ordinal)
            && error.Contains("expected boolean", StringComparison.Ordinal));
    }

    [Fact]
    public void FlagsAWrongType()
    {
        List<string> errors = Spec.Validate(
            Spec.Schemas["StockRequest"]!,
            Fake.Json("{\"ProductToken\":\"x\",\"Value\":1.5}"));

        Assert.Contains(errors, error => error.Contains("Value", StringComparison.Ordinal)
            && error.Contains("expected integer", StringComparison.Ordinal));
    }

    [Fact]
    public void FailsClosedOnASchemaShapeItDoesNotUnderstand()
    {
        List<string> errors = Spec.Validate(Fake.Json("{\"allOf\":[]}"), Fake.Json("{}"));

        Assert.Contains(errors, error => error.Contains("does not handle", StringComparison.Ordinal));
    }

    [Fact]
    public void FailsClosedOnASchemaWithNoType()
    {
        List<string> errors = Spec.Validate(Fake.Json("{\"description\":\"untyped\"}"), Fake.Json("{}"));

        Assert.Contains(errors, error => error.Contains("no \"type\"", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvesRefsRatherThanSkippingThem()
    {
        List<string> errors = Spec.Validate(
            Fake.Json("{\"$ref\":\"#/components/schemas/CancelRequest\"}"),
            Fake.Json("{\"Invented\":true}"));

        Assert.NotEmpty(errors);
    }

    [Fact]
    public void AcceptsAValidBody()
    {
        List<string> errors = Spec.Validate(
            Spec.Schemas["CancelRequest"]!,
            Fake.Json("{\"OrderUID\":\"x\",\"VoucherID\":7}"));

        Assert.Empty(errors);
    }

    /* ------------------------------------------------------------ multipart */

    private const string BinaryFileSchema =
        "{\"type\":\"object\",\"properties\":{\"File\":{\"type\":\"string\",\"format\":\"binary\"}}}";

    [Fact]
    public async Task AcceptsAValidMultipartBody()
    {
        CapturedRequest call = await Capture("/v4/Upload", Multipart(FilePart()));

        Assert.Empty(Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Upload"), call));
    }

    [Fact]
    public async Task FlagsAnUndeclaredPart()
    {
        CapturedRequest call = await Capture("/v4/Upload", Multipart(FilePart(), FilePart(name: "Invented")));

        List<string> errors = Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Upload"), call);

        Assert.Contains(errors, error => error.Contains("Invented", StringComparison.Ordinal)
            && error.Contains("not defined in the spec", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlagsAPartSentTwice()
    {
        CapturedRequest call = await Capture("/v4/Upload", Multipart(FilePart(), FilePart()));

        List<string> errors = Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Upload"), call);

        Assert.Contains(errors, error => error.Contains("File: sent 2 times", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlagsAMissingRequiredPart()
    {
        CapturedRequest call = await Capture("/v4/Upload", Multipart(FilePart()));
        JsonObject media = Fake.Json(
            "{\"schema\":{\"type\":\"object\",\"required\":[\"File\",\"Second\"],\"properties\":{" +
            "\"File\":{\"type\":\"string\",\"format\":\"binary\"},\"Second\":{\"type\":\"string\",\"format\":\"binary\"}}}}").AsObject();

        List<string> errors = Spec.ValidateMultipart(media, call);

        Assert.Contains(errors, error => error.Contains("Second: required by the spec but not sent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlagsABinaryPartSentWithoutAFilename()
    {
        CapturedRequest call = await Capture("/v4/Upload", Multipart(FilePart(fileName: null)));

        List<string> errors = Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Upload"), call);

        Assert.Contains(errors, error => error.Contains("with a filename", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlagsABinaryPartSentWithoutItsOwnContentType()
    {
        CapturedRequest call = await Capture("/v4/Upload", Multipart(FilePart(contentType: null)));

        List<string> errors = Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Upload"), call);

        Assert.Contains(errors, error => error.Contains("its own Content-Type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlagsABinaryPartSentWithATransferEncoding()
    {
        ByteArrayContent part = FilePart();
        part.Headers.TryAddWithoutValidation("Content-Transfer-Encoding", "base64");
        CapturedRequest call = await Capture("/v4/Upload", Multipart(part));

        List<string> errors = Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Upload"), call);

        Assert.Contains(errors, error => error.Contains("not as raw binary", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"schema\":{\"type\":\"object\",\"properties\":{\"File\":{\"type\":\"string\"}}}}")]
    [InlineData("{\"schema\":{\"type\":\"object\",\"properties\":{\"File\":{\"type\":\"string\",\"format\":\"binary\",\"nullable\":true}}}}")]
    [InlineData("{\"schema\":{\"allOf\":[" + BinaryFileSchema + "]}}")]
    [InlineData("{\"schema\":{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{\"File\":{\"type\":\"string\",\"format\":\"binary\"}}}}")]
    [InlineData("{\"schema\":" + BinaryFileSchema + ",\"encoding\":{\"File\":{\"style\":\"form\",\"contentType\":\"application/pdf\"}}}")]
    [InlineData("{\"schema\":" + BinaryFileSchema + ",\"encoding\":{\"File\":{\"style\":\"form\",\"explode\":true}}}")]
    [InlineData("{\"schema\":" + BinaryFileSchema + ",\"encoding\":{\"File\":{\"style\":\"spaceDelimited\"}}}")]
    [InlineData("{\"schema\":" + BinaryFileSchema + ",\"encoding\":{\"Other\":{\"style\":\"form\"}}}")]
    [InlineData("{\"schema\":" + BinaryFileSchema + ",\"examples\":{}}")]
    [InlineData("{\"encoding\":{}}")]
    public async Task FailsClosedOnAMultipartShapeItDoesNotUnderstand(string media)
    {
        CapturedRequest call = await Capture("/v4/Upload", Multipart(FilePart()));

        List<string> errors = Spec.ValidateMultipart(Fake.Json(media).AsObject(), call);

        Assert.Contains(errors, error => error.Contains("extend Spec.ValidateMultipart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailsClosedOnAMediaTypeItDoesNotUnderstand()
    {
        CapturedRequest call = await Capture("/v4/Upload", Multipart(FilePart()));
        JsonObject requestBody = Fake.Json(
            "{\"content\":{\"application/x-www-form-urlencoded\":{\"schema\":" + BinaryFileSchema + "}}}").AsObject();

        List<string> errors = Spec.ValidateRequestBody(requestBody, call);

        Assert.Contains(errors, error => error.Contains("does not handle", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NeverParsesAMultipartBodyAsJson_AMismatchIsAFailureNotAnException()
    {
        CapturedRequest call = await Capture("/v4/Order", Multipart(FilePart()));

        List<string> errors = Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Order"), call);

        Assert.Null(call.BodyJson);
        Assert.Contains(errors, error => error.Contains("sent multipart/form-data", StringComparison.Ordinal)
            && error.Contains("application/json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlagsAJsonBodySentToAMultipartOperation()
    {
        CapturedRequest call = await Capture(
            "/v4/Upload",
            new StringContent("{\"File\":\"x\"}", System.Text.Encoding.UTF8, "application/json"));

        List<string> errors = Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Upload"), call);

        Assert.Contains(errors, error => error.Contains("sent application/json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlagsAMultipartBodyThatIsNotWellFormed()
    {
        StringContent garbage = new("not multipart at all");
        garbage.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=abc");
        CapturedRequest call = await Capture("/v4/Upload", garbage);

        List<string> errors = Spec.ValidateRequestBody(Spec.RequestBody("POST", "/v4/Upload"), call);

        Assert.Contains(errors, error => error.Contains("not well-formed", StringComparison.Ordinal));
    }

    private static async Task<CapturedRequest> Capture(string path, HttpContent content)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "https://api.huuray.com" + path) { Content = content };
        return await CapturedRequest.CaptureAsync(request, CancellationToken.None);
    }

    private static ByteArrayContent FilePart(string name = "File", string? fileName = "po.pdf", string? contentType = "application/pdf")
    {
        ByteArrayContent part = new(Fake.PdfBytes);
        if (contentType is not null)
        {
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = name, FileName = fileName };
        return part;
    }

    private static MultipartFormDataContent Multipart(params HttpContent[] parts)
    {
        MultipartFormDataContent content = new();
        foreach (HttpContent part in parts)
        {
            content.Add(part);
        }

        return content;
    }
}
