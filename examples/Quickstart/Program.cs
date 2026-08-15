// Quickstart — read-only.
//
// Every call here is safe to run against a live account: nothing is ordered, nothing is
// delivered, nothing is spent.
//
//   HUURAY_API_TOKEN=... HUURAY_API_SECRET=... dotnet run --project examples/Quickstart

using System;
using System.Globalization;
using System.Threading.Tasks;
using Huuray;

string? apiToken = Environment.GetEnvironmentVariable("HUURAY_API_TOKEN");
string? apiSecret = Environment.GetEnvironmentVariable("HUURAY_API_SECRET");

if (string.IsNullOrEmpty(apiToken) || string.IsNullOrEmpty(apiSecret))
{
    Console.Error.WriteLine("Set HUURAY_API_TOKEN and HUURAY_API_SECRET in the environment.");
    return 1;
}

HuurayClient huuray = new(new HuurayClientOptions
{
    ApiToken = apiToken,
    ApiSecret = apiSecret,
});

// 1. What can we spend? Amounts are in minor units: 50000 is 500.00.
ListBalancesResult balances = await huuray.Balances.ListAsync();
foreach (BalanceItem balance in balances.Balances)
{
    Console.WriteLine(string.Format(
        CultureInfo.InvariantCulture,
        "{0}  {1:0.00}{2}",
        balance.Currency,
        balance.Balance / 100m,
        balance.Master ? "  (master)" : string.Empty));
}

// 2. What can we send? Leaving `all` false returns only the products this account can
//    order, and includes the ProductToken you need in order to order them.
ListCatalogueResult catalogue = await huuray.Catalogue.ListAsync(all: false);
Console.WriteLine();
Console.WriteLine($"{catalogue.Products.Count} products available");

CatalogueProduct? first = null;
foreach (CatalogueProduct product in catalogue.Products)
{
    if (product.Active && !string.IsNullOrEmpty(product.ProductToken))
    {
        first = product;
        break;
    }
}

string? token = first?.ProductToken;
if (first is null || string.IsNullOrEmpty(token))
{
    Console.WriteLine("No orderable products on this account.");
    return 0;
}

Console.WriteLine($"Example: {first.BrandName} ({first.Currency}) — token {token}");

// 3. Is it in stock?
CheckStockResult stock = await huuray.Stock.CheckAsync(new CheckStockRequest
{
    ProductToken = token,
});
Console.WriteLine($"Stock: {stock.Stock?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");

// 4. How would it be delivered? Templates are the emails and texts recipients get.
//    An account with none gets a 404, not an empty list.
Console.WriteLine();
try
{
    ListTemplatesResult templates = await huuray.Templates.ListAsync();
    Console.WriteLine($"{templates.Templates.Count} delivery templates");
    foreach (TemplateItem template in templates.Templates)
    {
        Console.WriteLine($"  {template.Id}  {template.Name} ({template.Type}, {template.Language})");
    }
}
catch (HuurayNotFoundException)
{
    Console.WriteLine("No delivery templates on this account.");
}

return 0;

/*
 * Sending an actual reward is one more call. It is commented out because running it
 * spends real money:
 *
 * CreateOrderResult reward = await huuray.SendRewardAsync(new SendRewardRequest
 * {
 *     ProductToken = token,
 *     Value        = 50_00,               // minor units — 50.00
 *     Currency     = first.Currency!,
 *     Recipient    = new Recipient { Name = "Jane Doe", Email = "jane@example.com" },
 *     TemplateId   = 1,
 *     RefId        = "quickstart-demo-1", // your own key, required
 * });
 * Console.WriteLine(reward.OrderUid);
 */
