using System;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Huuray.Tests;

public class RedactionTests
{
    [Fact]
    public void RemovesVoucherCodes_TheyAreBearerInstruments()
    {
        string output = Redaction.RedactJson(
            "{\"Vouchers\":[{\"ID\":1,\"Code\":\"REAL-CODE-123\",\"CVV\":\"999\",\"RedeemLink\":\"https://r/abc\"}]}");

        Assert.DoesNotContain("REAL-CODE-123", output, StringComparison.Ordinal);
        Assert.DoesNotContain("999", output, StringComparison.Ordinal);
        Assert.DoesNotContain("https://r/abc", output, StringComparison.Ordinal);
        Assert.Contains(Redaction.SecretMarker, output, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactsRegardlessOfCasing_SoMappedResultsAreCoveredToo()
    {
        string output = Redaction.RedactJson(
            "{\"vouchers\":[{\"code\":\"REAL\",\"cvv\":\"1\",\"redeemLink\":\"https://x\"}]}");

        Assert.DoesNotContain("REAL", output, StringComparison.Ordinal);
        Assert.DoesNotContain("https://x", output, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsIdsAndExpiry_WhichAreSafeAndUsefulInALog()
    {
        JsonNode? output = Redaction.Redact(
            Fake.Json("{\"ID\":42,\"Expires\":\"2027-01-01\",\"Code\":\"SECRET\"}"));

        Assert.Equal(42, output!["ID"]!.GetValue<int>());
        Assert.Equal("2027-01-01", output["Expires"]!.GetValue<string>());
        Assert.Equal(Redaction.SecretMarker, output["Code"]!.GetValue<string>());
    }

    [Fact]
    public void MasksPersonalDataWithoutDestroyingItEntirely()
    {
        JsonNode? output = Redaction.Redact(Fake.Json("{\"Email\":\"jane@example.com\"}"));

        Assert.Equal("ja***om", output!["Email"]!.GetValue<string>());
    }

    [Fact]
    public void MasksShortValuesCompletely()
    {
        JsonNode? output = Redaction.Redact(Fake.Json("{\"Phone\":\"123\"}"));

        Assert.Equal("***", output!["Phone"]!.GetValue<string>());
    }

    [Fact]
    public void MasksCredentials()
    {
        string output = Redaction.RedactJson(
            "{\"apiToken\":\"tok_live_abcdef\",\"apiSecret\":\"shhh-secret\"," +
            "\"X-API-TOKEN\":\"tok_live_abcdef\",\"X-API-HASH\":\"deadbeefdeadbeef\"}");

        Assert.DoesNotContain("tok_live_abcdef", output, StringComparison.Ordinal);
        Assert.DoesNotContain("shhh-secret", output, StringComparison.Ordinal);
        Assert.DoesNotContain("deadbeefdeadbeef", output, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesEmptyAndNullValuesAloneRatherThanInventingAMarker()
    {
        JsonNode? output = Redaction.Redact(Fake.Json("{\"Code\":null,\"CVV\":\"\"}"));

        Assert.Null(output!["Code"]);
        Assert.Equal(string.Empty, output["CVV"]!.GetValue<string>());
    }

    [Fact]
    public void WalksNestedStructures()
    {
        string output = Redaction.RedactJson("{\"a\":{\"b\":{\"c\":[{\"Code\":\"DEEP\"}]}}}");

        Assert.DoesNotContain("DEEP", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StopsAtADepthLimitRatherThanRecursingForever()
    {
        JsonObject root = new();
        JsonObject cursor = root;
        for (int i = 0; i < 40; i++)
        {
            JsonObject next = new();
            cursor["next"] = next;
            cursor = next;
        }

        cursor["Code"] = "DEEP-BUT-BEYOND-THE-LIMIT";

        string output = Redaction.SafeStringify(root);

        Assert.DoesNotContain("DEEP-BUT-BEYOND-THE-LIMIT", output, StringComparison.Ordinal);
        Assert.Contains("too deep", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotMutateTheInput()
    {
        JsonNode input = Fake.Json("{\"Code\":\"KEEP-ME\"}");

        Redaction.Redact(input);

        Assert.Equal("KEEP-ME", input["Code"]!.GetValue<string>());
    }

    [Fact]
    public void SafeStringifyHandlesNull()
    {
        Assert.Equal("null", Redaction.SafeStringify(null));
    }
}

public class VoucherPrintingTests
{
    [Fact]
    public void ToStringNeverRevealsABearerInstrument()
    {
        Voucher voucher = new(
            7,
            "REAL-CODE-123",
            "999",
            "https://redeem.example/abc",
            "2027-01-01",
            new Recipient { Name = "Jane" });

        string printed = voucher.ToString();

        Assert.DoesNotContain("REAL-CODE-123", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("999", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("redeem.example", printed, StringComparison.Ordinal);
        Assert.Contains("Id = 7", printed, StringComparison.Ordinal);
        Assert.Contains("2027-01-01", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void ToStringDoesNotPretendABlankCodeWasPresent()
    {
        Voucher voucher = new(7, null, null, null, null, null);

        string printed = voucher.ToString();

        Assert.DoesNotContain(Redaction.SecretMarker, printed, StringComparison.Ordinal);
    }
}

public class RecordToStringRedactionTests
{
    [Fact]
    public void RecipientToStringMasksContactDetails()
    {
        // The compiler-generated record ToString would print these in the clear.
        Recipient recipient = new()
        {
            Name = "Jane Doe",
            Email = "jane@example.com",
            Phone = "+4512345678",
            RefId = "r-1",
        };

        string rendered = recipient.ToString();

        Assert.DoesNotContain("jane@example.com", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("+4512345678", rendered, StringComparison.Ordinal);
        Assert.Contains("Jane Doe", rendered, StringComparison.Ordinal);
        Assert.Contains("r-1", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void VoucherToStringLeaksNeitherBearerFieldsNorRecipientContact()
    {
        // Voucher.ToString interpolates the recipient, so a leak there is a leak here.
        Voucher voucher = new(
            Id: 1,
            Code: "REAL-CODE-123",
            Cvv: "999",
            RedeemLink: "https://r/abc",
            Expires: "2027-01-01",
            Recipient: new Recipient { Email = "jane@example.com", Phone = "+4512345678" });

        string rendered = voucher.ToString();

        Assert.DoesNotContain("REAL-CODE-123", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("999", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("https://r/abc", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("jane@example.com", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("+4512345678", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientOptionsToStringRedactsTheCredentials_AndPrintsTheRestAsUsual()
    {
        // The compiler-generated record ToString printed ApiToken and ApiSecret in the clear.
        HuurayClientOptions options = new()
        {
            ApiToken = "tok-live-sentinel",
            ApiSecret = "secret-live-sentinel",
            BaseUrl = "https://example.test",
            UserAgent = "my-app/2.0",
        };

        Assert.Equal(
            "HuurayClientOptions { ApiToken = [redacted], ApiSecret = [redacted], BaseUrl = https://example.test, " +
            "HashEncoding = Hex, Timeout = 00:00:30, Retry = , UserAgent = my-app/2.0, NonceFactory =  }",
            options.ToString());
    }

    [Fact]
    public void ClientOptionsLeakNoCredentialThroughAnyFormattingPath()
    {
        HuurayClientOptions options = new()
        {
            ApiToken = "tok-live-sentinel",
            ApiSecret = "secret-live-sentinel",
            Retry = new RetryOptions { MaxRetries = 1 },
            NonceFactory = () => "nonce",
        };

        StringBuilder appended = new StringBuilder().Append(options);
        string[] renderings =
        {
            options.ToString(),
            $"{options}",
            string.Format(CultureInfo.InvariantCulture, "{0}", options),
            appended.ToString(),
            (options with { UserAgent = "copy" }).ToString(),
            new { Options = options }.ToString()!,
        };

        foreach (string rendered in renderings)
        {
            Assert.DoesNotContain("sentinel", rendered, StringComparison.Ordinal);
            Assert.Contains("ApiToken = [redacted], ApiSecret = [redacted]", rendered, StringComparison.Ordinal);
            Assert.Contains("Retry = RetryOptions { MaxRetries = 1,", rendered, StringComparison.Ordinal);
        }
    }
}

public class PurchaseOrderRedactionTests
{
    [Fact]
    public void MasksFileNameAndCustomerReference_BothCarryPersonalNames()
    {
        JsonNode? output = Redaction.Redact(Fake.Json(
            "{\"FileName\":\"purchase-order-jane-doe.pdf\",\"CustomerReference\":\"Jane Doe\",\"ArticleNumber\":\"ART-1\"}"));

        Assert.Equal("pu***df", output!["FileName"]!.GetValue<string>());
        Assert.Equal("Ja***oe", output["CustomerReference"]!.GetValue<string>());
        Assert.Equal("ART-1", output["ArticleNumber"]!.GetValue<string>());
        Assert.Contains("FileName", Redaction.SensitiveFieldNames);
        Assert.Contains("CustomerReference", Redaction.SensitiveFieldNames);
    }

    [Fact]
    public void CreateOrderRequestMasksTheCustomerReference_AndPrintsTheRestAsTheCompilerWould()
    {
        CreateOrderRequest request = new()
        {
            ProductToken = "tok",
            Value = 5000,
            Currency = "DKK",
            Quantity = 2,
            Expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RefId = "ref-1",
            TemplateId = 42,
            PdfTemplateUid = "pdf-1",
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            PersonalMessage = "Thanks",
            Recipients = new[] { new Recipient { Name = "Jane", Email = "jane@example.com" } },
            AdditionalReference = "PO-4711",
            CustomerReference = "Jane Doe",
            ArticleNumber = "ART-1",
            Description = "Ten cards",
            PurchaseOrderFileToken = "60050460-7a2d-42a8-a4dd-5cef88ad8374",
        };

        OrderTwin twin = new(
            request.ProductToken, request.Value, request.Currency, request.Quantity, request.Expires, request.RefId,
            request.TemplateId, request.PdfTemplateUid, request.DeliveryDatetime, request.PersonalMessage,
            request.Recipients, request.AdditionalReference, "Ja***oe", request.ArticleNumber, request.Description,
            request.PurchaseOrderFileToken);

        Assert.Equal(twin.ToString().Replace(nameof(OrderTwin), nameof(CreateOrderRequest), StringComparison.Ordinal), request.ToString());
        Assert.Equal(
            "CreateOrderRequest { ProductToken = tok, Value = 5000, Currency = DKK, Quantity = 1, Expires = , RefId = , " +
            "TemplateId = , PdfTemplateUid = , DeliveryDatetime = , PersonalMessage = , Recipients = , " +
            "AdditionalReference = , CustomerReference = , ArticleNumber = , Description = , PurchaseOrderFileToken =  }",
            OrdersTestData.Base.ToString());

        foreach (string rendered in Renderings(request, (request with { Quantity = 3 }).ToString()))
        {
            Assert.DoesNotContain("Jane Doe", rendered, StringComparison.Ordinal);
            Assert.Contains("CustomerReference = Ja***oe", rendered, StringComparison.Ordinal);
            Assert.Contains("PurchaseOrderFileToken = 60050460-7a2d-42a8-a4dd-5cef88ad8374", rendered, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SendRewardRequestMasksTheCustomerReference_AndPrintsTheRestAsTheCompilerWould()
    {
        SendRewardRequest request = new()
        {
            ProductToken = "tok",
            Value = 5000,
            Currency = "DKK",
            Recipient = new Recipient { Name = "Jane", Email = "jane@example.com" },
            TemplateId = 42,
            PdfTemplateUid = "pdf-1",
            RefId = "ref-1",
            Expires = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            DeliveryDatetime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            PersonalMessage = "Thanks",
            AdditionalReference = "PO-4711",
            CustomerReference = "Jane Doe",
            ArticleNumber = "ART-1",
            Description = "One card",
            PurchaseOrderFileToken = "60050460-7a2d-42a8-a4dd-5cef88ad8374",
        };

        RewardTwin twin = new(
            request.ProductToken, request.Value, request.Currency, request.Recipient, request.TemplateId,
            request.PdfTemplateUid, request.RefId, request.Expires, request.DeliveryDatetime, request.PersonalMessage,
            request.AdditionalReference, "Ja***oe", request.ArticleNumber, request.Description, request.PurchaseOrderFileToken);

        Assert.Equal(twin.ToString().Replace(nameof(RewardTwin), nameof(SendRewardRequest), StringComparison.Ordinal), request.ToString());

        foreach (string rendered in Renderings(request, (request with { Value = 1 }).ToString()))
        {
            Assert.DoesNotContain("Jane Doe", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("jane@example.com", rendered, StringComparison.Ordinal);
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

    /// <summary>The same members in the same order, printed by the compiler.</summary>
    private sealed record OrderTwin(
        string ProductToken,
        int Value,
        string Currency,
        int Quantity,
        DateTimeOffset? Expires,
        string? RefId,
        int? TemplateId,
        string? PdfTemplateUid,
        DateTimeOffset? DeliveryDatetime,
        string? PersonalMessage,
        System.Collections.Generic.IReadOnlyList<Recipient>? Recipients,
        string? AdditionalReference,
        string? CustomerReference,
        string? ArticleNumber,
        string? Description,
        string? PurchaseOrderFileToken);

    /// <summary>The same members in the same order, printed by the compiler.</summary>
    private sealed record RewardTwin(
        string ProductToken,
        int Value,
        string Currency,
        Recipient Recipient,
        int TemplateId,
        string? PdfTemplateUid,
        string RefId,
        DateTimeOffset? Expires,
        DateTimeOffset? DeliveryDatetime,
        string? PersonalMessage,
        string? AdditionalReference,
        string? CustomerReference,
        string? ArticleNumber,
        string? Description,
        string? PurchaseOrderFileToken);
}
