// The pattern that matters most: recovering from an order whose outcome is unknown.
//
// POST /v4/Order has no idempotency key. If the request times out or the server returns a
// 5xx, the order may or may not have been created — and retrying can order a second time,
// for real money.
//
// So this SDK never retries an order. It throws HuurayIndeterminateOrderException and
// expects you to reconcile, which is only possible if you sent a RefId you can look up.
// That is why SendRewardAsync requires one.
//
//   HUURAY_API_TOKEN=... HUURAY_API_SECRET=... dotnet run --project examples/ReconcileAfterTimeout

using System;
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

// A key from your own system — stable, unique, and meaningful to you.
const string RefId = "payroll-2026-08-jane";

try
{
    CreateOrderResult reward = await huuray.SendRewardAsync(new SendRewardRequest
    {
        ProductToken = "REPLACE_WITH_A_REAL_TOKEN",
        Value = 50_00, // minor units — 50.00
        Currency = "DKK",
        Recipient = new Recipient { Name = "Jane Doe", Email = "jane@example.com" },
        TemplateId = 1,
        RefId = RefId,
    });

    Console.WriteLine($"Ordered. OrderUid={reward.OrderUid}");
    return 0;
}
catch (HuurayIndeterminateOrderException)
{
    // Do NOT retry the order here. Find out what actually happened.
    Console.Error.WriteLine("Order outcome unknown. Reconciling by RefId instead of retrying.");

    try
    {
        SearchOrdersResult found = await huuray.Orders.SearchAsync(new SearchOrdersRequest { RefId = RefId });

        if (found.OrderUid is not null)
        {
            Console.WriteLine($"It landed after all: OrderUid={found.OrderUid}. Nothing more to do.");
            return 0;
        }

        Console.WriteLine("No order exists for this RefId. Safe to send again with the same RefId.");
        return 0;
    }
    catch (HuurayNotFoundException)
    {
        // The API signals an empty result set with 404 — its way of saying no order
        // matched. That IS the answer: the order did not land.
        Console.WriteLine("No order exists for this RefId (404). Safe to send again with the same RefId.");
        return 0;
    }

    // Any other exception from the lookup means the lookup itself failed and the outcome
    // is STILL unknown. It is deliberately not caught here: a failed lookup must never be
    // treated as "the order did not land".
}
