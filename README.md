# Huuray

#### Easily send gift cards and rewards from .NET

<!-- badges: start -->
[![CI](https://github.com/Huuray-A-S/huuray-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/Huuray-A-S/huuray-dotnet/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Huuray.svg?color=45652a)](https://www.nuget.org/packages/Huuray)
[![License: MIT](https://img.shields.io/badge/license-MIT-45652a.svg)](LICENSE)
[![API v4](https://img.shields.io/badge/Huuray%20API-v4-9dcf73.svg)](https://api.huuray.com/swagger/index.html)
[![Sign up](https://img.shields.io/badge/Huuray-sign%20up-ff5c43.svg)](https://huuray.com/sign-up/)
<!-- badges: end -->

[Huuray](https://huuray.com) is a platform for sending digital gift cards and rewards to recipients in 170+ countries. `Huuray` is the official, slightly-opinionated .NET client for the **Huuray API v4** — with, dare we say, *hurray*-worthy defaults for the parts of a rewards API that are easy to get wrong.

Use it to send employee recognition, customer incentives, survey payouts, referral bonuses, or research participant compensation — without anyone opening a dashboard.

```csharp
using Huuray;

HuurayClient huuray = new(new HuurayClientOptions
{
    ApiToken  = Environment.GetEnvironmentVariable("HUURAY_API_TOKEN")!,
    ApiSecret = Environment.GetEnvironmentVariable("HUURAY_API_SECRET")!,
});

await huuray.SendRewardAsync(new SendRewardRequest
{
    ProductToken = "the-product-you-chose",
    Value        = 50_00,                  // minor units — 50.00
    Currency     = "DKK",
    Recipient    = new Recipient { Name = "Jane Doe", Email = "jane@example.com" },
    TemplateId   = 42,
    RefId        = "payroll-2026-08-jane", // your own key
});
```

- **Async-first and cancellable**, with `HttpClient` injected so it fits `IHttpClientFactory` and your existing handlers.
- **Request signing handled** — the nonce and SHA-512 hash every call needs.
- **Safe by default around money** — orders are never automatically retried, because the API has no idempotency key.
- **Trimming and Native AOT friendly**, with source-generated JSON and **zero third-party dependencies**.

## Requirements

- **.NET 8.0 or .NET 9.0.**
- **A Huuray B2B account.** New to Huuray? [Sign up here](https://huuray.com/sign-up/) — it takes a couple of minutes.
- **API credentials** — an API token and secret for your account. Ask your Huuray contact to enable API access if you do not have them yet.

The full API this client wraps is documented at the [Huuray API v4 reference (Swagger)](https://api.huuray.com/swagger/index.html).

## Install

```bash
dotnet add package Huuray
```

## Getting started

Start with calls that only read. None of these order anything, deliver anything, or spend anything:

```csharp
using Huuray;

HuurayClient huuray = new(new HuurayClientOptions
{
    ApiToken  = Environment.GetEnvironmentVariable("HUURAY_API_TOKEN")!,
    ApiSecret = Environment.GetEnvironmentVariable("HUURAY_API_SECRET")!,
});

// What can you spend? Amounts are in minor units: 50000 is 500.00.
ListBalancesResult balances = await huuray.Balances.ListAsync();

// What can you send? Leaving `all` false returns just your products, with tokens.
ListCatalogueResult catalogue = await huuray.Catalogue.ListAsync();

// How will it be delivered? Templates are the emails and texts recipients get.
ListTemplatesResult templates = await huuray.Templates.ListAsync();
```

In an application with dependency injection, hand the client an `HttpClient` from `IHttpClientFactory` so it shares your connection pool, handlers and policies:

```csharp
builder.Services.AddSingleton(new HuurayClientOptions
{
    ApiToken  = builder.Configuration["Huuray:ApiToken"]!,
    ApiSecret = builder.Configuration["Huuray:ApiSecret"]!,
});

builder.Services.AddHttpClient<HuurayClient>();
```

Or from a terminal, without writing any code:

```bash
export HUURAY_API_TOKEN=... HUURAY_API_SECRET=...
dotnet tool install --global Huuray.Cli
huuray balance
huuray catalogue
```

## Sending a reward

`SendRewardAsync` is one gift card to one recipient — the common case, and exactly one `POST /v4/Order`:

```csharp
CreateOrderResult reward = await huuray.SendRewardAsync(new SendRewardRequest
{
    ProductToken = "the-product-you-chose",
    Value        = 50_00,
    Currency     = "DKK",
    Recipient    = new Recipient { Name = "Jane Doe", Email = "jane@example.com" },
    TemplateId   = 42,
    RefId        = "payroll-2026-08-jane",
});

reward.OrderUid;  // keep this
```

For anything larger, use the orders resource directly:

```csharp
await huuray.Orders.CreateAsync(new CreateOrderRequest
{
    ProductToken = "the-product-you-chose",
    Value        = 25_00,
    Currency     = "DKK",
    Quantity     = 200,
    TemplateId   = 42,
    RefId        = "q3-customer-thankyou",
    Recipients   = recipients,   // 1 recipient, or exactly 200
});
```

## Attaching a purchase order

An order can carry a purchase order file and four references for its invoice. Upload the file first, then pass its token with the order:

```csharp
UploadResult upload = await huuray.Uploads.CreateAsync(new CreateUploadRequest
{
    File        = await File.ReadAllBytesAsync("purchase-order-4711.pdf"),
    FileName    = "purchase-order-4711.pdf",
    ContentType = "application/pdf",        // optional; unset sends application/octet-stream
});

await huuray.Orders.CreateAsync(new CreateOrderRequest
{
    // … product, amount, quantity, delivery …
    PurchaseOrderFileToken = upload.Token,  // consumed by this order
    AdditionalReference    = "PO-4711",
    CustomerReference      = "Jane Doe",
    ArticleNumber          = "ART-1",
    Description            = "Ten gift cards for the sales team",
});
```

All five fields are optional and independent, and `SendRewardRequest` takes them too. Each is accepted only when the matching option is enabled on your account; otherwise the API answers 422, thrown as `HuurayValidationException`. The client sends every value as given and leaves length and content checks to the API.

**Uploads are never retried.** Each upload stores a file that holds one of your account's pending upload slots until an order uses its token or the upload is cleaned up, and no call lists uploads. A timeout or a dropped connection throws the ordinary `HuurayTimeoutException` or `HuurayConnectionException` — not `HuurayIndeterminateOrderException` — and its message says the upload may still have been stored.

## Fetching a gift card PDF

`Pdfs.GetAsync` asks once for the gift card PDF of an order: one document per voucher, or one for them all with `Combine = true`. While the order is still being processed, or a supplier has not delivered a code yet, the API answers `202`, which arrives as `Ready == false` with no documents and a `RetryAfter`. `Pdfs.GetWhenReadyAsync` does the asking for you: it waits `RetryAfter` (30 seconds when none is sent, and never less than 1 second) between signed requests, and throws `HuurayTimeoutException` with the API's last status before a wait would pass `maxWait` (10 minutes by default):

```csharp
PdfResult pdf = await huuray.Pdfs.GetWhenReadyAsync(
    new GetPdfRequest { OrderUid = orderUid },   // VoucherId, PdfTemplateUid and Combine are optional
    maxWait: TimeSpan.FromMinutes(5));

PdfDocument document = pdf.Documents[0];         // FileName, VoucherIds, and Content: the PDF's bytes
```

**The PDF is a bearer instrument.** It carries the redeemable code, so whoever holds the file can spend the gift card. Never log `Content`, and keep it no longer than you need it; `PdfDocument.ToString()` prints its size, never its bytes, and `Redaction` masks any `Content` field.

The API token needs the Search permission, and the API serves only orders with at most three receivers, answering 422 (`HuurayValidationException`) for larger ones; the client leaves both to the API. A PDF can run to several megabytes and take longer than other calls, so Huuray suggests a 100-second `Timeout` for them, for example on a second client. Nothing in this client caps the response size.

## Seven things worth knowing

These are the parts of the API that are easy to get wrong. The client handles each one, but the behaviour is worth understanding.

### 1. Money is in minor units

`Value = 50_00` is 50.00, not 5000.00. Passing a major-unit amount into this field orders **1/100th** of what you meant, so the field is typed `int` and the compiler refuses anything fractional:

```csharp
Value = 50.5     // does not compile
Value = 50_00    // 50.00
```

When the amount reaches you as a `decimal` or a `double` — from a database column, a spreadsheet, a JSON payload — put it through the guard rather than casting:

```csharp
Value = MinorUnits.FromDecimal(amount);   // throws on 50.5, with an explanation
```

One mixup no guard can catch: `50.00m` **is** the whole number 50, so it passes every check and orders 0.50. Always keep amounts as whole numbers of minor units.

### 2. Orders are never retried automatically

`POST /v4/Order` has no idempotency key, so retrying a timed-out order can order a second time — real gift cards, real money. This client never does that. Instead it throws `HuurayIndeterminateOrderException`, and you reconcile:

```csharp
try
{
    await huuray.SendRewardAsync(new SendRewardRequest { RefId = "payroll-2026-08-jane", /* … */ });
}
catch (HuurayIndeterminateOrderException)
{
    // Do NOT retry. Find out what actually happened.
    try
    {
        SearchOrdersResult found = await huuray.Orders.SearchAsync(
            new SearchOrdersRequest { RefId = "payroll-2026-08-jane" });

        if (found.OrderUid is not null)
        {
            // It landed. Nothing more to do.
        }
        else
        {
            // No match — it did not land. Safe to send again with the same RefId.
        }
    }
    catch (HuurayNotFoundException)
    {
        // The API answers 404 when nothing matches: the order did not land.
        // Safe to send again with the same RefId.
    }
    // Anything else means the lookup itself failed and the outcome is still unknown.
}
```

This is why `SendRewardAsync` requires a `RefId` even though the API treats it as optional: without one, an order that times out cannot be looked up.

Reads *are* retried — with backoff, on connection failures and 5xx.

### 3. Synchronous and asynchronous orders are different calls

| | `Orders.CreateAsync` | `Orders.CreateSyncAsync` |
|---|---|---|
| Sends | `Sync: false` | `Sync: true` |
| Quantity | no documented limit | max 25 |
| Returns | `OrderUid` only | `OrderUid` **and vouchers** |
| Delivery | Huuray sends via your template | you handle the codes |

"No documented limit" is precise, not a promise: the API specification states a cap only for synchronous orders. It says nothing about an asynchronous maximum, so this client imposes none — but a very large single order is untested territory. Ask your Huuray contact before relying on one.

They are separate methods because their return types differ. Reading `Vouchers` on an asynchronous order is a mistake the type system should catch, not a runtime surprise.

### 4. `206 Partial Content` is a real outcome

Cancel and resend can partly succeed. Checking only that the request "worked" will miss it:

```csharp
CancelResult result = await huuray.Orders.CancelAsync(new CancelRequest { OrderUid = orderUid });

if (result.Partial)
{
    int failed = result.Vouchers.Count(v => !v.Cancelled);
    logger.LogWarning("{Count} vouchers could not be cancelled", failed);
}
```

### 5. Voucher codes are blank unless your account allows them

`Voucher.Code`, `Voucher.Cvv` and `Voucher.RedeemLink` are returned only if **`ReturnCode` is enabled on your B2B account**. Otherwise they come back empty and Huuray delivers the codes for you. If you need codes returned to your own system, ask your Huuray contact to enable it.

This client never logs a code. `Voucher.ToString()` redacts them, so an accidental interpolation cannot leak one, and `Redaction` is public so you can do the same with your own payloads:

```csharp
logger.LogInformation("order complete {Payload}", Redaction.RedactJson(json));   // codes stripped
```

### 6. "Nothing found" can be a 404, not an empty list

The API can signal "nothing found" as HTTP 404: `Orders.SearchAsync()` with no match throws `HuurayNotFoundException` rather than returning an empty result. `POST /v4/Template` has been observed live both ways (see [CHANGELOG](CHANGELOG.md)): a 404 (*"There were no active templates"*) when the account had no templates, which `Templates.ListAsync()` throws as `HuurayNotFoundException`, and a 200 with an empty `Templates` list for an account with PDF templates but no email or SMS templates. Handle both:

```csharp
IReadOnlyList<Template> templates = Array.Empty<Template>();
try
{
    templates = (await huuray.Templates.ListAsync()).Templates;   // can be empty
}
catch (HuurayNotFoundException)
{
    // the 404 observed when the account had no templates
}
```

### 7. Authentication, and what a 401 usually means

Every request carries three headers, all built for you:

| Header | Value |
|---|---|
| `X-API-TOKEN` | your API token |
| `X-API-NONCE` | a random value, **single-use within 60 days, max 50 characters** |
| `X-API-HASH` | SHA-512 of ( API secret + nonce ) |

Nonces are 24 random bytes as base64url — 32 characters, comfortably under the limit. Avoid rolling your own: 32-byte hex is 64 characters and is silently rejected, and timestamps collide under concurrency.

**If you get a 401 with credentials you know are correct**, the digest encoding is the thing to try. The API specification states the construction but not the encoding, so this client defaults to lowercase hex and lets you change it:

```csharp
new HuurayClient(new HuurayClientOptions { ApiToken = t, ApiSecret = s, HashEncoding = HashEncoding.Base64 });
// Hex (default) | HexUpper | Base64 | Base64Url
```

## API coverage

All eleven v4 operations, and nothing else. Every method maps to one operation in the [Swagger reference](https://api.huuray.com/swagger/index.html):

| Method | Endpoint |
|---|---|
| `Balances.ListAsync()` | `GET /v4/Balance` |
| `Catalogue.ListAsync(all)` | `POST /v4/Catalogue` |
| `Templates.ListAsync()` | `POST /v4/Template` |
| `Stock.CheckAsync(…)` | `POST /v4/Stock` |
| `ExchangeRates.GetAsync(from, to)` | `GET /v4/ExchangeRates` |
| `Orders.CreateAsync(…)` | `POST /v4/Order` (`Sync: false`) |
| `Orders.CreateSyncAsync(…)` | `POST /v4/Order` (`Sync: true`) |
| `Orders.SendRewardAsync(…)` | `POST /v4/Order`, one recipient |
| `Orders.SearchAsync(…)` | `POST /v4/Search` |
| `Orders.ResendAsync(…)` | `POST /v4/Resend` |
| `Orders.CancelAsync(…)` | `DELETE /v4/Cancel` |
| `Uploads.CreateAsync(…)` | `POST /v4/Upload` (`multipart/form-data`) |
| `Pdfs.GetAsync(…)` | `POST /v4/Pdf` |
| `Pdfs.GetWhenReadyAsync(…)` | `POST /v4/Pdf`, repeated while the answer is `202` |

Need something not covered? `RequestAsync` signs any call for you:

```csharp
JsonNode? found = await huuray.RequestAsync(
    HttpMethod.Post,
    "/v4/Search",
    new JsonObject { ["RefID"] = "payroll-2026-08-jane" });
```

**This client targets API v4 only.** Field names match the Huuray API reference exactly, differing only in casing (`OrderUID` → `OrderUid`), so anything you read in the API documentation maps straight across.

## Errors

Every exception derives from `HuurayException`.

| Class | When |
|---|---|
| `HuurayConfigurationException` | missing or invalid client options |
| `HuurayConnectionException` | the request never reached the API, or the response was unusable |
| `HuurayTimeoutException` | the request exceeded `Timeout`, or `Pdfs.GetWhenReadyAsync` gave up within `maxWait` |
| `HuurayAuthException` | 401 or 403 — see *Authentication* above |
| `HuurayNotFoundException` | 404 |
| `HuurayValidationException` | 422 |
| `HuurayServerException` | 5xx |
| `HuurayApiException` | any other non-2xx; the base for the four above |
| `HuurayIndeterminateOrderException` | an order whose outcome is unknown — **do not retry** |

API exceptions carry `HttpStatus`, `Status`, `StatusMessage`, and the parsed `Body`. The client reads `StatusMessage` and falls back to the deprecated `Message`. The retained `Body` is redacted, so logging an exception can never leak a voucher code.

Argument problems that this client catches before anything is sent — a fractional amount, a recipient count that is neither 1 nor `Quantity` when `TemplateId` is set, a synchronous order over 25, an upload file name that cannot be sent in a header — throw `ArgumentException`. They are programming mistakes, not API responses.

## Client options

```csharp
new HuurayClient(
    new HuurayClientOptions
    {
        ApiToken     = "…",                     // required
        ApiSecret    = "…",                     // required
        BaseUrl      = "https://api.huuray.com",// default
        HashEncoding = HashEncoding.Hex,        // Hex | HexUpper | Base64 | Base64Url
        Timeout      = TimeSpan.FromSeconds(30),
        Retry        = new RetryOptions { MaxRetries = 2 },
        UserAgent    = "my-app/2.0",
        NonceFactory = null,                    // supply your own if you must
    },
    httpClient);                                // optional; IHttpClientFactory-friendly
```

## CLI

Read-only by design. Ordering, resending and cancelling move real value and belong in reviewed code, not a shell one-liner. Voucher codes are never printed.

```bash
dotnet tool install --global Huuray.Cli
huuray balance
huuray catalogue --all
huuray templates
huuray stock --token <token> --value 5000
huuray rates --from EUR --to DKK
huuray search --ref-id payroll-2026-08-jane
huuray --help
```

## Examples

- [`examples/Quickstart`](examples/Quickstart/Program.cs) — read-only tour, safe to run
- [`examples/ReconcileAfterTimeout`](examples/ReconcileAfterTimeout/Program.cs) — recovering from an order whose outcome is unknown

## Further reading

- [Huuray API v4 reference (Swagger)](https://api.huuray.com/swagger/index.html) — the specification this client is built against
- [Sign up for a Huuray B2B account](https://huuray.com/sign-up/) — if you do not have one yet
- [Contributing](.github/CONTRIBUTING.md) — including the note on **spec fidelity**: this client deliberately exposes nothing the API does not document, and that rule is enforced by tests
- [Changelog](CHANGELOG.md)

## Feedback

Found a bug, or something in this library that could be friendlier? Please [file an issue](https://github.com/Huuray-A-S/huuray-dotnet/issues) or start a [discussion](https://github.com/Huuray-A-S/huuray-dotnet/discussions). This repository does not accept external pull requests — see [CONTRIBUTING.md](.github/CONTRIBUTING.md) for why.

For the API itself, your account, or a live production problem, contact your Huuray representative — see [SUPPORT.md](.github/SUPPORT.md) for which channel to use. Never open a public issue for a security vulnerability; see [SECURITY.md](.github/SECURITY.md).

## Code of Conduct

Please note that this project is released with a [Contributor Code of Conduct](.github/CODE_OF_CONDUCT.md). By contributing to this project, you agree to abide by its terms.

<p align="center">
  <img src="https://raw.githubusercontent.com/Huuray-A-S/huuray-dotnet/main/.github/assets/huuray-logo.svg" width="96" alt="Huuray"/><br/>
  <sub>Made with 💚 in Denmark by <a href="https://huuray.com">Huuray A/S</a> · <a href="LICENSE">MIT</a></sub>
</p>
