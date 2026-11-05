# UK API contract verification — TASK-005

Verified on 2026-11-04. This slice repairs the read-only UK reference adapter; it does not establish customs applicability or enable payable duty/VAT.

Subsequent work on the same date added opt-in managed authentication and inspected the previously unverified hosted run. See [the managed API runbook and latest evidence](UK_MANAGED_API.md). The results below record the preceding public-contract slice.

## Discovered mismatch and fix

The official commodity response for `0101210000` has `data.type = "commodity"` and `data.id = "93796"`. Its commodity code is `data.attributes.goods_nomenclature_item_id`. The previous guard compared `data.id` to the commodity code, while its synthetic tests used a plural resource type. Consequently, valid official responses were rejected with `invalid_upstream_contract`.

The guard now requires the singular resource type, a nonempty internal resource ID and an exact commodity-code attribute match. Geographical areas require `type = "geographical_area"` and an exact area resource ID match. Malformed included resources and documents mixing data with errors are rejected without exposing data; invalid areas are not cached. Both requests explicitly send the documented v2 media type, `Accept: application/vnd.hmrc.2.0+json`.

The HTTPS host/path allowlist, redirect/cookie prohibition, size limits, authentication and read-only behavior remain in force. Existing public HTTP response shapes are preserved; `data.id` is passed through as an upstream internal ID, not rewritten into a commodity code.

## Official sources and access migration

- [Current official documentation](https://docs.trade-tariff.service.gov.uk/reference.html) describes the managed API at `https://api.trade-tariff.service.gov.uk/uk/api`, the v2 Accept header and OAuth 2.0 credentials from the [developer portal](https://hub.trade-tariff.service.gov.uk/).
- [Official documentation source at the reviewed revision](https://github.com/trade-tariff/trade-tariff-api-docs/blob/f57cd82addcb747aeb23f16190e66d825ec9ce7d/source/reference.html.md.erb) documents that migration. The docs also announce rate limiting from September 2026.
- [Official commodity serializer](https://github.com/trade-tariff/trade-tariff-backend/blob/64c30b8d35a9c2b2508e0669a6af57892f14bdeb/app/serializers/api/v2/commodities/commodity_serializer.rb) sets the internal goods-nomenclature SID as the ID and uses the singular resource type.
- [Official geographical-area tree serializer](https://github.com/trade-tariff/trade-tariff-backend/blob/64c30b8d35a9c2b2508e0669a6af57892f14bdeb/app/serializers/api/v2/geographical_area_tree_serializer.rb) exposes `children_geographical_areas`, not dated membership resources.
- [Official API catalogue](https://www.api.gov.uk/hmrc/gov-uk-trade-tariff-api/) lists the data usage licence as [Open Government Licence v3.0](https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/). This is separate from the backend code's MIT licence.

The currently configured legacy `https://www.trade-tariff.service.gov.uk/uk/api/v2/` endpoint returned the recorded responses directly, without redirects or credentials, during this verification. That observation does not establish ongoing access guarantees or verify the managed API. No additional host or OAuth token support was added. A migration needs operator-provisioned credentials and separate transport, token-lifecycle and access-policy work. The service terms page returned HTTP 403 during review; a full current service-terms review remains open.

These requests use the UK/GB endpoint. Northern Ireland (`/xi/`) is not allowed by this adapter and was not probed. Passing these tests does not establish GB/XI equivalence.

## Recorded public fixtures

Exact response bytes are stored under `tests/GlobalTradeTax.Api.Tests/Fixtures/UkTariff/`. [Provenance](../tests/GlobalTradeTax.Api.Tests/Fixtures/UkTariff/provenance.json) records each source URL, UTC retrieval time, HTTP status, content type, byte count and SHA-256. No credentials or merchant data were sent, and the fixtures contain public tariff reference documents only. No transformations were applied.

Contains public sector information licensed under the Open Government Licence v3.0.

| Resource | HTTP | Observed identity / body | Size |
|---|---|---|---|
| `commodities/0101210000` | 200 | `commodity:93796`; code attribute `0101210000`; 603 included resources, including 67 measures | 269,763 bytes |
| `geographical_areas/1011` | 200 | `geographical_area:1011`; 261 included geographical areas | 60,943 bytes |
| `commodities/0000000000` | 404 | JSON errors array with `detail = "not found"` | 35 bytes |
| `geographical_areas/ZZZZZZZZZZZZ` | 404 | Same not-found shape | 35 bytes |

The two successful documents are individual resources with embedded relationships and no top-level pagination links. The adapter does not enumerate search/list endpoints or follow pagination. This is bounded point-lookup coverage, not validation of every UK endpoint. The fixtures are historical test evidence and must not be used as current tariff data. A refresh requires an explicit source/licence review and matching provenance updates; tests never overwrite them.

The geographical-area tree contains country references without effective-dated membership records. The membership projection therefore still returns `unknown`. Measure relationships and rate expressions require further normalization and applicability work under TASK-006/007; fixture success is not evidence of verified duty arithmetic.

## Repeatable checks

Normal builds run the recorded fixtures offline and explicitly skip the four live probes:

```powershell
dotnet build tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-restore -c Release -warnaserror
dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-build -c Release
```

From a fresh checkout, run `dotnet restore tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj` first. To opt into the four fixed, read-only official requests:

```powershell
$env:TRADE_RUN_UK_LIVE_CONTRACT = '1'
try {
    dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-build -c Release --filter 'Category=UkLiveContract'
} finally {
    Remove-Item Env:\TRADE_RUN_UK_LIVE_CONTRACT -ErrorAction SilentlyContinue
}
```

Each live HTTP request has a 20-second timeout and uses the production adapter with redirects/cookies disabled. The checks cover commodity identity and normalization, geographical-area identity, and both missing-resource cases. They do not send credentials, follow upstream links or exercise search, checkout, payments or remote writes.

In GitHub Actions, manually dispatch `.NET build, tests and package` with `uk_live_contract` enabled. The separate `uk-live-contract` job fails on structural/status drift or availability problems and uploads its TRX report. Ordinary push/PR runs keep the live probes skipped. There is no scheduled monitor or automatic notification, and no hosted run was performed in this session.

## Actual local results

- Release build with warnings as errors: passed, zero warnings/errors.
- Full xUnit suite: **167 passed, 4 explicitly skipped live probes**. This adds 24 offline regressions to the previous 143-case baseline.
- Opt-in live xUnit check: **4 passed, 0 skipped** against the configured official legacy endpoint.
- Isolated running Development API: **10 general HTTP smoke assertions and 6 authenticated UK route checks passed**. Verified raw/normalized commodity, area, unknown membership and both HTTP 404 cases. The temporary process used a randomly generated development key in its environment and was stopped afterward; no existing service or store was changed.
- Python suite: **25 passed**. Task integrity, static CI guard and limited tracked/untracked secret-pattern scan passed.
- Workflow syntax: actionlint **1.7.12 passed** with optional ShellCheck/Pyflakes disabled; its official Windows release archive was verified against the published SHA-256 checksum. `git diff --check` passed.

Offline regressions also verify provenance hashes, reject identity/type/schema mutations, check cache behavior and confirm that an assessment with real commodity data and failed area lookups retains null import duty/VAT and `ApprovedForPayment = false`.

TASK-005 remains **in-progress**. Managed-credentials migration, current service-terms/access review, broader contract coverage, hosted execution and a decision on operational drift monitoring remain open. This is not production or legal approval.
