# TASK-011: EU TARIC & destination expansion

- **Status:** in-progress
- **Priority:** P1
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-08

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

## GCC reference slice (2026-11-08)
Scope: authenticated, tenant-limited static reference review for BH, KW, OM, QA, SA and AE, with national customs and tax sources and unresolved shipment checks. No tariff or VAT calculation, legal eligibility, clearance, payment or schema changes. Source review dates are not effective dates. Independent rights and legal review and live authoritative data remain open; TASK-011 remains in progress.


The endpoint `GET /v1/references/gcc/{country}` accepts the six ISO codes case-insensitively and rejects other inputs. It uses the existing authenticated tenant budget. Country-specific official source pointers and unresolved checks are also reflected in destination routing and compliance. Review date records source inspection, not a legal effective date or applicability determination. GCC membership does not establish a uniform national VAT, applicable rate, exemption or territorial scope. The references provide no rates, declaration, payment, or shipment approval. No migration or provider is introduced. Existing UK, tenant and financial behavior is retained.

Sources are listed in [the registry](../docs/SOURCES.md). The Qatar and Kuwait tax authority links provide general national tax context, not confirmation that VAT applies or does not apply. A live, effective-dated tariff and national tax dataset, terms/data rights review, shipment-specific evidence and independent legal approvals remain open. Linking to official sources does not establish rights to reuse their data.

Local Release test run: 537 passed and 16 optional live probes skipped; Python: 39 passed. Hosted database/container results are tied to the PR's tested commit and run, not this preliminary local result.
