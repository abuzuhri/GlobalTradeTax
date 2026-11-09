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

## AU/NZ reference slice (2026-11-09)
Scope: authenticated, tenant-limited country reference guidance for AU and NZ only, including official customs, GST, import and biosecurity sources and unresolved territorial and preferential eligibility checks. No rates, clearance, payment, legal decisions, provider or schema changes. Source review is not an effective date; live data, rights and independent legal review remain open.

Local validation before commit: Release .NET suite 587 passed, 16 optional live probes skipped; Python unittest suite 39 passed. Focused AU/NZ tests passed after source registry update. Database and hosted latest-head evidence is tracked in the PR.
## India and China reference slice (2026-11-09)
Scope: authenticated, tenant-budgeted static commercial import source guidance for IN and CN, with unresolved shipment and territorial checks. No rates, provider, migration or approval; source rights and independent legal review remain open.

Implemented authenticated IN/CN source guidance and compliance reminders; [source context and remaining gates](../docs/IN_CN_DESTINATION_REFERENCE.md). Local Release suite: 587 passed, 16 optional live probes skipped. Hosted per-commit checks supply container evidence. TASK-011 remains in progress.
## JP/KR/SG reference slice (2026-11-09)
Scope: authenticated tenant-limited static national reference guidance for Japan, South Korea and Singapore only. Review official customs, tax, import procedures, controls and destination or zone context. Shipment facts, calculation, payment and release remain unresolved; no schema or authoritative data integration is introduced. Source review dates do not establish legal applicability. Independent rights and legal review remain open.

The endpoint `GET /v1/references/jp-kr-sg/{country}` accepts JP, KR and SG in either case; malformed and unsupported codes return 400. It uses the existing tenant authentication and rate budget. The destination and compliance reminders remain review-only, and each response has null financial amounts and closed calculation, payment and release gates. No personal identity data or schema changes are needed. Official national sources are listed in [the registry](../docs/SOURCES.md). These three countries do not imply wider regional coverage. Classification, actual goods origin, preference proof, valuation, procedure, taxes and fees, importer responsibility, controls and exact destination or zone remain unresolved. Links and agreements do not establish eligibility or clearance.

Local preliminary validation: 585 .NET tests passed, 16 optional live tests skipped, and 39 Python tests passed. The local database runner could not run: Windows has psycopg but no Docker command; WSL has Docker without access to its daemon and lacks psycopg. Hosted results must be tied to the actual tested commit and run. Existing UK, EU and GCC behavior and tenant controls remain in scope for regression checks. Source review is not legal applicability; authoritative integration, data rights and independent legal approval remain open.
## US, Canada and Mexico reference slice (2026-11-09)
Scope: authenticated tenant-limited reference guidance for US, CA and MX only, with official national customs and tax pointers and unresolved shipment, preference and exact destination checks. No rate, eligibility, payment, release, provider or schema change. TASK-011 remains in progress; source review is not a legal effective date.

Endpoint `GET /v1/references/north-america/{country}` is authenticated and shares the tenant budget. It supports US, CA and MX in either case; territory codes and other inputs are rejected. References carry null amounts and closed calculation, payment and release gates. Destination and compliance guidance retain unresolved preference, actual origin and territorial tax checks. The source registry describes official pointers, not licensed or effective-dated data integration. No migration is needed. See [sources](../docs/SOURCES.md).

Local implementation verification: Release suite 586 passed, 16 optional live probes skipped; Python suite 39 passed. Hosted checks and container evidence are tied to the PR head and run. These checks do not establish legal eligibility or data rights.
