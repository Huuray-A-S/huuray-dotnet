# Changelog

All notable changes to this project are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Confirmed against the live API (2026-08-15)

Every assumption the specification left open has been verified with real calls, made
through the reference implementation of this SDK:

- **`X-API-HASH` encoding is lowercase hex** — authenticated against `GET /v4/Balance`;
  the other three candidate encodings return 401. The default is pinned by a test;
  `HashEncoding` remains available as an override.
- **Base URL `https://api.huuray.com`** works for every endpoint exercised.
- **`POST /v4/Template` accepts a bodyless request**, as the spec implies.
- **The full order loop works end to end**: Balance → sync Order (quantity 1, no
  delivery) → Search by `RefID` (matched) → Cancel (full) → Balance.
- **An empty result set is signalled as HTTP 404**, not as an empty 200 — observed live on
  `/v4/Template` ("There were no active templates"). This is why the reconciliation
  examples treat `HuurayNotFoundException` from `/v4/Search` as "the order did not land".

## [0.1.0] — unreleased

First release. Complete coverage of the Huuray API v4.

### Added

- `HuurayClient` with request signing, nonce generation, timeouts, and typed exceptions.
- All nine v4 operations: balances, catalogue, templates, stock, exchange rates, orders
  (create, create sync, search, resend, cancel).
- `SendRewardAsync` — one gift card to one recipient in a single call.
- `RequestAsync` — an escape hatch that signs any call and returns a `JsonNode`.
- Read-only CLI tool `Huuray.Cli`: `balance`, `catalogue`, `templates`, `stock`, `rates`,
  `search`.
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
- A weekly spec-drift job re-downloads the live specification and opens a pull request on
  any change.
- No test makes a live API call.

[Unreleased]: https://github.com/Huuray-A-S/huuray-dotnet/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Huuray-A-S/huuray-dotnet/releases/tag/v0.1.0
