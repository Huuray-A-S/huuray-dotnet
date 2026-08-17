# Contributing

Thanks for taking the time.

**This repository does not accept external pull requests.** It is a published client library that must stay in exact step with the Huuray API specification, and it moves real money, so changes come from Huuray. Please do not spend your time on a patch we cannot merge.

**Questions, bug reports and suggestions are very welcome** — open a [discussion](https://github.com/Huuray-A-S/huuray-dotnet/discussions) to ask something, or an [issue](https://github.com/Huuray-A-S/huuray-dotnet/issues) to report a bug. We read every one.

The rest of this file documents how the library is built and the rules it is held to, so you can read the code with confidence and describe a problem precisely.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting set up

```bash
git clone https://github.com/Huuray-A-S/huuray-dotnet.git
cd huuray-dotnet
dotnet restore
dotnet test
```

The **.NET 9 SDK** is required, because the library multi-targets `net8.0` and `net9.0` and only the newer SDK can build both. No test touches the network.

You also need the **.NET 8 *runtime*** installed. The .NET 9 SDK does not ship it, and the test project runs against both target frameworks — without it the `net8.0` leg fails to start with *"You must install or update .NET to run this application"*. Either install it ([dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0)) or restrict the run to one framework:

```bash
dotnet test -f net9.0
```

| Command | What it does |
|---|---|
| `dotnet build` | build every project, warnings as errors |
| `dotnet test` | run the whole suite, including the three conformance gates |
| `dotnet pack -c Release` | produce the NuGet packages and symbol packages |
| `scripts/fetch-spec.sh` | re-download the live spec over `openapi/huuray-v4.json` |

## Spec fidelity — read this before adding anything

**This client exposes nothing the API does not document.** It is the rule the whole library is built on, and it is enforced by tests rather than by review.

In practice:

1. **Never send a field the spec does not define.** Not "just in case", not because another endpoint accepts it.
2. **Never call a path or verb the spec does not define.**
3. **Never depend on undocumented behaviour** — an undocumented status code, an undocumented header, an undocumented error shape. If the specification is silent, we confirm with Huuray before implementing. An unanswered question blocks the feature; it does not get a best guess.
4. **Field names mirror the spec, differing only in casing convention.** `OrderUID` becomes `OrderUid`. It does not become `OrderId`, `Uid`, or anything more tasteful. Someone reading the Huuray API reference must be able to map it across without a translation table.
5. **Convenience methods are allowed, but only as a documented composition of real operations.** `SendRewardAsync` is fine: it is exactly one `POST /v4/Order`, and its documentation says so.

Three gates in [`tests/Huuray.Tests/ConformanceTests.cs`](../tests/Huuray.Tests/ConformanceTests.cs) enforce this against the vendored [`openapi/huuray-v4.json`](../openapi/huuray-v4.json):

- **no-invention** — every request the SDK can emit maps to a path and verb in the spec, and sends no undefined property
- **coverage** — every operation in the spec has a method
- **request-conformance** — every request body validates against the spec schema

They work by calling every public method with every optional parameter populated, then checking what came out. **If you add a method, add it to `ExercisedSurface` and to the inventory in `PublicSurfaceInventory`** — the inventory test fails otherwise, which is the point: a new method cannot slip past the gates by simply not being exercised.

The validator in `Spec.Validate` **fails closed**. A schema shape it does not understand — `allOf`, `oneOf`, `anyOf`, or a missing `type` — is reported as a failure rather than passed over. Widen the validator deliberately; never let it go quietly vacuous.

## The vendored specification

`openapi/huuray-v4.json` is the source of truth and is vendored deliberately. A scheduled workflow re-downloads it weekly and opens a pull request if it changed, which is how we find out about API changes. Review every one of those pull requests; do not merge on green alone.

## Tests

- **No live API calls, ever.** Ordering gift cards from a test runner spends real money. Inject a fake through the `HttpClient` constructor parameter; `tests/Huuray.Tests/TestTransport.cs` has one ready.
- **Fixtures contain invented data only.** Never record a real response.
- New behaviour needs a test that fails without your change.

## Money and value — extra care

Some of this library moves real money. Changes in these areas get closer review, and pull requests that weaken a guard will be asked to justify it:

- **Never add automatic retries to `/v4/Order`, `/v4/Resend` or `/v4/Cancel`.** There is no idempotency key. A retried order orders twice; a retried resend re-delivers a live gift card.
- **Never widen the CLI to move value.** It is read-only on purpose.
- **Never log a voucher code**, at any level, in any code path.
- **Keep amounts as whole numbers of minor units.** No floating point, no silent rounding.
- **Keep the response body read inside the same error mapping as the request.** A failure that escapes raw bypasses `HuurayIndeterminateOrderException`, and a consumer's generic retry handler then re-orders.

## Style

- `TreatWarningsAsErrors` is on for every project. Warnings are not suggestions here.
- Nullable reference types are enabled; implicit usings are off. Write the usings you need.
- Public members carry XML documentation. The package ships the documentation file.
- Prefer explicit types over `var` in this codebase — the shapes are the interesting part.

## Why there is no pull request workflow

Two reasons, both structural rather than a matter of taste:

1. **Spec fidelity.** The types are generated from the vendored specification and three CI gates assert the client sends nothing the API does not document. A change that looks like an improvement usually needs an API change first, which is a conversation with Huuray, not a patch here.
2. **It moves money.** The guards described above — never retrying an order, minor units as integers, never logging a voucher code — exist because getting them wrong costs real money. They are not open to drive-by modification.

If you have found a bug or need behaviour the client does not offer, please open an issue or a discussion and describe the case. That is genuinely the fastest route to a fix.


## Reporting a bug

[Open an issue.](https://github.com/Huuray-A-S/huuray-dotnet/issues) Include the package version, your .NET version, what you called, what you expected, and what happened.

**Never paste an API token, an API secret, or a voucher code into an issue.** For a vulnerability, see [SECURITY.md](SECURITY.md) instead.

## What belongs somewhere else

Questions about the API itself, your account, pricing, or a live production problem go to your Huuray representative rather than here — see [SUPPORT.md](SUPPORT.md). We cannot resolve those from a GitHub issue.
