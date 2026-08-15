using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
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
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            PersonalMessage = "Thank you",
            Recipients = new[]
            {
                new Recipient { Name = "A", Email = "a@example.com", RefId = "r-a" },
                new Recipient { Name = "B", Phone = "+4512345678", RefId = "r-b" },
            },
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
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            PersonalMessage = "Thanks",
            Recipients = new[] { new Recipient { Name = "C", Email = "c@example.com", RefId = "r-c" } },
        });

        await harness.Client.Orders.SendRewardAsync(new SendRewardRequest
        {
            ProductToken = "tok",
            Value = 5000,
            Currency = "DKK",
            Recipient = new Recipient { Name = "Jane", Email = "jane@example.com" },
            TemplateId = 42,
            RefId = "ref-2",
            PersonalMessage = "Nice work",
            Expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
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
    public void CoversExactlyTheNineV4Operations_NoMoreAndNoFewer()
    {
        Assert.Equal(9, Spec.Operations().Count);
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
            JsonObject? schema = Spec.RequestBodySchema(call.Method, call.Path);

            if (schema is null)
            {
                // The spec declares no body for this operation, so the SDK must send none.
                if (!call.BodyOmitted)
                {
                    failures.Add(
                        $"{call.Method} {call.Path}: the spec declares no requestBody, but the SDK sent one");
                }

                continue;
            }

            failures.AddRange(Spec.Validate(schema, call.BodyJson, $"{call.Method} {call.Path}"));
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
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
    };

    [Fact]
    public void EveryPublicMethodIsOnTheExercisedInventory()
    {
        Type[] types =
        {
            typeof(HuurayClient),
            typeof(BalancesResource),
            typeof(CatalogueResource),
            typeof(TemplatesResource),
            typeof(StockResource),
            typeof(ExchangeRatesResource),
            typeof(OrdersResource),
        };

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
}
