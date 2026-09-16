using System;
using System.Threading.Tasks;
using Xunit;

namespace Huuray.Tests;

public class BalancesTests
{
    [Fact]
    public async Task MapsBalanceRowsAndKeepsAmountsInMinorUnits()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json(
                "{\"Balances\":[{\"Currency\":\"DKK\",\"Balance\":50000,\"Master\":true}," +
                "{\"Currency\":\"EUR\",\"Balance\":1234,\"Master\":false}]}"),
        });

        ListBalancesResult result = await harness.Client.Balances.ListAsync();

        Assert.Equal("GET", harness.First.Method);
        Assert.Equal("/v4/Balance", harness.First.Path);
        Assert.True(harness.First.BodyOmitted);
        Assert.Equal(
            new[]
            {
                new BalanceItem("DKK", 50_000, true),
                new BalanceItem("EUR", 1234, false),
            },
            result.Balances);
    }

    [Fact]
    public async Task ReturnsAnEmptyListWhenTheApiSendsNull()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"Balances\":null}") });

        ListBalancesResult result = await harness.Client.Balances.ListAsync();

        Assert.Empty(result.Balances);
    }
}

public class CatalogueTests
{
    [Fact]
    public async Task DefaultsAllToFalse_YourProductsWithTokensAndDiscount()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"Products\":[]}") });

        await harness.Client.Catalogue.ListAsync();

        Assert.Equal("{\"All\":false}", harness.First.Body);
    }

    [Fact]
    public async Task PassesAllThroughWhenRequestingTheWholeCatalogue()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"Products\":[]}") });

        await harness.Client.Catalogue.ListAsync(all: true);

        Assert.Equal("{\"All\":true}", harness.First.Body);
    }

    [Fact]
    public async Task MapsProductFields()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json(
                "{\"Products\":[{\"ProductToken\":\"tok\",\"BrandName\":\"Example\",\"CountryCode\":\"DK\"," +
                "\"Discount\":4.5,\"Currency\":\"DKK\",\"Active\":true}]}"),
        });

        ListCatalogueResult result = await harness.Client.Catalogue.ListAsync();

        CatalogueProduct product = Assert.Single(result.Products);
        Assert.Equal("tok", product.ProductToken);
        Assert.Equal("Example", product.BrandName);
        Assert.Equal("DK", product.CountryCode);
        Assert.Equal(4.5m, product.Discount);
        Assert.Equal("DKK", product.Currency);
        Assert.True(product.Active);
    }
}

public class TemplatesTests
{
    [Fact]
    public async Task SendsNoRequestBody_BecauseTheSpecDeclaresNone()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"Templates\":[]}") });

        await harness.Client.Templates.ListAsync();

        Assert.Equal("POST", harness.First.Method);
        Assert.Equal("/v4/Template", harness.First.Path);
        Assert.True(harness.First.BodyOmitted);
    }

    [Fact]
    public async Task MapsTemplateFields()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json(
                "{\"Templates\":[{\"Id\":42,\"Name\":\"Default\",\"Type\":\"Email\",\"Language\":\"da\"}]}"),
        });

        ListTemplatesResult result = await harness.Client.Templates.ListAsync();

        TemplateItem template = Assert.Single(result.Templates);
        Assert.Equal(42, template.Id);
        Assert.Equal("Default", template.Name);
        Assert.Equal("Email", template.Type);
        Assert.Equal("da", template.Language);
    }

    [Fact]
    public async Task MapsPdfTemplateFields_EvenWhenThereAreNoDeliveryTemplates()
    {
        // The regression: an account whose templates are all PDF templates answers with an
        // empty Templates list, and mapping only Templates silently dropped every PDF template.
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json(
                "{\"Templates\":[],\"PDFTemplates\":[" +
                "{\"Uid\":\"pdf-uid-1\",\"Name\":\"Example PDF\",\"Type\":\"ExampleType\",\"Language\":\"en\"," +
                "\"Country\":\"Exampleland\",\"BrandName\":\"Example Brand\"}," +
                "{\"Uid\":\"pdf-uid-2\",\"Name\":\"Any country, any brand\",\"Type\":\"ExampleType\"," +
                "\"Language\":\"da\",\"Country\":null,\"BrandName\":null}]," +
                "\"Status\":200,\"Message\":null,\"StatusMessage\":null}"),
        });

        ListTemplatesResult result = await harness.Client.Templates.ListAsync();

        Assert.Empty(result.Templates);
        Assert.Equal(
            new[]
            {
                new PdfTemplate("pdf-uid-1", "Example PDF", "ExampleType", "en", "Exampleland", "Example Brand"),
                new PdfTemplate("pdf-uid-2", "Any country, any brand", "ExampleType", "da", null, null),
            },
            result.PdfTemplates);
    }

    [Theory]
    [InlineData("{\"Templates\":[{\"Id\":42,\"Name\":\"Default\"}],\"PDFTemplates\":null}")]
    [InlineData("{\"Templates\":[{\"Id\":42,\"Name\":\"Default\"}]}")]
    public async Task ReturnsAnEmptyPdfTemplateListWhenTheApiSendsNullOrOmitsIt(string json)
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json(json) });

        ListTemplatesResult result = await harness.Client.Templates.ListAsync();

        Assert.NotNull(result.PdfTemplates);
        Assert.Empty(result.PdfTemplates);
        Assert.Single(result.Templates);
    }

    [Fact]
    public async Task AnAccountWithNoTemplatesGetsA404_NotAnEmptyList()
    {
        // Observed live: /v4/Template answers 404 "There were no active templates".
        TestHarness harness = Fake.Client(new MockResponse
        {
            Status = 404,
            Json = Fake.Json("{\"Status\":404,\"StatusMessage\":\"There were no active templates\"}"),
        });

        HuurayNotFoundException error =
            await Assert.ThrowsAsync<HuurayNotFoundException>(() => harness.Client.Templates.ListAsync());

        Assert.Equal("There were no active templates", error.StatusMessage);
    }
}

public class StockTests
{
    [Fact]
    public async Task OmitsValueWhenNotSupplied()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"Stock\":10}") });

        CheckStockResult result = await harness.Client.Stock.CheckAsync(
            new CheckStockRequest { ProductToken = "tok" });

        Assert.Equal("{\"ProductToken\":\"tok\"}", harness.First.Body);
        Assert.Equal(10, result.Stock);
    }

    [Fact]
    public async Task SendsValueWhenSupplied()
    {
        TestHarness harness = Fake.Client(new MockResponse { Json = Fake.Json("{\"Stock\":10}") });

        await harness.Client.Stock.CheckAsync(new CheckStockRequest { ProductToken = "tok", Value = 5000 });

        Assert.Equal("{\"ProductToken\":\"tok\",\"Value\":5000}", harness.First.Body);
    }
}

public class ExchangeRatesTests
{
    [Fact]
    public async Task SendsTheCurrenciesAsQueryParameters()
    {
        TestHarness harness = Fake.Client(new MockResponse
        {
            Json = Fake.Json("{\"ExchangeRate\":7.46,\"Spread\":2}"),
        });

        ExchangeRateResult result = await harness.Client.ExchangeRates.GetAsync("EUR", "DKK");

        Assert.Equal("GET", harness.First.Method);
        Assert.Equal("/v4/ExchangeRates", harness.First.Path);
        Assert.Equal("EUR", harness.First.Query["FromCurrency"]);
        Assert.Equal("DKK", harness.First.Query["ToCurrency"]);
        Assert.Equal(new ExchangeRateResult(7.46, 2), result);
    }

    [Fact]
    public async Task RejectsANullCurrency()
    {
        TestHarness harness = Fake.Client();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            harness.Client.ExchangeRates.GetAsync(null!, "DKK"));

        Assert.Empty(harness.Calls);
    }
}
