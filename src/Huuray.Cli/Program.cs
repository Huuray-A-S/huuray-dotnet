using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Huuray.Cli;

/// <summary>
/// Read-only command line interface for the Huuray API v4.
/// </summary>
/// <remarks>
/// Deliberately limited to operations that cannot move value: there is no ordering,
/// resending, or cancelling here. Sending real gift cards from a shell one-liner is too
/// easy to do by accident, and a mistyped quantity is money.
/// <para>Voucher codes are never printed, whatever the account settings allow.</para>
/// </remarks>
internal static class Program
{
    private const string Usage = """
huuray — read-only CLI for the Huuray API v4

  Usage
    huuray <command> [options]

  Commands
    balance                       Available balances, per currency
    catalogue [--all]             Products you can order (--all for the full catalogue)
    templates                     Delivery templates on your account
    stock --token <t> [--value N] Stock for a product (value in minor units)
    rates --from EUR --to DKK     Exchange rate and spread
    search [--ref-id R] [--order-uid U] [--voucher-id N]
                                  Look up vouchers from previous orders

  Options
    --json                        Machine-readable output
    -h, --help                    This text

  Credentials, from the environment
    HUURAY_API_TOKEN
    HUURAY_API_SECRET
    HUURAY_BASE_URL               Optional; defaults to https://api.huuray.com

  Ordering, resending and cancelling are not available here. They move real
  value, so they belong in code you have reviewed. See the README.

  Voucher codes are never printed by this CLI.
""";

    internal static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (HuurayApiException exception)
        {
            Console.Error.WriteLine("Error: " + exception.Message);
            if (exception.HttpStatus is 401 or 403)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(
                    "If the credentials are correct, the X-API-HASH encoding may differ from this");
                Console.Error.WriteLine("client's default. See the README section \"Authentication\".");
            }

            return 1;
        }
        catch (HuurayException exception)
        {
            Console.Error.WriteLine("Error: " + exception.Message);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        ParsedArgs parsed = CliArgs.Parse(args);

        // Help must work before anything else, including the credential check.
        if (CliArgs.WantsHelp(parsed.Flags) || parsed.Command is null)
        {
            Console.WriteLine(Usage);
            return CliArgs.WantsHelp(parsed.Flags) ? 0 : 1;
        }

        string? apiToken = Environment.GetEnvironmentVariable("HUURAY_API_TOKEN");
        string? apiSecret = Environment.GetEnvironmentVariable("HUURAY_API_SECRET");
        if (string.IsNullOrEmpty(apiToken) || string.IsNullOrEmpty(apiSecret))
        {
            Console.Error.WriteLine("Set HUURAY_API_TOKEN and HUURAY_API_SECRET in the environment.");
            Console.Error.WriteLine("Run \"huuray --help\" for usage.");
            return 1;
        }

        HuurayClient client = new(new HuurayClientOptions
        {
            ApiToken = apiToken,
            ApiSecret = apiSecret,
            BaseUrl = Environment.GetEnvironmentVariable("HUURAY_BASE_URL"),
            UserAgent = "huuray-cli",
        });

        bool asJson = CliArgs.HasFlag(parsed.Flags, "json");

        switch (parsed.Command)
        {
            case "balance":
                return await BalanceAsync(client, asJson).ConfigureAwait(false);

            case "catalogue":
                return await CatalogueAsync(client, asJson, CliArgs.HasFlag(parsed.Flags, "all")).ConfigureAwait(false);

            case "templates":
                return await TemplatesAsync(client, asJson).ConfigureAwait(false);

            case "stock":
                return await StockAsync(
                        client,
                        asJson,
                        CliArgs.RequireFlag(parsed.Flags, "token"),
                        CliArgs.OptionalMinorUnits(parsed.Flags, "value"))
                    .ConfigureAwait(false);

            case "rates":
                return await RatesAsync(
                        client,
                        asJson,
                        CliArgs.RequireFlag(parsed.Flags, "from"),
                        CliArgs.RequireFlag(parsed.Flags, "to"))
                    .ConfigureAwait(false);

            case "search":
                return await SearchAsync(
                        client,
                        asJson,
                        CliArgs.OptionalString(parsed.Flags, "ref-id"),
                        CliArgs.OptionalString(parsed.Flags, "order-uid"),
                        CliArgs.OptionalInt(parsed.Flags, "voucher-id"))
                    .ConfigureAwait(false);

            default:
                Console.Error.WriteLine($"Unknown command \"{parsed.Command}\". Run \"huuray --help\".");
                return 1;
        }
    }

    private static async Task<int> BalanceAsync(HuurayClient client, bool asJson)
    {
        ListBalancesResult result = await client.Balances.ListAsync().ConfigureAwait(false);

        JsonArray json = new();
        List<IReadOnlyList<KeyValuePair<string, string>>> rows = new();
        foreach (BalanceItem balance in result.Balances)
        {
            json.Add(new JsonObject
            {
                ["Currency"] = balance.Currency,
                ["Balance"] = balance.Balance,
                ["Master"] = balance.Master,
            });

            rows.Add(new[]
            {
                new KeyValuePair<string, string>("currency", balance.Currency ?? string.Empty),
                new KeyValuePair<string, string>(
                    "balance (minor units)",
                    balance.Balance.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("master", balance.Master ? "yes" : string.Empty),
            });
        }

        Emit(asJson, json, rows);
        return 0;
    }

    private static async Task<int> CatalogueAsync(HuurayClient client, bool asJson, bool all)
    {
        ListCatalogueResult result = await client.Catalogue.ListAsync(all).ConfigureAwait(false);

        JsonArray json = new();
        List<IReadOnlyList<KeyValuePair<string, string>>> rows = new();
        foreach (CatalogueProduct product in result.Products)
        {
            json.Add(new JsonObject
            {
                ["ProductToken"] = product.ProductToken,
                ["BrandName"] = product.BrandName,
                ["CountryCode"] = product.CountryCode,
                ["Currency"] = product.Currency,
                ["Discount"] = product.Discount,
                ["Denominations"] = product.Denominations,
                ["Active"] = product.Active,
            });

            rows.Add(new[]
            {
                new KeyValuePair<string, string>("token", product.ProductToken ?? "(not returned with --all)"),
                new KeyValuePair<string, string>("brand", product.BrandName ?? string.Empty),
                new KeyValuePair<string, string>("country", product.CountryCode ?? string.Empty),
                new KeyValuePair<string, string>("currency", product.Currency ?? string.Empty),
                new KeyValuePair<string, string>(
                    "discount",
                    product.Discount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                new KeyValuePair<string, string>("active", product.Active ? "yes" : "no"),
            });
        }

        Emit(asJson, json, rows);
        return 0;
    }

    private static async Task<int> TemplatesAsync(HuurayClient client, bool asJson)
    {
        ListTemplatesResult result = await client.Templates.ListAsync().ConfigureAwait(false);

        JsonArray json = new();
        List<IReadOnlyList<KeyValuePair<string, string>>> rows = new();
        foreach (TemplateItem template in result.Templates)
        {
            json.Add(new JsonObject
            {
                ["Id"] = template.Id,
                ["Name"] = template.Name,
                ["Type"] = template.Type,
                ["Language"] = template.Language,
                ["Sender"] = template.Sender,
                ["Subject"] = template.Subject,
            });

            rows.Add(new[]
            {
                new KeyValuePair<string, string>("id", template.Id.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("name", template.Name ?? string.Empty),
                new KeyValuePair<string, string>("type", template.Type ?? string.Empty),
                new KeyValuePair<string, string>("language", template.Language ?? string.Empty),
                new KeyValuePair<string, string>("sender", template.Sender ?? string.Empty),
            });
        }

        Emit(asJson, json, rows);
        return 0;
    }

    private static async Task<int> StockAsync(HuurayClient client, bool asJson, string token, int? value)
    {
        CheckStockResult result = await client.Stock
            .CheckAsync(new CheckStockRequest { ProductToken = token, Value = value })
            .ConfigureAwait(false);

        JsonObject json = new() { ["Stock"] = result.Stock };
        List<IReadOnlyList<KeyValuePair<string, string>>> rows = new()
        {
            new[]
            {
                new KeyValuePair<string, string>(
                    "stock",
                    result.Stock?.ToString(CultureInfo.InvariantCulture) ?? "unknown"),
            },
        };

        Emit(asJson, json, rows);
        return 0;
    }

    private static async Task<int> RatesAsync(HuurayClient client, bool asJson, string from, string to)
    {
        ExchangeRateResult result = await client.ExchangeRates.GetAsync(from, to).ConfigureAwait(false);

        JsonObject json = new()
        {
            ["ExchangeRate"] = result.ExchangeRate,
            ["Spread"] = result.Spread,
        };

        List<IReadOnlyList<KeyValuePair<string, string>>> rows = new()
        {
            new[]
            {
                new KeyValuePair<string, string>(
                    "rate",
                    result.ExchangeRate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                new KeyValuePair<string, string>(
                    "spread (%)",
                    result.Spread?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
            },
        };

        Emit(asJson, json, rows);
        return 0;
    }

    private static async Task<int> SearchAsync(
        HuurayClient client,
        bool asJson,
        string? refId,
        string? orderUid,
        int? voucherId)
    {
        SearchOrdersResult result = await client.Orders
            .SearchAsync(new SearchOrdersRequest { RefId = refId, OrderUid = orderUid, VoucherId = voucherId })
            .ConfigureAwait(false);

        // No code, cvv or redeem link is put into either output path — not even a
        // redaction marker, because a column of markers would wrongly imply that codes
        // were present in the response.
        JsonArray vouchers = new();
        List<IReadOnlyList<KeyValuePair<string, string>>> rows = new();
        foreach (Voucher voucher in result.Vouchers)
        {
            JsonObject? recipient = null;
            if (voucher.Recipient is not null)
            {
                recipient = new JsonObject
                {
                    ["Name"] = voucher.Recipient.Name,
                    ["RefID"] = voucher.Recipient.RefId,
                };
            }

            vouchers.Add(new JsonObject
            {
                ["ID"] = voucher.Id,
                ["Expires"] = voucher.Expires,
                ["Recipient"] = recipient,
            });

            rows.Add(new[]
            {
                new KeyValuePair<string, string>(
                    "voucher id",
                    voucher.Id?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                new KeyValuePair<string, string>("expires", voucher.Expires ?? string.Empty),
                new KeyValuePair<string, string>(
                    "recipient",
                    voucher.Recipient?.Name ?? voucher.Recipient?.RefId ?? string.Empty),
            });
        }

        JsonObject json = new()
        {
            ["OrderUID"] = result.OrderUid,
            ["RefID"] = result.RefId,
            ["Vouchers"] = vouchers,
        };

        Emit(asJson, json, rows);

        if (!asJson)
        {
            Console.WriteLine();
            Console.WriteLine($"order: {result.OrderUid ?? "(none)"}  ref: {result.RefId ?? string.Empty}");
            Console.WriteLine("(voucher codes are never printed by this CLI)");
        }

        return 0;
    }

    /// <summary>
    /// Writes one result, redacted on both paths.
    /// </summary>
    private static void Emit(
        bool asJson,
        JsonNode json,
        IReadOnlyList<IReadOnlyList<KeyValuePair<string, string>>> rows)
    {
        if (asJson)
        {
            Console.WriteLine(Redaction.SafeStringify(json, indented: true));
            return;
        }

        Console.WriteLine(CliArgs.Table(rows));
    }
}
