# TASK-008: UK import VAT & landed cost

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-05

## Objective
Determine VAT rate, taxable base, reliefs, freight, insurance, currency conversion and rounding.

## Existing project references
src/GlobalTradeTax.Api/LandedCost.cs

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [x] Implement isolated UK review changes with explicit error handling and fail-closed behavior.
- [x] Add unit tests for success, missing data, invalid input and failure modes (executed 2026-11-05).
- [x] Add recorded/public live contracts and real authenticated container/database checks (executed 2026-11-05; managed live remains open).
- [x] Update API documentation, source attribution and configuration examples.
- [x] Record test commands, outcomes and evidence in the task log.
- [x] Review UK endpoint bounds, authentication, tenant audit isolation and evidence-v3 compatibility.

## Definition of done
Per-line and shipment totals reconcile; VAT exceptions tested; uncertainty reported.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

## 2026-11-02 implementation note
- Added `UkVatReadiness.cs` with explicit evidence gates, illustrative VAT base arithmetic, and never-payable output.
- Added four unit tests. Build/tests not executed in this environment.
- Still blocked on official VAT classifications, reliefs, UK valuation scope, rounding, and real API fixtures.

## 2026-11-02 v2.9 progress
- Added `UkVatClassificationGate.cs` to collect commodity, territory (GB vs XI), origin, import procedure, rate-source validity dates and exemption evidence without assuming a VAT rate.
- Added five unit tests for self-asserted evidence, Northern Ireland, expiry, missing inputs and insecure source URLs.
- Every result remains `requires_review`; `ConfirmedVatRatePercent` remains null pending independently validated UK VAT classification and procedure rules.
- Tests require .NET SDK execution; source presence does not establish passing tests.

## 2026-11-02 v3.0 progress
- Added `UkVatQuoteReview.cs` as a fail-closed orchestration layer for VAT classification and arithmetic readiness.
- Added authenticated POST `/v1/references/uk/vat/review` and five xUnit tests.
- Rejects negative input, flags rate mismatches, NI and non-GBP currency; payable amounts remain null.
- Documentation: `docs/V30_UK_VAT_QUOTE_REVIEW.md`.
- **Still in progress**: no live UK fixtures, legal VAT validation or executable test result yet.

## 2026-11-02 v3.1 progress
- Suppress illustrative GBP values on currency mismatch, NI destination, rate mismatch, expired rate, or invalid core classification.
- Added assertions and two new tests. No .NET test run or legal tariff verification yet.

## 2026-11-05 — Full UK engineering completion pass
- Requested scope: finish the remaining UK engineering across GB and XI. Implement territory-bound reference access/evidence, structured measure/components/conditions and document/quota/preference review, bounded duty arithmetic, line/shipment VAT cost reconciliation and UK compliance gates. Preserve null payable results whenever authority, eligibility or data is unverified. Add recorded official contracts, arithmetic regressions, authenticated integration checks and a final per-criterion readiness report. Managed-live credentials and independent rule/rights approval are external prerequisites, not assumed complete.

## 2026-11-05 — Expanded UK verification evidence
- Added authenticated bounded shipment review with actual goods origin, explicit measure selection, purpose-specific dated currency evidence, cost inclusion/scope, exact penny allocation, mixed proposed VAT rates and line/shipment reconciliation. NI/low-value/gift/special procedures and conditional VAT treatments suppress amounts. Missing legacy VAT evidence now suppresses illustrations. Provider evidence must match actual goods origin and total overflow stays unavailable. All new verification/approval flags remain false and payable values null.
- PASS locally: warnings-as-errors Release test build; 378 offline xUnit cases, 16 optional live skips; separate ten-case public live run; 32 Python tests; 112 PostgreSQL/container assertions with cleanup=true; repository/task/CI guards and whitespace check. Hosted verification is recorded in the review PR after execution.
- [Current API, compatibility, source attribution and per-criterion completion gates](../docs/UK_SHIPMENT_REVIEW.md). Status remains in-progress where those acceptance criteria are unmet; no deployment or collection is enabled.
