# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Purchase order files and references on orders.** `Uploads.CreateAsync(CreateUploadRequest)`
  sends `POST /v4/Upload` as `multipart/form-data` with one `File` part and returns
  `UploadResult` (`Token`, `FileName`, `ContentType`, `Size`). `CreateOrderRequest` and
  `SendRewardRequest` accept `AdditionalReference`, `CustomerReference`, `ArticleNumber`,
  `Description` and `PurchaseOrderFileToken`: omitted when unset, sent as given otherwise.
- **Uploads are never retried.** A timeout or a dropped connection throws the ordinary
  `HuurayTimeoutException` or `HuurayConnectionException`, whose message says the upload may
  still have been stored and may hold a pending upload slot.
- Without a `ContentType` the file part is sent as `application/octet-stream`. A `FileName` that
  is null, empty or only whitespace, or holds a double quote or a control character, and a
  `ContentType` that is not a media type throw `ArgumentException` before anything is sent,
  without quoting the value.
- `CreateUploadRequest` and `UploadResult` mask `FileName` in `ToString()`, and
  `CreateUploadRequest.ToString()` prints the file as `[N bytes]`.
- The request-conformance gate validates `multipart/form-data` bodies part by part and fails
  closed on any multipart shape it does not understand.
- **Gift card PDFs.** `Pdfs.GetAsync(GetPdfRequest)` sends `POST /v4/Pdf` with `OrderUid` and,
  when set, `VoucherId`, `PdfTemplateUid` and `Combine`, and returns `PdfResult` (`Ready`,
  `OrderUid`, `Documents`, `RetryAfter`). Each `PdfDocument` carries `VoucherIds`,
  `PdfTemplateUid`, `FileName`, `ContentType` and `Content`, the PDF decoded from base64.
  `Ready` is `true` on 200 and `false` on 202, the API's "not ready yet"; `RetryAfter` is the
  `Retry-After` header in whole seconds, or null when absent or not whole seconds.
- `Pdfs.GetWhenReadyAsync(GetPdfRequest, maxWait, CancellationToken)` asks again after each 202
  with a new signed request, waiting `RetryAfter` or 30 seconds, and throws
  `HuurayTimeoutException` with the last `StatusMessage` before a wait would pass `maxWait`
  (default `PdfsResource.DefaultMaxWait`, 10 minutes). Any other answer ends the wait.
- `POST /v4/Pdf` is a read: retried on connection failures and 5xx with a fresh nonce. A 200
  whose `Content` is not valid base64 throws `HuurayConnectionException`, without quoting it.
  The order id, template id and receiver count are left to the API.
- `PdfDocument.ToString()` prints `Content` as `[N bytes]`. The conformance gates exercise both
  methods with every `PdfRequest` field; the coverage gate expects eleven operations.

### Changed

- **`Timeout` must be between 1 and 4294967294 milliseconds**, the range
  `CancellationTokenSource.CancelAfter` honours; anything else throws
  `HuurayConfigurationException` at construction. A sub-millisecond timeout fired at once,
  a longer one threw `ArgumentOutOfRangeException` at the first request, and zero, a
  negative value or `Timeout.InfiniteTimeSpan` was silently replaced with 30 seconds.
- **A `BaseUrl` with user-info (`user@` or `user:password@`)** throws
  `HuurayConfigurationException` at construction. The default `HttpClient` did not send
  it, but it stayed in every request URI.
- **`FileName` and `CustomerReference` are treated as personal data.** `Redaction` masks
  both, and `CreateOrderRequest` and `SendRewardRequest` mask `CustomerReference` in
  `ToString()`.
- **`Content` is a bearer field.** `Redaction` replaces any `Content` value with the secret
  marker, as it does `Code`, `CVV` and `RedeemLink`: a gift card PDF carries the code.

### Fixed

- **Header values are checked before anything is sent.** An `ApiToken` or `UserAgent`
  containing a control character (line break, tab, NUL, DEL) or a non-ASCII character
  throws `HuurayConfigurationException` at construction, and a whitespace-only `ApiToken`
  counts as missing. A custom nonce that is empty or not visible ASCII throws
  `ArgumentException` before the request. `HttpClient` wrote a line break straight onto
  the wire, injecting a header, and a non-ASCII character failed the send after the fact —
  on `/v4/Order` as `HuurayIndeterminateOrderException`, although nothing was sent.
- **`RequestSigner.BuildAuthHeaders` checks the token as `HuurayClient` does.** An
  `apiToken` that is empty, only whitespace, or holds a control or non-ASCII character
  throws `ArgumentException`. A line break was returned inside the `X-API-TOKEN` value.
- **`RequestAsync` rejects a path that does not start with `/` or holds anything but
  visible ASCII**, with `ArgumentException`, before signing. The path is appended to the
  base URL as text, so `@host/…`, `.host/…`, `:port/…` or `v4/…` sent the signed request
  to another host or port.
- **A `BaseUrl` with a space, control character or non-ASCII character** throws
  `HuurayConfigurationException` at construction. It was percent-encoded into the path,
  turned into an IDN host, or failed only at the first request.
- **A `BaseUrl` with a query (`?`) or a fragment (`#`)** throws
  `HuurayConfigurationException` at construction. The path is appended as text, so
  `https://host?x` requested `/?x/v4/Balance` and `https://host#x` requested `/`. A
  trailing slash is still accepted.
- **Retry delays must be at most 4294967294 milliseconds**: a larger
  `RetryOptions.MaxDelay` throws `HuurayConfigurationException` at construction. With
  both set to `TimeSpan.MaxValue`, the wait before the first retry threw
  `ArgumentOutOfRangeException` after the first attempt had been sent. A zero `BaseDelay`
  with `MaxRetries` above 1024 threw `ArgumentException` before retry 1025; that wait is
  now zero.
- **A call with an already-cancelled `CancellationToken` throws
  `OperationCanceledException`** before anything is signed or handed to the `HttpClient`.
  `CreateAsync`, `CreateSyncAsync` and `SendRewardAsync` reported it as
  `HuurayIndeterminateOrderException`, although nothing was sent; every other call already
  threw `OperationCanceledException`, but only after handing the request to the
  `HttpClient`.
- **`HuurayClientOptions.ToString()` prints `ApiToken = [redacted]` and
  `ApiSecret = [redacted]`.** The compiler-generated record `ToString` printed both in the
  clear. Every other member prints as before.
- None of the header, path or base URL messages quotes the rejected value.

### Documentation

- The README no longer invites pull requests, which this repository does not accept.
- The recipient-count check is documented as applying only when `TemplateId` is set,
  which is what the code does.
- `Templates.ListAsync()` docs and the Quickstart cover both outcomes observed live: a 404
  for an account with no templates, and an empty `Templates` list for one with only PDF
  templates.
- The spec-drift workflow is described as it behaves: with pull requests disabled, a
  detected change pushes the `spec-drift` branch and fails the run.

### Confirmed against the live API

Every assumption the specification left open has been verified with real calls, made on
2026-08-15 through the reference implementation of this SDK unless a bullet gives another
date:

- **`X-API-HASH` encoding is lowercase hex** — authenticated against `GET /v4/Balance`;
  the other three candidate encodings return 401. The default is pinned by a test;
  `HashEncoding` remains available as an override.
- **Base URL `https://api.huuray.com`** works for every endpoint exercised.
- **`POST /v4/Template` accepts a bodyless request**, as the spec implies.
- **The full order loop works end to end**: Balance → sync Order (quantity 1, no
  delivery) → Search by `RefID` (matched) → Cancel (full) → Balance.
- **`POST /v4/Template` answered HTTP 404** ("There were no active templates"), not an
  empty 200, for an account with no templates. This is why the reconciliation examples
  treat `HuurayNotFoundException` from `/v4/Search` as "the order did not land".
- **`POST /v4/Template` answered HTTP 200 with an empty `Templates` list** for an account
  with PDF templates but no email or SMS templates — observed live 2026-09-16. So
  `Templates.ListAsync()` can throw `HuurayNotFoundException` or return an empty list.

## [0.1.0] — unreleased

First release. Complete coverage of the Huuray API v4.

### Added

- `HuurayClient` with request signing, nonce generation, timeouts, and typed exceptions.
- All nine v4 operations: balances, catalogue, templates, stock, exchange rates, orders
  (create, create sync, search, resend, cancel).
- PDF templates, added to the v4 specification: `Templates.ListAsync()` returns
  `PdfTemplates` (`PdfTemplate`: `Uid`, `Name`, `Type`, `Language`, `Country`, `BrandName`)
  alongside `Templates`. `CreateOrderRequest` and `SendRewardRequest` accept an optional
  `PdfTemplateUid`, sent as `DeliveryPDFTemplateUid` and omitted when unset. Setting it
  without `TemplateId` throws before any request is made; the API requires that template to
  be an email template.
- `SendRewardAsync` — one gift card to one recipient in a single call.
- `RequestAsync` — an escape hatch that signs any call and returns a `JsonNode`.
- Read-only CLI tool `Huuray.Cli`: `balance`, `catalogue`, `templates`, `stock`, `rates`,
  `search`.
- The CLI `templates` command lists PDF templates as well as delivery templates, in table
  and `--json` output (`--json` prints one object: `Templates` and `PdfTemplates`).
- `Redaction` and a redacting `Voucher.ToString()` for keeping voucher codes out of logs.
- `MinorUnits` guards for amounts that arrive as `decimal` or `double`.
- Multi-targets `net8.0` and `net9.0`. Source-generated `System.Text.Json` contexts, so the
  package is trimming and Native AOT friendly and carries no third-party dependencies.

### Safety behaviour worth calling out

- **Orders, resends and cancels are never retried automatically.** The API has no
  idempotency key, so a retry can order twice or re-deliver a live gift card. A failed
  order throws `HuurayIndeterminateOrderException`, which points at
  `Orders.SearchAsync(new SearchOrdersRequest { RefId = … })` for reconciliation.
- **Retries are opt-in per operation, never inferred from the HTTP method** — four of the
  read-only v4 endpoints are POSTs, and two of the value-moving ones are too.
- **Amounts are integers in minor units.** The request models type them as `int`, so a
  fractional literal does not compile; `MinorUnits` rejects a fractional `decimal` or
  `double` with an explanation rather than rounding, because rounding here is a 100× error.
- **A connection drop or timeout while the response body streams** maps into the exception
  taxonomy like any other transport fault — on `/v4/Order` it wraps in
  `HuurayIndeterminateOrderException` rather than escaping as a raw `IOException`.
- **A 2xx response with an empty or unparseable body** throws `HuurayConnectionException`
  instead of masquerading as an empty result — a garbled `/v4/Search` response must never
  read as "the order did not land". The body is never quoted in the message: it could hold
  voucher codes.
- **`206 Partial Content`** on cancel and resend is surfaced as `Partial = true` rather than
  being treated as plain success.
- **Voucher codes are never logged** by this library at any level. Exception bodies are
  redacted, and `Voucher.ToString()` masks the bearer fields.
- **The CLI cannot move value.**

### Enforced by CI

- Three conformance gates read the vendored `openapi/huuray-v4.json`: **no-invention**
  (every request maps to a documented path, verb and field), **coverage** (every documented
  operation has a method), and **request-conformance** (every request body validates
  against its schema). The validator fails closed on schema shapes it does not understand.
- A mechanical inventory pins the public method list, so a new method cannot bypass the
  gates by not being exercised.
- A weekly spec-drift job re-downloads the live specification and fails on any change:
  pull requests are disabled on this repository.
- No test makes a live API call.

[Unreleased]: https://github.com/Huuray-A-S/huuray-dotnet/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Huuray-A-S/huuray-dotnet/releases/tag/v0.1.0
