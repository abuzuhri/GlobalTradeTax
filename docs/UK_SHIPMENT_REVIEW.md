# UK shipment review — GB and Northern Ireland

Implemented 2026-11-05 for TASK-005–009. This completes a broad reference and arithmetic review path. It does **not** complete independently approved UK duty/VAT determination. `verifiedForCalculation` and `approvedForPayment` are always false; `payableDutyGbp`, `payableVatGbp` and `payableLandedTotalGbp` are always null. The general quote endpoint remains provider-dependent.

## Territory and evidence contract

- Existing UK reference GET routes accept optional `territory=GB|XI`, defaulting to GB for compatibility. Routes that accept `importDate` continue to send that date upstream. Undated raw/normalized routes remain undated.
- Configure GB and XI separately. Public prefixes are `https://www.trade-tariff.service.gov.uk/uk/api/v2/` and `https://www.trade-tariff.service.gov.uk/xi/api/v2/`. Managed prefixes are `https://api.trade-tariff.service.gov.uk/uk/api/` and `https://api.trade-tariff.service.gov.uk/xi/api/`. The XI environment setting is `TRADE_UK_TARIFF_XI_API_BASE_URL`; see [managed authentication](UK_MANAGED_API.md) and [.env.example](../.env.example).
- A missing or invalid XI configuration never uses GB. Territory, requested date, full URL and credential fingerprint partition lookups/cache. Managed prefixes share the existing fixed OAuth endpoint; tokens never go to public hosts.
- Successful reference responses expose territory, exact response-byte SHA-256 and capture time. A digest identifies retrieved bytes, not the deployed API revision or legal correctness. The response cap remains 1 MiB.
- Assessment rules preserve typed duty components, import linkage, VAT/excise flags, conditions with action/document codes, alternative condition sets, quota/additional/preference references, legal acts and footnotes. Missing resources or malformed links make completeness false. Certificate alternatives remain alternatives; matching a caller's document code does not authenticate a certificate or establish entitlement.
- Assessment evidence is now **`uk-reference-evidence/v3`**. The canonical digest additionally covers territory, source digests, graph completeness and structured rules. Preserve the version with every stored digest; v1/v2/v3 digests are not interchangeable. Capture time remains outside the digest. [Earlier v2 evidence](UK_APPLICABILITY_EVIDENCE.md) describes its historical slice.

## Authenticated shipment endpoint

`POST /v1/references/uk/shipments/review` uses existing merchant authentication (`X-API-Key`). Authentication happens before reading the body. JSON is bounded to 256 KiB, with 1–20 lines and at most 100 costs. Duplicate JSON fields, quoted numbers, exponent numbers and decimal values that cannot be represented exactly are rejected. HTTP 400 means invalid input; 413 means oversized input. A valid review with missing upstream access returns HTTP 200 with explicit gaps and null amounts.

Example JSON (the IDs are explicit measures from the recorded GB response, not recommendations to use those measures):

```json
{
  "shipmentId": "review-example",
  "destinationTerritory": "GB",
  "importDate": "2026-11-05",
  "shipFrom": "US",
  "importProcedure": "free_circulation",
  "currency": "GBP",
  "transactionKind": "commercial",
  "intrinsicValueGbp": 1000,
  "usePublishedFx": false,
  "lines": [{
    "lineId": "a",
    "commodityCode": "0201300090",
    "goodsOrigin": "US",
    "customsValue": 1000,
    "netMassKg": 100,
    "itemCount": 1,
    "dutyMeasureId": "20000070",
    "vatMeasureId": "-1012549171",
    "declaredDocuments": []
  }],
  "costs": [{
    "costId": "onward-transport",
    "amount": 30,
    "category": "transport",
    "includedInCustomsValue": false,
    "lineIds": ["a"]
  }]
}
```

`customsValue` is a caller-declared customs value **for the whole line**, in `currency`. It is not merchandise unit price or an independently calculated customs valuation. Net mass is kilograms; item count must be a whole number. Actual `goodsOrigin` stays separate from `shipFrom`. Commodity codes must contain ten digits. All amounts and quantities have explicit bounds.

`costs` must be an explicit array, including `[]` when the caller declares no additional costs. Each cost uses the same currency as customs values. `includedInCustomsValue=true` prevents adding it twice. False adds the cost to the VAT-base illustration; null suppresses complete VAT totals. Optional `lineIds` restrict allocation; otherwise the cost is shared across all lines by declared customs value. Supported review categories are incidental, transport, insurance, packing, commission, excise and levy. These are caller declarations, not verified classification or valuation. Special adjustments such as royalties/reduced valuation require separate review and suppress the illustration when supplied as an unsupported cost category.

Responses contain:

- `lines`: source assessments, explicit reference duty scenarios, selected illustrative duty, proposed VAT rate, illustrative VAT base/VAT, allocated costs, declared document matches and unresolved checks.
- `fxRates`: purpose-specific dated evidence, never caller-supplied rates on this HTTP endpoint.
- `illustrativeTotals`: sums of completed line illustrations, or null if any line is incomplete. `valuePlusTaxesGbp` is declared customs value plus additional costs/duty/VAT; it is not a payable invoice or a verified landed total.
- `evidenceSha256`: deterministic `uk-shipment-review/v2` digest binding request, context, source assessments, rates and results, including declared VAT treatment/accounting and review decisions. Reordering lines/costs does not change it. Store the schema version with the digest; earlier v1 hashes are not comparable. This is an integrity aid, not a signature.
- Unconditional false verification/payment flags and null payable amounts.

With PostgreSQL, the existing audit store records tenant, review status, digest, territory, date and false approval. It does not store the line inputs or retrieved reference bodies. Capture the original request, full review and raw referenced responses securely if independent replay or source-digest verification is required. No database migration is needed for this endpoint.

## Supported arithmetic and explicit limits

Structured component arithmetic supports a single expression `01` base and additive `04` components, percentages, GBP/EUR amounts per net kg (`KGM`), 100 kg (`DTN`), tonne (`TNE`) or whole item (`NAR`). It uses source components, not a parsed HTML rate. Unsupported qualifiers, operations, missing quantities, ambiguous FX, duplicate bases, negative amounts, overflow or incomplete graphs yield unknown arithmetic. Agricultural/Meursing codes 12/14/21/25/27/29 are **not** guessed to mean MIN/MAX; other expressions remain unsupported with null amounts.

Two distinct currency contracts are implemented:

| Purpose | Official feed | Conversion |
|---|---|---|
| Invoice/customs value | GB `exchange_rates/YYYY-MM?as_of=DATE&filter[type]=monthly` | Published foreign units per GBP are inverted to GBP per unit. Matching currency and inclusive validity dates required. |
| EUR tariff components | XI `monetary_exchange_rates?as_of=DATE` | Matching monthly record supplies GBP per EUR directly. No latest-month fallback. |

`usePublishedFx=true` enables applicable feeds. Invoice FX is never substituted for tariff FX. A missing conversion is not zero. An HMRC monthly feed alone does not establish whether a subsequent correction or a permitted contractual conversion basis applies to a real declaration.

Per-line GB VAT illustrations require an explicitly selected source VAT component and complete selected duty arithmetic. VAT conditions, quotas, additional codes, footnotes and preference qualifiers suppress VAT amounts until treatment is resolved; a proposed reference rate can still be displayed. Additional costs are allocated in pennies by exact largest remainder, with ordinal line-ID tie breaking; already-included costs are excluded. Line VAT is rounded to two places away from zero, then summed. This rounding/allocation is a documented illustrative policy, not a statutory rounding approval.

Shipment duty/VAT totals are suppressed for XI, GB↔XI movements, unknown/special import procedures, gifts, noncommercial/unknown transaction kinds, and intrinsic value at or below £135 or absent. That threshold is a conservative illustration gate; it does not assert tax exemption, a duty threshold for NI or eligibility for a particular collection regime. XI still exposes its separate dated rules and reference scenarios. A caller's `northernIrelandRisk` declaration never selects a payable Union/UK tariff or grants UKIMS entitlement.

No lowest-rate selection is performed. A selected duty with conditions, quota/additional-code links or a nonstandard preference code remains unresolved. Other applicable measures, controls, excise, trade remedies and special VAT treatments still require review even when a simple component scenario is mathematically available. Explicit zero rates can be illustrated only when present in selected source data; missing data never becomes a zero rate.

**2026-11-06 duty comparison update:** [five observed live duty rows and four pinned official-test examples](UK_DUTY_REFERENCE_CASES.md) now match, with precision regressions corrected. These observations remain awaiting independent approval and do not change the payment or NI gates below.

**2026-11-06 VAT exception update:** lines accept optional `vatTreatment` declarations and return `vatTreatmentReview`. Relief claims suppress ordinary VAT; conditional art valuation remains separate from totals, and postponed accounting preserves liability illustrations. Known NI movements stay unresolved. [Claim tokens, exact JSON bounds, sources and v2 compatibility](UK_VAT_EXCEPTIONS.md) describe the current contract and 486-case local verification. The dated implementation evidence below remains historical.

## Current completion gates

| Task | Implemented and verifiable | Remaining before claiming full task completion |
|---|---|---|
| 005 — API contracts | Separate GB/XI public/managed paths; exact attributed fixtures; dated geography and both FX contracts; offline/live probes; weekly public contract workflow | Real managed GB/XI and FX probes need provisioned credentials. Confirm current service/access terms and operational ownership. Scheduled execution starts only after merge to the default branch; manual passing probes do not prove future availability. |
| 006 — applicability | Date/origin/territory bound candidates, typed membership/exclusions, conditional document alternatives, legal/footnote/quota/preference references | Independently reviewed legal applicability; authentic origin/certificates; preference entitlement; quota eligibility/balance and relevant trade remedies. Source graph completeness is not legal clearance. |
| 007 — duty | Recorded percentage/GBP compound/XI EUR compound arithmetic; explicit units; unknown unsupported expressions; no silent fallback or cheapest-rate selection | Approved golden cases against the official calculator for the supported release scope; legal rule selection, unsupported expressions and currency/rounding approval. Current examples are regression arithmetic, not independently approved golden cases. |
| 008 — VAT/landed cost | Line/shipment reconciliation, mixed proposed rates, cost scope/inclusion, FX separation, low-value/gift/procedure/NI uncertainty; stricter legacy VAT gate | Authoritative VAT treatment, reduced valuations/exemptions/reliefs, valuation method, collection regime, NI movement/risk rules and statutory rounding. Supplier/provider evidence must match actual goods origin; shipping country is insufficient. |
| 009 — controls | GB/XI manual review requirements for EORI, classification, procedure, licences, document alternatives, preference/quota and Incoterms; controlled flags hold regardless of shipping country | Actual sanctions/licence/certificate validation and specialist clearance. The task also includes a broader US export review outside this UK pass. Heuristics never assert legal clearance. |

No credentials are present in this environment or configured repository Actions secrets. They must be provisioned through the secret provider; never paste them into a chat or tracked file. The current PR does not deploy or enable collection. Task statuses remain in progress wherever these acceptance criteria remain unmet.

## Sources and verification

Seven unmodified public responses and exact URLs/timestamps/hashes/attribution live in [fixture provenance](../tests/GlobalTradeTax.Api.Tests/Fixtures/UkCompletion/provenance.json). The government [API catalogue](https://www.api.gov.uk/hmrc/gov-uk-trade-tariff-api/) attributes data under [OGL v3](https://www.nationalarchives.gov.uk/doc/open-government-licence/version/3/); this does not replace a current access/terms review. The [API guide](https://docs.trade-tariff.service.gov.uk/the-trade-tariff-api.html) documents separate GB/XI paths. Public backend serializers/models/controllers were reviewed at [revision e5cab2b](https://github.com/trade-tariff/trade-tariff-backend/tree/e5cab2b0fea383c2b5cc80dc6d25469fc3ede323), not asserted to be the deployed revision.

VAT review uses current HMRC guidance: [import VAT value](https://www.gov.uk/guidance/how-to-value-goods-for-import-vat), [customs-value additions](https://www.gov.uk/guidance/working-out-the-vat-value-using-the-customs-value-of-the-imported-goods), and [currency conversion](https://www.gov.uk/guidance/converting-foreign-currency-amounts-to-include-in-the-customs-value). The VAT value guidance was updated on 2 November 2026 and includes special reduced valuations; the withdrawn VAT Notice 702 is not the implementation authority. NI review also requires the relevant [GB-to-NI risk guidance](https://www.gov.uk/guidance/check-if-you-can-bring-your-goods-into-northern-ireland-from-great-britain-without-paying-duty) and [other-country-to-NI duty guidance](https://www.gov.uk/guidance/how-to-make-sure-the-correct-duty-is-applied-to-goods-you-bring-into-northern-ireland-from-countries-outside-of-the-eu-and-uk). These sources motivate conservative gates; reading them does not establish specialist approval.

Reproduce offline checks:

```powershell
dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj -c Release -warnaserror
python -m unittest discover -s scripts/tests -v
python scripts/check_project_integrity.py
python scripts/check_ci_workflow.py
python scripts/check_secrets.py --include-untracked
```

Public live probes require `TRADE_RUN_UK_LIVE_CONTRACT=1` and filter `Category=UkLiveContract`; managed probes use the separate opt-in and secrets described in the managed runbook. [The weekly public workflow](../.github/workflows/uk-public-contract.yml) produces a failed check and TRX on contract drift or outage. The regular CI container job exercises real merchant authentication, malformed/oversized review bodies, GB/XI missing-source results, audit persistence, recreation and backup/restore using private labelled resources. Existing Saleor volumes are not selected.

Local verification passed: warnings-as-errors Release build/test (380 offline cases; 16 optional live skips), ten separately enabled public live probes, 32 Python cases and 112 PostgreSQL/container assertions with cleanup=true. Release publish, actionlint 1.7.12 (optional ShellCheck/Pyflakes disabled), task/CI/secret-pattern guards and whitespace checks passed. The database run verified private resource cleanup; the existing stopped Saleor containers and data were retained. Hosted results are recorded in the review PR after execution.
