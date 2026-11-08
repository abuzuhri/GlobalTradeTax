# TASK-011: EU TARIC & destination expansion

- **Status:** in-progress
- **Priority:** P1
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-02

## Objective
Add licensed EU TARIC and VAT sources; country-specific conditions; later GCC and US inbound.

## Existing project references
docs/SOURCES.md; src/GlobalTradeTax.Api/TradeDataCatalog.cs

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [ ] Implement minimal, isolated C# changes with explicit error handling and fail-closed behavior.
- [x] Add reference-guidance and validation tests across all member codes.
- [ ] Add integration/contract tests where dependencies are involved.
- [ ] Update API documentation, source attribution and configuration examples.
- [ ] Record test commands, outcomes and evidence in the task log.
- [ ] Review security, tenant isolation and backward compatibility.

## Definition of done
Verified official-source cases per destination; provenance and effective dates retained.

## Evidence / decision log
- 2026-11-08: Added authenticated EU member-state reference guidance and consistent EU compliance reminders. Local Release tests: 563 passed, 16 optional live probes skipped; 37 Python tests passed. Source links and territorial/financial limits are documented in [EU destination guidance](../docs/EU_DESTINATION_REFERENCE.md). Isolated container and hosted per-commit results are tracked in the review PR. These results are not evidence of payable tariff or VAT rates.
- 2026-11-08: Start the EU destination reference slice: authenticated review for the 27 EU member-state codes, current official TARIC and VAT source guidance, explicit unresolved territorial and national-tax checks, and consistent EU compliance checks. No rates, eligibility decisions or payable results; dated legal applicability, data rights and provider integration remain open.
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?
