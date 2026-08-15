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
