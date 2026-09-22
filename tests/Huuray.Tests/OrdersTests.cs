using System;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Huuray.Tests;

public class MinorUnitTests
{
    [Fact]
    public void RejectsAFractionalDecimalAmount()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => MinorUnits.FromDecimal(50.0001m));

        Assert.Contains("whole number of minor units", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainsTheRealFailure_MajorUnitsOrderOneHundredthOfTheIntent()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => MinorUnits.FromDecimal(50.5m));

        Assert.Contains("1/100th of the intended amount", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysPlainlyThatItCannotCatchEveryMixup()
    {
        // 50.00 IS the integer 50 — the guard passes and the order is for 0.50. No
        // run-time check can catch that, so the message says so rather than implying
        // the guard is complete.
        Assert.Equal(50, MinorUnits.FromDecimal(50.00m));
        Assert.Contains("50.00 IS the integer 50", MinorUnits.MajorUnitWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAFractionalDoubleAmount()
    {
        Assert.Throws<ArgumentException>(() => MinorUnits.FromDouble(50.5));
    }

    [Fact]
    public void RejectsAnAmountThatDoesNotFitTheApisInt32()
    {
        Assert.Throws<ArgumentException>(() => MinorUnits.FromDecimal(3_000_000_000m));
    }

    [Fact]
    public void RejectsANonFiniteDouble()
    {
        Assert.Throws<ArgumentException>(() => MinorUnits.FromDouble(double.NaN));
        Assert.Throws<ArgumentException>(() => MinorUnits.FromDouble(double.PositiveInfinity));
    }

    [Theory]
    [InlineData("5000", 5000)]
    [InlineData("-500", -500)]
    [InlineData("0", 0)]
    public void ParsesWholeMinorUnitAmounts(string text, int expected)
    {
        Assert.Equal(expected, MinorUnits.Parse(text));
    }

    [Fact]
    public void RefusesTextThatIsNotAWholeNumberOfMinorUnits()
    {
        Assert.Throws<ArgumentException>(() => MinorUnits.Parse("50.5"));
        Assert.Throws<ArgumentException>(() => MinorUnits.Parse("fifty"));
    }

    [Fact]
    public async Task SendsTheIntegerThroughUntouched()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });

        await harness.Client.Orders.CreateAsync(OrdersTestData.Base with { Value = 5000 });

        Assert.Equal(5000, harness.First.BodyJson!["Product"]!["Value"]!.GetValue<int>());
    }
}

internal static class OrdersTestData
{
    internal static CreateOrderRequest Base => new()
    {
        ProductToken = "tok",
        Value = 5000,
        Currency = "DKK",
        Quantity = 1,
    };
}

public class SyncVersusAsyncOrderTests
{
    [Fact]
    public async Task CreateAsyncSendsSyncFalse()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });

        await harness.Client.Orders.CreateAsync(OrdersTestData.Base);

        Assert.False(harness.First.BodyJson!["Sync"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CreateSyncAsyncSendsSyncTrue()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });

        await harness.Client.Orders.CreateSyncAsync(OrdersTestData.Base);

        Assert.True(harness.First.BodyJson!["Sync"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CreateSyncAsyncEnforcesTheDocumented25CodeLimit()
    {
        TestHarness harness = Fake.Client();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.Orders.CreateSyncAsync(
                OrdersTestData.Base with { Quantity = OrdersResource.SyncQuantityLimit + 1 }));

        Assert.Contains("limited to 25", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Calls);
    }

    [Fact]
    public async Task CreateSyncAsyncReturnsVouchers()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json(
                "{\"OrderUID\":\"x\",\"Vouchers\":[{\"ID\":1,\"Code\":\"ABC\",\"RedeemLink\":\"https://r/1\"," +
                "\"Expires\":\"2027-01-01\"}]}"),
        });

        CreateSyncOrderResult result = await harness.Client.Orders.CreateSyncAsync(OrdersTestData.Base);

        Voucher voucher = Assert.Single(result.Vouchers);
        Assert.Equal(1, voucher.Id);
        Assert.Equal("ABC", voucher.Code);
        Assert.Equal("https://r/1", voucher.RedeemLink);
    }

    [Fact]
    public async Task SurfacesBlankedCodesAsNullRatherThanPretending()
    {
        // Codes come back empty unless ReturnCode is enabled on the account.
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json(
                "{\"OrderUID\":\"x\",\"Vouchers\":[{\"ID\":1,\"Code\":null,\"CVV\":null,\"RedeemLink\":null}]}"),
        });

        CreateSyncOrderResult result = await harness.Client.Orders.CreateSyncAsync(OrdersTestData.Base);

        Voucher voucher = Assert.Single(result.Vouchers);
        Assert.Equal(1, voucher.Id);
        Assert.Null(voucher.Code);
        Assert.Null(voucher.Cvv);
        Assert.Null(voucher.RedeemLink);
    }

    [Fact]
    public async Task CreateSyncAsyncAcceptsExactlyTheLimit()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });

        await harness.Client.Orders.CreateSyncAsync(
            OrdersTestData.Base with { Quantity = OrdersResource.SyncQuantityLimit });

        Assert.Single(harness.Calls);
    }
}

public class RecipientValidationTests
{
    [Fact]
    public async Task RequiresRecipientsWhenADeliveryTemplateIsSet()
    {
        TestHarness harness = Fake.Client();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base with { TemplateId = 42 }));

        Assert.Contains("Recipients is required", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Calls);
    }

    [Fact]
    public async Task AcceptsExactlyOneRecipientForAMultiCodeOrder()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });

        await harness.Client.Orders.CreateAsync(OrdersTestData.Base with
        {
            Quantity = 5,
            TemplateId = 42,
            Recipients = new[] { new Recipient { Email = "a@example.com" } },
        });

        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task AcceptsARecipientCountMatchingQuantity()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });

        await harness.Client.Orders.CreateAsync(OrdersTestData.Base with
        {
            Quantity = 2,
            TemplateId = 42,
            Recipients = new[]
            {
                new Recipient { Email = "a@example.com" },
                new Recipient { Email = "b@example.com" },
            },
        });

        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task RejectsACountThatIsNeitherOneNorQuantity()
    {
        TestHarness harness = Fake.Client();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base with
            {
                Quantity = 5,
                TemplateId = 42,
                Recipients = new[]
                {
                    new Recipient { Email = "a@example.com" },
                    new Recipient { Email = "b@example.com" },
                },
            }));

        Assert.Contains("either 1 entry or exactly Quantity", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Calls);
    }

    [Fact]
    public async Task AllowsNoRecipientsWhenThereIsNoDeliveryTemplate()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });

        await harness.Client.Orders.CreateAsync(OrdersTestData.Base);

        Assert.Single(harness.Calls);
    }

    [Fact]
    public async Task RejectsANonPositiveQuantity()
    {
        TestHarness harness = Fake.Client();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base with { Quantity = 0 }));

        Assert.Empty(harness.Calls);
    }
}

public class SendRewardTests
{
    private static SendRewardRequest Reward => new()
    {
        ProductToken = "tok",
        Value = 5000,
        Currency = "DKK",
        Recipient = new Recipient { Name = "Jane", Email = "jane@example.com" },
        TemplateId = 42,
        RefId = "payroll-2026-08-jane",
    };

    [Fact]
    public async Task MakesExactlyOnePostToOrderWithQuantityOneAndSyncFalse()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"RefID\":\"r\"}") });

        await harness.Client.Orders.SendRewardAsync(Reward);

        CapturedRequest call = Assert.Single(harness.Calls);
        Assert.Equal("POST", call.Method);
        Assert.Equal("/v4/Order", call.Path);

        JsonNode body = call.BodyJson!;
        Assert.Equal(1, body["Product"]!["Quantity"]!.GetValue<int>());
        Assert.False(body["Sync"]!.GetValue<bool>());
        Assert.Equal("payroll-2026-08-jane", body["RefID"]!.GetValue<string>());
        Assert.Single<JsonNode?>(body["Recipients"]!.AsArray());
    }

    [Fact]
    public async Task RefusesWithoutARefId_AndNeverGeneratesOne()
    {
        TestHarness harness = Fake.Client();

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Client.Orders.SendRewardAsync(Reward with { RefId = string.Empty }));

        Assert.Contains("RefId is required", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Calls);
    }

    [Fact]
    public async Task IsAlsoReachableFromTheClientForTheOneCallCase()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });

        await harness.Client.SendRewardAsync(Reward);

        Assert.Single(harness.Calls);
    }
}

public class PdfTemplateOrderTests
{
    private const string PdfUid = "pdf-template-uid-1";

    private static CreateOrderRequest WithDelivery => OrdersTestData.Base with
    {
        TemplateId = 42,
        Recipients = new[] { new Recipient { Name = "Jane", Email = "jane@example.com" } },
    };

    private static SendRewardRequest Reward => new()
    {
        ProductToken = "tok",
        Value = 5000,
        Currency = "DKK",
        Recipient = new Recipient { Name = "Jane", Email = "jane@example.com" },
        TemplateId = 42,
        RefId = "reward-ref-1",
    };

    [Theory]
    [InlineData("create")]
    [InlineData("createSync")]
    [InlineData("sendReward")]
    public async Task SendsDeliveryPDFTemplateUidWhenSupplied(string method)
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });

        await Place(harness, method, PdfUid);

        CapturedRequest call = Assert.Single(harness.Calls);
        Assert.Equal("/v4/Order", call.Path);
        JsonNode body = call.BodyJson!;
        Assert.Equal(PdfUid, body["DeliveryPDFTemplateUid"]!.GetValue<string>());
        Assert.Equal(42, body["DeliveryTemplateId"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("createSync")]
    [InlineData("sendReward")]
    public async Task OmitsTheDeliveryPDFTemplateUidKeyWhenNotSupplied(string method)
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });

        await Place(harness, method, pdfTemplateUid: null);

        CapturedRequest call = Assert.Single(harness.Calls);
        Assert.False(call.BodyJson!.AsObject().ContainsKey("DeliveryPDFTemplateUid"));
        Assert.DoesNotContain("DeliveryPDFTemplateUid", call.Body!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("createSync")]
    public async Task RejectsAPdfTemplateUidWithoutATemplateId_BeforeAnyRequest(string method)
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });
        CreateOrderRequest request = OrdersTestData.Base with { PdfTemplateUid = PdfUid };

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(() => method == "create"
            ? harness.Client.Orders.CreateAsync(request)
            : harness.Client.Orders.CreateSyncAsync(request));

        Assert.Contains("TemplateId is required when PdfTemplateUid is set", error.Message, StringComparison.Ordinal);
        Assert.Contains("email template", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Calls);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("createSync")]
    [InlineData("sendReward")]
    public async Task ChecksOnlyThatTemplateIdIsPresent_NotWhatKindOfTemplateItIs(string method)
    {
        // Whether TemplateId is an email template is the API's call; the client cannot know
        // without a lookup. A phone-only recipient hints at an SMS template, and must still be sent.
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });
        Recipient phoneOnly = new() { Phone = "+4500000000" };

        Task placing = method switch
        {
            "create" => harness.Client.Orders.CreateAsync(
                WithDelivery with { Recipients = new[] { phoneOnly }, PdfTemplateUid = PdfUid }),
            "createSync" => harness.Client.Orders.CreateSyncAsync(
                WithDelivery with { Recipients = new[] { phoneOnly }, PdfTemplateUid = PdfUid }),
            "sendReward" => harness.Client.Orders.SendRewardAsync(
                Reward with { Recipient = phoneOnly, PdfTemplateUid = PdfUid }),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };

        await placing;

        CapturedRequest call = Assert.Single(harness.Calls);
        Assert.Equal("/v4/Order", call.Path);
        JsonNode body = call.BodyJson!;
        Assert.Equal(PdfUid, body["DeliveryPDFTemplateUid"]!.GetValue<string>());
        Assert.Equal(42, body["DeliveryTemplateId"]!.GetValue<int>());
        JsonNode recipient = Assert.Single(body["Recipients"]!.AsArray())!;
        Assert.Equal("+4500000000", recipient["Phone"]!.GetValue<string>());
        Assert.False(recipient.AsObject().ContainsKey("Email"));
    }

    private static Task Place(TestHarness harness, string method, string? pdfTemplateUid) => method switch
    {
        "create" => harness.Client.Orders.CreateAsync(WithDelivery with { PdfTemplateUid = pdfTemplateUid }),
        "createSync" => harness.Client.Orders.CreateSyncAsync(WithDelivery with { PdfTemplateUid = pdfTemplateUid }),
        "sendReward" => harness.Client.Orders.SendRewardAsync(Reward with { PdfTemplateUid = pdfTemplateUid }),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };
}

public class IndeterminateOrderTests
{
    [Fact]
    public async Task ThrowsWhenTheConnectionDrops()
    {
        TestHarness harness = Fake.Client(new MockResponse { Throws = new HttpRequestException("socket hang up") });

        await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base with { RefId = "ref-9" }));
    }

    [Fact]
    public async Task ThrowsWhenTheConnectionDropsMidBody_AfterTheRequestWasSent()
    {
        // The regression that matters most: a body-read failure escaping raw would
        // bypass this wrapper entirely — and a consumer's generic retry handler would
        // then re-order.
        TestHarness harness = Fake.Client(new MockResponse { BodyThrows = new IOException("terminated") });

        await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base with { RefId = "ref-9" }));
    }

    [Fact]
    public async Task ThrowsWhenTheCallersTokenCancelsMidFlight()
    {
        // Passing HttpContext.RequestAborted into an order call is idiomatic ASP.NET
        // Core, so an ordinary browser disconnect lands here. The request was already
        // on the wire, so the outcome is unknown — a bare TaskCanceledException would
        // carry no RefId, no "do not retry", and no pointer at SearchAsync, and a
        // generic resilience handler would re-issue the order.
        // The request stalls; the caller's token fires while the client's own
        // timeout is still far away, so this is caller cancellation, not a timeout.
        using CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        TestHarness harness = Fake.Client(
            new MockResponse { Hangs = true },
            timeout: TimeSpan.FromSeconds(30));

        HuurayIndeterminateOrderException error =
            await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
                harness.Client.Orders.CreateAsync(
                    OrdersTestData.Base with { RefId = "ref-cancel" },
                    cts.Token));

        Assert.Equal("ref-cancel", error.RefId);
        Assert.Contains("Do NOT retry", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    // A token cancelled before the call sends nothing, so the outcome is known: plain
    // cancellation, not an order of unknown outcome.
    [InlineData("create")]
    [InlineData("createSync")]
    [InlineData("sendReward")]
    [InlineData("resend")]
    [InlineData("cancel")]
    [InlineData("search")]
    public async Task AnAlreadyCancelledTokenSendsNothing_AndIsPlainCancellation(string method)
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });

        Exception? error = await Record.ExceptionAsync(() => method switch
        {
            "create" => harness.Client.Orders.CreateAsync(OrdersTestData.Base with { RefId = "ref-pre" }, cts.Token),
            "createSync" => harness.Client.Orders.CreateSyncAsync(OrdersTestData.Base with { RefId = "ref-pre" }, cts.Token),
            "sendReward" => harness.Client.SendRewardAsync(
                new SendRewardRequest
                {
                    ProductToken = "tok",
                    Value = 5000,
                    Currency = "DKK",
                    Recipient = new Recipient { Email = "jane@example.com" },
                    TemplateId = 42,
                    RefId = "ref-pre",
                },
                cts.Token),
            "resend" => harness.Client.Orders.ResendAsync(new ResendRequest { OrderUid = "x" }, cts.Token),
            "cancel" => harness.Client.Orders.CancelAsync(new CancelRequest { OrderUid = "x" }, cts.Token),
            "search" => harness.Client.Orders.SearchAsync(new SearchOrdersRequest { RefId = "ref-pre" }, cts.Token),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        });

        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(cts.Token, ((OperationCanceledException)error!).CancellationToken);
        Assert.Equal(0, harness.Handler.Invocations);
    }

    [Fact]
    public async Task ThrowsOnATimeoutThatFiresWhileTheResponseBodyStreams()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { BodyHangs = true },
            timeout: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base with { RefId = "ref-9" }));
    }

    [Fact]
    public async Task ThrowsOnAGarbled2xxBody_TheOrderMayWellHaveLanded()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 200, Text = "not json at all" });

        await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base with { RefId = "ref-9" }));
    }

    [Fact]
    public async Task ThrowsOn5xxToo_TheServerMayStillHaveProcessedTheOrder()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 500 });

        await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base with { RefId = "ref-9" }));
    }

    [Fact]
    public async Task CarriesTheRefIdSoTheCallerCanReconcile()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 502 });

        HuurayIndeterminateOrderException error =
            await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
                harness.Client.Orders.CreateAsync(OrdersTestData.Base with { RefId = "ref-9" }));

        Assert.Equal("ref-9", error.RefId);
        Assert.Contains("Do NOT retry", error.Message, StringComparison.Ordinal);
        Assert.Contains("ref-9", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysSoPlainlyWhenNoRefIdWasSent()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 500 });

        HuurayIndeterminateOrderException error =
            await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
                harness.Client.Orders.CreateAsync(OrdersTestData.Base));

        Assert.Contains("No RefID was sent", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoesNotMaskA422_ThatOrderWasDefinitivelyRejected()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 422,
            Json = Fake.Json("{\"Status\":422,\"StatusMessage\":\"bad\"}"),
        });

        await Assert.ThrowsAsync<HuurayValidationException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base));
    }

    [Fact]
    public async Task DoesNotMaskA401()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 401 });

        await Assert.ThrowsAsync<HuurayAuthException>(() =>
            harness.Client.Orders.CreateAsync(OrdersTestData.Base));
    }

    [Fact]
    public async Task KeepsTheUnderlyingFailureAsTheInnerException()
    {
        TestHarness harness = Fake.Client(new MockResponse { Status = 503 });

        HuurayIndeterminateOrderException error =
            await Assert.ThrowsAsync<HuurayIndeterminateOrderException>(() =>
                harness.Client.Orders.CreateAsync(OrdersTestData.Base with { RefId = "ref-9" }));

        Assert.IsType<HuurayServerException>(error.InnerException);
    }
}

public class PartialContentTests
{
    [Fact]
    public async Task FlagsAPartialCancelAndExposesThePerVoucherOutcome()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 206,
            Json = Fake.Json(
                "{\"OrderUID\":\"uid\",\"OrderCancelled\":false," +
                "\"Vouchers\":[{\"ID\":1,\"Cancelled\":true},{\"ID\":2,\"Cancelled\":false}]}"),
        });

        CancelResult result = await harness.Client.Orders.CancelAsync(new CancelRequest { OrderUid = "uid" });

        Assert.True(result.Partial);
        Assert.False(result.OrderCancelled);
        Assert.Equal(
            new[] { new CancelledVoucher(1, true), new CancelledVoucher(2, false) },
            result.Vouchers);
    }

    [Fact]
    public async Task DoesNotFlagAClean200CancelAsPartial()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 200,
            Json = Fake.Json("{\"OrderUID\":\"uid\",\"OrderCancelled\":true,\"Vouchers\":[]}"),
        });

        CancelResult result = await harness.Client.Orders.CancelAsync(new CancelRequest { OrderUid = "uid" });

        Assert.False(result.Partial);
        Assert.True(result.OrderCancelled);
    }

    [Fact]
    public async Task FlagsAPartialResend()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 206,
            Json = Fake.Json("{\"NumberOfResends\":3}"),
        });

        ResendResult result = await harness.Client.Orders.ResendAsync(new ResendRequest { OrderUid = "uid" });

        Assert.Equal(new ResendResult(3, true), result);
    }

    [Fact]
    public async Task CancelUsesDeleteWithAJsonBody()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json("{\"OrderUID\":\"uid\",\"OrderCancelled\":true,\"Vouchers\":[]}"),
        });

        await harness.Client.Orders.CancelAsync(new CancelRequest { OrderUid = "uid", VoucherId = 7 });

        Assert.Equal("DELETE", harness.First.Method);
        Assert.Equal("/v4/Cancel", harness.First.Path);
        Assert.Equal("{\"OrderUID\":\"uid\",\"VoucherID\":7}", harness.First.Body);
    }
}

public class SearchTests
{
    [Fact]
    public async Task OmitsEveryParameterThatWasNotSupplied()
    {
        TestHarness harness = Fake.Client(
            new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });

        await harness.Client.Orders.SearchAsync(new SearchOrdersRequest { RefId = "ref-1" });

        Assert.Equal("{\"RefID\":\"ref-1\"}", harness.First.Body);
    }

    [Fact]
    public async Task IsTheDocumentedWayToReconcileAfterAnIndeterminateOrder()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json("{\"OrderUID\":\"uid-7\",\"RefID\":\"ref-9\",\"Vouchers\":[]}"),
        });

        SearchOrdersResult found = await harness.Client.Orders.SearchAsync(
            new SearchOrdersRequest { RefId = "ref-9" });

        Assert.Equal("POST", harness.First.Method);
        Assert.Equal("/v4/Search", harness.First.Path);
        Assert.Equal("uid-7", found.OrderUid);
    }

    [Fact]
    public async Task A404FromSearchMeansTheOrderDidNotLand()
    {
        // The API signals an empty result set as 404 with a message, not as an empty 200.
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 404,
            Json = Fake.Json("{\"Status\":404,\"StatusMessage\":\"No vouchers found\"}"),
        });

        HuurayNotFoundException error = await Assert.ThrowsAsync<HuurayNotFoundException>(() =>
            harness.Client.Orders.SearchAsync(new SearchOrdersRequest { RefId = "ref-9" }));

        Assert.Equal("No vouchers found", error.StatusMessage);
    }
}

public class DateFormattingTests
{
    [Fact]
    public async Task WritesDatesAsIso8601InUtc()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });

        await harness.Client.Orders.CreateAsync(OrdersTestData.Base with
        {
            Expires = new DateTimeOffset(2027, 1, 1, 2, 0, 0, TimeSpan.FromHours(2)),
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
        });

        JsonNode body = harness.First.BodyJson!;

        Assert.Equal("2027-01-01T00:00:00.000Z", body["Product"]!["Expires"]!.GetValue<string>());
        Assert.Equal("2026-09-01T09:00:00.000Z", body["DeliveryDatetime"]!.GetValue<string>());
    }
}

public class PurchaseOrderFieldTests
{
    private static readonly string[] Fields =
    {
        "AdditionalReference", "CustomerReference", "ArticleNumber", "Description", "PurchaseOrderFileToken",
    };

    private static CreateOrderRequest Order => OrdersTestData.Base with
    {
        TemplateId = 42,
        Recipients = new[] { new Recipient { Name = "Jane", Email = "jane@example.com" } },
        RefId = "po-ref-1",
    };

    private static SendRewardRequest Reward => new()
    {
        ProductToken = "tok",
        Value = 5000,
        Currency = "DKK",
        Recipient = new Recipient { Name = "Jane", Email = "jane@example.com" },
        TemplateId = 42,
        RefId = "po-ref-1",
    };

    [Theory]
    [InlineData("create")]
    [InlineData("createSync")]
    [InlineData("sendReward")]
    [InlineData("clientSendReward")]
    public async Task SendsEachFieldVerbatim_WithNoClientSideChecks(string method)
    {
        // Longer than 250 characters, padded, holding a script tag, and a token that is no
        // GUID: the API decides all of that, so every value goes out exactly as given.
        string longText = "  " + new string('x', 300) + "  ";
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });

        await Place(harness, method, new[] { longText, "<script>alert(1)</script>", "ART-1", "Ten gift cards ✓", "not-a-guid" });

        JsonNode body = Assert.Single(harness.Calls).BodyJson!;
        Assert.Equal(longText, body["AdditionalReference"]!.GetValue<string>());
        Assert.Equal("<script>alert(1)</script>", body["CustomerReference"]!.GetValue<string>());
        Assert.Equal("ART-1", body["ArticleNumber"]!.GetValue<string>());
        Assert.Equal("Ten gift cards ✓", body["Description"]!.GetValue<string>());
        Assert.Equal("not-a-guid", body["PurchaseOrderFileToken"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("createSync")]
    [InlineData("sendReward")]
    [InlineData("clientSendReward")]
    public async Task OmitsEveryFieldThatIsNotGiven(string method)
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\",\"Vouchers\":[]}") });

        await Place(harness, method, new string?[5]);

        CapturedRequest call = Assert.Single(harness.Calls);
        foreach (string field in Fields)
        {
            Assert.False(call.BodyJson!.AsObject().ContainsKey(field), $"{field} was sent although it was not given");
            Assert.DoesNotContain(field, call.Body!, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task SendsOneFieldWithoutTheOthers(int index)
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });
        string?[] values = new string?[5];
        values[index] = "value-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        await Place(harness, "create", values);

        JsonObject body = harness.First.BodyJson!.AsObject();
        for (int i = 0; i < Fields.Length; i++)
        {
            Assert.Equal(i == index, body.ContainsKey(Fields[i]));
        }

        Assert.Equal(values[index], body[Fields[index]]!.GetValue<string>());
    }

    [Fact]
    public async Task SendsAnEmptyStringRatherThanDroppingIt()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"OrderUID\":\"x\"}") });

        await Place(harness, "create", new[] { string.Empty, string.Empty, string.Empty, string.Empty, string.Empty });

        JsonObject body = harness.First.BodyJson!.AsObject();
        foreach (string field in Fields)
        {
            Assert.Equal(string.Empty, body[field]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task AttachesAnUploadedFileByItsToken()
    {
        TestHarness harness = Fake.ClientWithQueue(new[]
        {
            UploadsTestData.Response201,
            new MockResponse { Status = 202, Json = Fake.Json("{\"OrderUID\":\"x\",\"RefID\":\"po-ref-1\"}") },
        });

        UploadResult upload = await harness.Client.Uploads.CreateAsync(UploadsTestData.Pdf);
        await harness.Client.Orders.CreateAsync(Order with { PurchaseOrderFileToken = upload.Token });

        Assert.Equal(new[] { "/v4/Upload", "/v4/Order" }, new[] { harness.Calls[0].Path, harness.Calls[1].Path });
        Assert.Equal(
            "60050460-7a2d-42a8-a4dd-5cef88ad8374",
            harness.Calls[1].BodyJson!["PurchaseOrderFileToken"]!.GetValue<string>());
    }

    private static Task Place(TestHarness harness, string method, string?[] v)
    {
        CreateOrderRequest order = Order with
        {
            AdditionalReference = v[0],
            CustomerReference = v[1],
            ArticleNumber = v[2],
            Description = v[3],
            PurchaseOrderFileToken = v[4],
        };
        SendRewardRequest reward = Reward with
        {
            AdditionalReference = v[0],
            CustomerReference = v[1],
            ArticleNumber = v[2],
            Description = v[3],
            PurchaseOrderFileToken = v[4],
        };

        return method switch
        {
            "create" => harness.Client.Orders.CreateAsync(order),
            "createSync" => harness.Client.Orders.CreateSyncAsync(order),
            "sendReward" => harness.Client.Orders.SendRewardAsync(reward),
            "clientSendReward" => harness.Client.SendRewardAsync(reward),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
        };
    }
}
