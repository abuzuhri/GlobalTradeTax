# UK VAT exceptions and accounting review

Implemented 2026-11-06 for TASK-008. These APIs record claims and expose review arithmetic. They do not establish relief eligibility, confirm a legal VAT rate, authorize payment or prepare a VAT return. Every treatment decision remains unverified, unapproved and without payable VAT.

## Declared treatment contract

Each line of `POST /v1/references/uk/shipments/review` accepts an optional `vatTreatment` object. The older `POST /v1/references/uk/vat/review` accepts the same object under `classification`, alongside optional `classification.shipFrom` (two uppercase letters). The shipment endpoint already requires dispatch separately from goods origin.

```json
{
  "treatment": "reduced_valuation_art",
  "accountingMethod": "pay_at_import",
  "evidenceReference": "internal-review-file-1"
}
```

This is a fragment, not a complete request. `evidenceReference` is an optional caller reference: 1–256 characters, not whitespace-only and without control characters. It is retained as `declaredEvidenceReference`, not fetched or authenticated. Do not include credentials or unnecessary personal information.

| `treatment` | Review behavior |
|---|---|
| `ordinary` | Existing reference illustrations remain subject to all source, context, cost and arithmetic gates. |
| `reduced_valuation_art` | Suppresses ordinary VAT and shipment totals; may expose the separate conditional scenario below. |
| `returned_goods_relief` | Requires identity, export/reimport, condition and VAT ownership evidence; never assumes zero VAT. |
| `temporary_admission` | Requires authorization, permitted use, reexport and full/partial relief review. |
| `vat_exemption`, `other_relief` | Claimed relief is not a verified zero rate; ordinary amounts stay null. |
| `unknown` | Explicitly unresolved; ordinary amounts stay null. |

Tokens are case-sensitive. `treatment` is required when the object is present. `accountingMethod` may be omitted or be `unknown`, `pay_at_import`, `postponed_accounting` or `seller_collected`. Invalid claims return HTTP 400. Omitting the entire object preserves the previous reference-only illustration scope and adds an explicit undeclared-treatment check; omission never verifies that ordinary treatment applies.

`postponed_accounting` adds registration, eligibility and declaration checks. It preserves an otherwise available **liability illustration** rather than changing the amount to zero; cash payment, recovery and accounting entries remain undetermined. `seller_collected` suppresses ordinary import VAT illustrations pending a separate supply/collection-regime review. See HMRC's [postponed accounting eligibility guidance](https://www.gov.uk/guidance/check-when-you-can-account-for-import-vat-on-your-vat-return) and [collection of accounting guidance](https://www.gov.uk/government/collections/using-postponed-vat-accounting).

Responses add `vatTreatmentReview` to every shipment line and to the legacy VAT review. It includes the declared treatment/accounting/reference, suppression decision, required checks, guidance URLs and `sourcesReviewedOn=2026-11-06`. Source links and a review date are implementation references, not evidence that a claim was approved or that a rule applies on every requested import date.

## Conditional reduced-valuation arithmetic

HMRC describes a 25% valuation followed by the standard 20% rate for certain eligible art, antiques and collectors' items. The guidance lists headings 4911, 5805 and 9701–9706, with further goods-specific restrictions. A heading match alone does not establish eligibility. [Import VAT valuation guidance, updated 2 November 2026](https://www.gov.uk/guidance/how-to-value-goods-for-import-vat).

The implementation exposes `conditionalScenario` only for a declared art claim, a listed heading, GB free circulation, a proposed 20% VAT rate and an available full base. Known GB/XI dispatch and seller collection exclude it. The legacy endpoint retains compatibility when optional dispatch is absent; that absence is not independent evidence of import scope. Existing classification/source/amount consistency gates still apply there. Shipment review still requires complete selected duty arithmetic and the ordinary shipment context gates, including declared costs and invoice FX.

For a synthetic full base of £1,140, the scenario shows £285 at a valuation fraction of 0.25 and £57 VAT at an **assumed** 20%. A proposed 5%, zero, other or missing rate never receives this fraction as an additional reduction. Synthetic tests use code `9701100000` only to exercise the heading check; they do not establish that it is a currently declarable commodity or that any artwork qualifies.

The scenario is labelled `conditional_arithmetic_only`, is never included in ordinary line VAT or shipment totals and has false verification/payment flags. It can describe a counterfactual while source VAT qualifiers still require review; it does not clear conditions, footnotes, additional codes, valuation eligibility or entitlement. With no consistent base or standard-rate proposal it stays null. Shipment scenarios round VAT to pennies away from zero, an illustrative policy awaiting statutory approval. Ordinary legacy readiness arithmetic retains its existing unrounded output and legal-rounding check.

## Northern Ireland and special procedures

XI destinations and known GB↔XI movements suppress ordinary and conditional VAT amounts. GB domestic dispatch is also outside the import illustration scope. A caller's `northernIrelandRisk` value does not determine VAT treatment. Dispatch, customs status, importer VAT registration, import/acquisition scope and movement facts need separate review; no EU-country list or NI threshold algorithm is inferred here. HMRC's [GB/NI movement guidance](https://www.gov.uk/guidance/how-vat-will-apply-to-goods-moving-between-great-britain-and-northern-ireland) has a specific individuals/non-VAT-registered-business scope. Broader business cases require the relevant [NI import and movement accounting guidance](https://www.gov.uk/government/publications/accounting-for-vat-on-goods-moving-between-great-britain-and-northern-ireland-from-1-january-2021/accounting-for-vat-when-importing-or-moving-goods-into-northern-ireland-from-outside-the-eu).

The legacy endpoint now suppresses special/unknown procedures even when all caller verification flags are true. Neither a claim nor those flags authenticate [returned goods relief](https://www.gov.uk/guidance/pay-less-import-duty-and-vat-when-re-importing-goods-to-the-uk-and-eu) or [temporary admission](https://www.gov.uk/guidance/apply-to-import-goods-temporarily-to-the-uk-or-eu). Refunds, guarantees, partial relief, margin schemes and tax filings remain outside this implementation.

## Numerical and HTTP compatibility

Positive invoice/cost FX and VAT calculations that underflow decimal precision now remain unknown with a precision-loss check. Overflow also remains unavailable. A genuine explicit zero rate is distinct from missing data or arithmetic loss. Existing deliberate penny allocation and rounding can still round a nonzero sub-penny illustration to zero; that does not imply tax exemption.

The legacy VAT route now authenticates before reading the body and limits it to **65,536 bytes**, including chunked requests. Duplicate fields (case-insensitive), exponent numbers, quoted numbers and excess decimal precision return HTTP 400; oversized bodies return 413. This intentionally tightens the previous automatic model binding. The shipment route retains its existing exact-number contract and 256 KiB bound.

Shipment response and canonical digest schema are now **`uk-shipment-review/v2`** because claims and decisions form part of the evidence. Retain the schema version alongside every digest; v1 and v2 hashes are not comparable. Stable line/cost ordering remains canonical. Old requests without the optional claim remain valid, while new declared exception contexts suppress illustrations. Assessment evidence remains `uk-reference-evidence/v3`. No database migration is required: shipment audit events still retain metadata and a digest, not the complete claim or source bodies. Securely retain the request and full response for replay.

## Verification and remaining work

Local warnings-as-errors Release testing passed **486 offline xUnit cases**, with 16 optional live API cases skipped. The 33 Python tests, task/integrity/CI checks, limited secret-pattern scan and whitespace checks passed. The extended hosted container suite covers real authenticated relief/accounting/NI requests, strict JSON, size bounds and shipment claim/audit retention; its results are recorded below.

Hosted verification of implementation commit `d9663d1` passed all six regular jobs in [run 42294049414](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42294049414): 486 xUnit passes/16 optional skips, 33 Python passes, 11 anonymous HTTP smoke assertions and 199 PostgreSQL/container assertions with cleanup=true, plus Release build/publish, dependency/UI and full-history secret checks. Optional public/managed live jobs were not enabled. [PR #8](https://github.com/abuzuhri/GlobalTradeTax/pull/8) records final-head status and review.

Tests include mixed ordinary/conditional lines, evidence hash changes, no double reduction, unlisted headings, false payment flags, missing evidence, procedure/NI suppression, overflow and precision loss. These are engineering regressions, not independently approved tax cases. TASK-008 remains in progress for independently reviewed eligibility, valuation, NI rules, rounding and authoritative payable calculation; managed live verification still requires provisioned credentials. See the [shipment completion gates](UK_SHIPMENT_REVIEW.md).
