using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Huuray.Cli;
using Xunit;

namespace Huuray.Tests;

/// <summary>
/// CLI commands run against the fake transport — never the network.
/// </summary>
public class CliTemplatesCommandTests
{
    // Invented values. An account can have no delivery templates and only PDF templates;
    // the command used to print nothing at all for it.
    private const string OnlyPdfTemplates =
        "{\"Templates\":[],\"PDFTemplates\":[" +
        "{\"Uid\":\"invented-pdf-uid-7\",\"Name\":\"Invented PDF template\",\"Type\":\"InventedType\"," +
        "\"Language\":\"en\",\"Country\":null,\"BrandName\":null}]}";

    [Fact]
    public async Task TableOutputListsPdfTemplates_WhenThereAreNoDeliveryTemplates()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json(OnlyPdfTemplates) });
        using StringWriter output = new();

        int exitCode = await Program.TemplatesAsync(harness.Client, asJson: false, output);

        Assert.Equal(0, exitCode);
        Assert.Equal("/v4/Template", Assert.Single(harness.Calls).Path);

        string text = output.ToString();
        Assert.Contains("invented-pdf-uid-7", text, StringComparison.Ordinal);
        Assert.Contains("Invented PDF template", text, StringComparison.Ordinal);

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Assert.Equal("Delivery templates", lines[0]);
        Assert.Equal("(no results)", lines[1]);
        Assert.Equal(string.Empty, lines[2]);
        Assert.Equal("PDF templates", lines[3]);
        Assert.Equal(
            new[] { "uid", "name", "type", "language", "country", "brand" },
            lines[4].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("─", lines[5], StringComparison.Ordinal);

        // Null country and brand print as empty cells, not as "null".
        Assert.Equal("invented-pdf-uid-7  Invented PDF template  InventedType  en", lines[6]);
    }

    [Fact]
    public async Task JsonOutputHoldsBothListsInOneObject()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json(OnlyPdfTemplates) });
        using StringWriter output = new();

        int exitCode = await Program.TemplatesAsync(harness.Client, asJson: true, output);

        Assert.Equal(0, exitCode);

        string text = output.ToString();
        Assert.Contains("invented-pdf-uid-7", text, StringComparison.Ordinal);
        Assert.Contains("Invented PDF template", text, StringComparison.Ordinal);

        JsonObject root = JsonNode.Parse(text)!.AsObject();
        Assert.Equal(2, root.Count);
        Assert.Empty(root["Templates"]!.AsArray());

        JsonObject pdf = Assert.Single(root["PdfTemplates"]!.AsArray())!.AsObject();
        Assert.Equal("invented-pdf-uid-7", pdf["Uid"]!.GetValue<string>());
        Assert.Equal("Invented PDF template", pdf["Name"]!.GetValue<string>());
        Assert.Equal("InventedType", pdf["Type"]!.GetValue<string>());
        Assert.Equal("en", pdf["Language"]!.GetValue<string>());
        Assert.Null(pdf["Country"]);
        Assert.Null(pdf["BrandName"]);
    }
}
