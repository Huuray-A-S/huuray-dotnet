using System;
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
}
