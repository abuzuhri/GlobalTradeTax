# Declared control holds across quoting

Implemented 2026-11-07 for TASK-009, with supporting TASK-002 input safeguards. A declared export-control hold now prevents automated quote completion. This is an application policy, not a licensing determination or a customs release workflow.

## Behavior

The existing `QuoteRequest` flags `requiresExportLicense=true` and `itarControlled=true` produce stable hold reasons:

| Request flag | `holdReasons` entry |
|---|---|
| `requiresExportLicense` | `declared_export_licence` |
| `itarControlled` | `declared_itar_control` |

Either flag triggers a specialist hold regardless of dispatch, destination, invoice currency, value or DDP/DAP. The same policy applies to GB and Northern Ireland. These are caller declarations: a false or absent flag is not evidence that goods are uncontrolled.

`POST /v1/compliance/check` returns the existing route, decision, checks and warnings, plus `holdReasons`, `specialistHoldRequired` and **`shipmentReleaseApproved=false`**. UK EORI, classification, origin, procedure, certificates, sanctions, preference/quota and NI review requirements remain. Accepted lowercase country inputs also receive the existing destination checks.

`POST /v1/quotes` saves a held quote with `status=manual_review_required`, null duty/tax/landed total, a specialist action and the complete `compliance` result. Tenant isolation and immutable idempotency still apply: repeating the same request/key returns the same quote; changing flags under that key returns 409. There is no approval token or override in this API.

`POST /v1/landed-cost/estimate` evaluates the hold **before calling the rate provider**. A held request returns `status=manual_review_required`, null duty, tax, clearance fee, total and provider evidence, plus the `compliance` result. Merchandise, shipping and insurance remain declared amounts. The database audit stores the authenticated tenant, status, hold reasons and false shipment-release approval. It does not store a claimed licence document or authenticate one.

Without a declared hold, the previous financial provider-evidence gate remains. An estimate may have financial `status=verified` while its separate UK compliance decision still requires manual review. Financial verification never changes `shipmentReleaseApproved`, which is always false. The UK reference shipment/VAT endpoints retain their existing unverified, never-payable contract; no tax rate, provider subscription or payment processing is enabled here.

For example, add these flags to the [existing request shape](../samples/us-to-sweden.json), using the actual shipment facts:

```json
{
  "requiresExportLicense": true,
  "itarControlled": false
}
```

This fragment is not a complete request. A declared hold remains a hold even if a provider could return a valid financial quote. No claim in this request authenticates a licence, approves an exception, screens parties or proves an item's legal control classification.

## HTTP and persistence compatibility

All three routes now authenticate before reading JSON, then apply a **65,536-byte** bound, including chunked bodies. `/v1/quotes` also requires its existing 1–128 character `Idempotency-Key` before parsing. Oversized authenticated bodies with valid headers return 413. Invalid JSON, case-insensitive duplicate fields, exponent numbers, quoted numeric/boolean values and decimals outside the exact input contract return 400. In particular, a duplicate `ItarControlled=false` cannot replace an earlier `itarControlled=true`.

The shared exact-number contract permits at most 28 significant digits and 28 fractional places after trimming trailing fractional zeros. This intentionally excludes some larger integers representable by the runtime decimal type; clients must use the documented plain-number bound rather than exponent or quoted notation.

Input validation rejects missing/null lines, more than 100 lines, invalid nested entries, nonpositive quantities, negative amounts and arithmetic overflow. Country/origin fields require two ASCII letters, currency three; either case is accepted. SKU and HS reference strings are nonblank, without control characters and bounded to 128 and 32 characters respectively. These syntax checks do not authenticate country membership or HS classification. DDP/DAP remains the supported Incoterm set.

The change intentionally tightens permissive automatic JSON binding. Valid existing sample requests remain accepted. Response `compliance` is additive; flagged quote/estimate statuses now become `manual_review_required`. Existing stored quote JSON and request fingerprints are preserved without a migration. Historical snapshots may have `compliance=null`; absence is not current compliance clearance. Quote retrieval/replay does not rerun a legal review or convert a historical snapshot into release approval.

## Sources and scope

Official sources reviewed 2026-11-07:

- HMRC's [UK import process](https://www.gov.uk/import-goods-into-uk) distinguishes GB imports, NI movements and product-specific licence/certificate requirements. The [import controls guidance](https://www.gov.uk/guidance/import-controls), updated 18 September 2026, describes importer responsibility and controlled categories. The implementation retains review requirements rather than inferring permissions from a country or HS code.
- BIS [EAR Part 758, including section 758.3](https://www.bis.gov/regulations/ear/758) addresses exporter/agent responsibilities. A financial provider response is not treated as licensing evidence. This reference does not determine whether a particular item is subject to the EAR or ITAR.

The licence/ITAR flags already existed. This change enforces their declared hold and does not implement licensing rules, sanctions/denied-party screening, certificate validation, Schedule B/ECCN/USML classification, AES filing or a specialist approval system. Existing preliminary EEI screening remains preliminary. TASK-009 remains in progress for those independently reviewed requirements; TASK-002 still needs broader key/rate-limit/threat-model work.

## Verification

Eight regression cases failed against the preceding implementation: held stored quotes still returned `requires_provider_quote`, and held landed-cost requests returned a provider's `verified` total. After the fix, **514 offline xUnit cases passed**, with 16 optional live tariff API cases skipped, using the Release warnings-as-errors build. Tests verify that a declared hold causes zero provider calls, null financial completion, preserved hold reasons, tenant/idempotency behavior and false release approval. They also cover historical JSON, malformed nested inputs, overflow, case compatibility and ordinary financial estimates.

All 33 Python tests and task/integrity/CI/limited-secret/whitespace checks passed. The extended hosted PostgreSQL suite exercises all three routes with duplicate flags, nulls, invalid booleans, precision loss, overflow, authenticated and anonymous oversized bodies, durable held quotes, audit evidence, recreation and backup/restore. No managed/live licensing service was queried.

Implementation commit `8dc1bf6` passed all six regular jobs in [hosted run 42355867265](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42355867265): 514 xUnit passes/16 optional skips, 33 Python passes, 11 anonymous HTTP smoke assertions and **297 PostgreSQL/container assertions with cleanup=true**, plus zero-warning Release build/publish, dependency/UI and full-history secret checks. Optional public/managed tariff live jobs were not enabled. [Ready PR #9](https://github.com/abuzuhri/GlobalTradeTax/pull/9) records final-head verification and review.
