# TASK-008: UK import VAT & landed cost

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-02

## Objective
Determine VAT rate, taxable base, reliefs, freight, insurance, currency conversion and rounding.

## Existing project references
src/GlobalTradeTax.Api/LandedCost.cs

## Detailed checklist
- [ ] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [ ] Implement minimal, isolated C# changes with explicit error handling and fail-closed behavior.
- [ ] Add unit tests for success, missing data, invalid input, and failure modes.
- [ ] Add integration/contract tests where dependencies are involved.
- [ ] Update API documentation, source attribution and configuration examples.
- [ ] Record test commands, outcomes and evidence in the task log.
- [ ] Review security, tenant isolation and backward compatibility.

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
