# GlobalTradeTax — Master task register

**Single source of truth for planned work.** Detailed task files live in `tasks/`. This tracker describes implementation status, not regulatory approval or production readiness.

**TASK-002 rate-limit slice (2026-11-07):** authenticated merchant routes use bounded process-local tenant budgets, with 429 and Retry-After on exhaustion. Implementation commit `a960d8c` passed [six hosted jobs](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42377007491), including 309 database assertions; [PR #10](https://github.com/abuzuhri/GlobalTradeTax/pull/10) tracks later-head checks. See [configuration and limitations](docs/TENANT_RATE_LIMITS.md); distributed enforcement, production authorization and threat-model approval remain open.

**Latest TASK-004 slice (2026-11-05):** strict byte-preserving uploads, duplicate/overlap checks, archived provenance and append-only operator review/expiry are implemented. Local suites: 344 offline xUnit and 33 Python cases passed; [full container/upgrade/restore evidence and limits](docs/TARIFF_PROVENANCE_REVIEW.md) track final verification. TASK-004 remains in progress for authoritative source agreements, independent rights review and controlled publication; no reviewed/staged record is a payable rate.

**Latest TASK-006 evidence (2026-11-05):** date-scoped UK requests/cache, typed membership paths, import/export linkage, effective windows and explicit exclusions now have 292 passing offline xUnit cases, six separately enabled public live probes and 35 dated HTTP assertions. [Scope, compatibility and verification](docs/UK_APPLICABILITY_EVIDENCE.md). TASK-006 remains in progress; legal conditions, certificates, quotas and preferences are still unresolved. The dated baseline below records the preceding slice.

**Latest UK completion pass (2026-11-05):** GB/XI shipment review, typed conditional duty evidence, separate invoice/tariff currency feeds, mixed-VAT cost reconciliation and UK manual control gates are implemented. Local checks passed 380 offline xUnit cases, ten public live probes, 32 Python tests and 112 isolated database/container assertions with cleanup. [Current contract and remaining gates](docs/UK_SHIPMENT_REVIEW.md) explicitly retain managed-live access, independently approved calculator cases and legal/valuation/NI approvals as unfinished; TASK-005–009 are not marked done.

**Latest TASK-007 evidence (2026-11-06):** five live official calculator duty rows and four published calculator test examples now match. Four reproduced precision failures are corrected without enabling payment. The local Release suite passes 447 cases with 16 optional API skips. [Case evidence and remaining approval gates](docs/UK_DUTY_REFERENCE_CASES.md). TASK-007 remains in progress.

**Latest TASK-008 evidence (2026-11-06):** explicit VAT treatment/accounting claims, separate conditional art valuation, NI movement checks and strict legacy JSON are implemented. Local Release tests pass 486 offline cases/16 optional live skips and 33 Python cases. [Contract, compatibility and remaining tax approval](docs/UK_VAT_EXCEPTIONS.md). Shipment evidence is now `uk-shipment-review/v2`; TASK-008 remains in progress.

**Latest TASK-009/002 evidence (2026-11-07):** declared licence/ITAR holds now block quote completion and landed-cost provider calls and totals. Compliance checks report stable hold reasons and false shipment-release approval. Strict authenticated input parsing rejects duplicate control flags and invalid/overflowing requests. Local checks pass 514 offline xUnit cases/16 optional skips and 33 Python cases. [Behavior, compatibility, sources and remaining approval](docs/COMPLIANCE_HOLDS.md). Both tasks remain in progress.

**Latest TASK-003 slice (2026-11-08):** an operator-invoked migration ledger now checks immutable files and ordered history, verifies explicit legacy adoption and schema drift, serializes migrations and records changes transactionally. Local checks passed 525 offline xUnit cases (16 optional live skips), 37 Python cases and 334 isolated PostgreSQL/container assertions on implementation commit `eaefcf0` with cleanup. [Runbook and limitations](docs/POSTGRES_CONTAINER_VERIFICATION.md); hosted per-commit results belong to the PR. TASK-003 remains in progress for production privileges, index review and recovery exercises.

**JP/KR/SG TASK-011 slice (2026-11-09):** Three-country authenticated reference guidance is implemented with official national sources and unresolved shipment checks; authoritative data integration, rights and independent legal review remain open. See [task](tasks/011-eu-taric-destination-expansion.md).

**Latest TASK-011 slice (2026-11-08):** authenticated source guidance covers 27 EU member-state codes, with explicit unresolved territory and separate tariff and national VAT review. Local Release tests passed 563 cases with 16 optional live skips; 37 Python tests passed. [Guidance and limits](docs/EU_DESTINATION_REFERENCE.md). Actual tariff and VAT integration, source rights and independent legal approval remain open.

**TASK-002 API-key lifecycle slice (2026-11-08):** expiry and operator-managed issue, list, rotation and revocation are implemented with transactional tenant-bound audit. [Runbook and limits](docs/TENANT_KEY_LIFECYCLE.md); production authorization and threat-model approval remain open. PR checks track current verification.

**TASK-011 GCC reference slice (2026-11-08):** six national destination references are available for review only; calculation, legal approval and data rights remain open. See [scope and evidence](tasks/011-eu-taric-destination-expansion.md).

**TASK-011 India and China slice (2026-11-09):** in progress: national reference guidance for IN and CN only; no calculation or legal approval. See [task scope](tasks/011-eu-taric-destination-expansion.md).
**TASK-011 US, Canada and Mexico reference slice (2026-11-09):** reference-only guidance and compliance reminders for US, CA and MX; calculation, legal eligibility and data rights remain open. See [scope](tasks/011-eu-taric-destination-expansion.md) and [sources](docs/SOURCES.md).

## Status vocabulary
- `not-started`: not implemented.
- `in-progress`: partial code exists; acceptance criteria unmet.
- `blocked`: external prerequisite required.
- `done`: acceptance criteria evidenced and reviewed.

**Current verified baseline (2026-11-04):** .NET 8 Release build passed with zero warnings/errors; 219 offline xUnit tests (eight live probes skipped), 32 Python tests and 103 isolated PostgreSQL/container assertions passed locally. Fresh/legacy/restored databases and cleanup passed with PostgreSQL 16.15. See [database evidence](docs/POSTGRES_CONTAINER_VERIFICATION.md). The preceding UK-authentication source passed hosted CI and four public live probes; [draft PR #1](https://github.com/abuzuhri/GlobalTradeTax/pull/1) and the [managed runbook](docs/UK_MANAGED_API.md) retain those results. The new `container-postgres` job records hosted integration evidence in the review PR. The latest Saleor evidence is [separate](docs/SALEOR_APP_LIFECYCLE.md). Live managed credentials, current access/terms review, authoritative duty/VAT calculation and production security remain open. Earlier entries describe their recorded dates. Never infer `done` from a version number.

## Priority and dependencies
- **P0:** correctness, security, data integrity, UK duty calculation and tests.
- **P1:** compliance, operational readiness, geographic expansion.
- **P2:** integrations and settlement after core correctness.

| ID | Task | Priority | Status | Detailed file |
|---|---|---|---|---|
| 001 | Foundation & API contracts | P0 | in-progress | [Task](tasks/001-foundation-api-contracts.md) |
| 002 | Tenant security & access control | P0 | in-progress | [Task](tasks/002-tenant-security-access-control.md) |
| 003 | PostgreSQL schema & migrations | P0 | in-progress | [Task](tasks/003-postgresql-schema-migrations.md) |
| 004 | Tariff ingestion & provenance | P0 | in-progress | [Task](tasks/004-tariff-ingestion-provenance.md) |
| 005 | UK live API contract & fixtures | P0 | in-progress | [Task](tasks/005-uk-live-api-contract-fixtures.md) |
| 006 | UK geography & measure applicability | P0 | in-progress | [Task](tasks/006-uk-geography-measure-applicability.md) |
| 007 | UK duty calculation engine | P0 | in-progress | [Task](tasks/007-uk-duty-calculation-engine.md) |
| 008 | UK import VAT & landed cost | P0 | in-progress | [Task](tasks/008-uk-import-vat-landed-cost.md) |
| 009 | Compliance & restricted goods | P1 | in-progress | [Task](tasks/009-compliance-restricted-goods.md) |
| 010 | Quality CI & security | P0 | in-progress | [Task](tasks/010-quality-ci-security.md) |
| 011 | EU TARIC & destination expansion | P1 | in-progress | [Task](tasks/011-eu-taric-destination-expansion.md) |
| 012 | Carrier & commerce integrations | P2 | in-progress | [Task](tasks/012-carrier-commerce-integrations.md) |
| 013 | Payments, ledger & reconciliation | P2 | not-started | [Task](tasks/013-payments,-ledger-reconciliation.md) |
| 014 | Operations, monitoring & launch | P1 | not-started | [Task](tasks/014-operations,-monitoring-launch.md) |

## Execution order
1. Complete **005** (live UK contract verification) and **010** (build/CI baseline).
2. Finish **006** (applicability) and **004** (trusted provenance).
3. Implement **007** (duty) then **008** (VAT and landed cost).
4. Harden **002/003/009/014** before external merchant rollout.
5. Expand through **011/012/013** only after verified UK baseline.

## Change-management rules
1. Every new feature requires a task file and a master-register row **before coding**.
2. Keep task IDs stable; do not delete historical tasks.
3. Update status only with evidence (build logs, test results, official source validation).
4. Record blockers, assumptions, data licensing and effective dates.
5. Monetary duty/VAT remains `null` if the legally applicable rule cannot be verified.
6. On completion, add a dated entry to `docs/CHANGELOG_TASKS.md` and link a commit/PR when available.

## Sources
- UK backend: https://github.com/trade-tariff/trade-tariff-backend
- UK frontend: https://github.com/trade-tariff/trade-tariff-frontend
- UK API docs: https://docs.trade-tariff.service.gov.uk/
- UK government tariff: https://www.gov.uk/trade-tariff

The open-source backend is an engineering reference; source-code licensing and rights to reuse tariff data are separate matters.

> v3.6 task 010: API runtime smoke test added to CI; local Python suite 15 passed. Hosted CI and .NET execution pending. See `docs/V36_API_SMOKE_TEST.md`.


**v3.7 / Task 010:** expanded anonymous-route smoke coverage and loopback guard; 16 offline Python tests passed. Hosted CI and .NET build/tests still pending. See `docs/V37_AUTH_SMOKE_COVERAGE.md`.

**v3.8 / Task 010:** local smoke HTTP redirect guard and regression tests; see `docs/V38_SMOKE_REDIRECT_SAFETY.md`. .NET/hosted CI still pending.

**2026-11-02 / Task 010 Windows verification:** local .NET compilation, tests, publish and HTTP smoke now pass after fixes. Earlier NOT RUN entries above describe previous environments. Task 010 remains `in-progress`: hosted CI, required PR checks, container smoke and history scanning remain open. See [actual commands and results](docs/TASK010_WINDOWS_VERIFICATION.md).

**2026-11-02 / Task 012 Saleor pilot:** added a disabled-by-default, signed, tenant-bound payload review endpoint. Local verification: 108 xUnit tests (44 new Saleor cases), 25 Python tests and 10 HTTP smoke assertions passed; independently signed HTTP requests verified against the published app. This is not an installed Saleor App or a payable tax integration. See [pilot evidence and remaining work](docs/SALEOR_REVIEW_PILOT.md). Task remains `in-progress`; existing core correctness priorities are unchanged.

**2026-11-02 / Task 012 running sandbox:** Core 3.23.45 and Dashboard 3.23.41 now run locally in WSL Docker with digest-pinned images and isolated data. Runtime schema validation exposed and resolved a variant-field conflict; the corrected query passes, as does replay of a genuine Saleor signature into the published importer. Total xUnit tests: 111 passed; Python: 25 passed. No real checkout event or automatic tax integration has been validated. See [sandbox evidence](docs/SALEOR_SANDBOX.md).

**2026-11-03 / Task 012 installed review App:** App installation, encrypted tenant/store state, automatic signed checkout-created/updated delivery and authenticated Dashboard review display now pass in the local sandbox. Actual cart quantity changed from 2 to 3; payable customs fields stayed null. Local suites: 124 xUnit and 25 Python tests passed; 10 container HTTP smoke checks passed. See [installed App evidence](docs/SALEOR_INSTALLED_APP.md). Earlier synthetic-only statements describe the previous slice; production hardening and calculation gates remain open.

**2026-11-03 / Task 012 lifecycle and key rotation:** signed self-lifecycle delivery, deactivate/reactivate/delete/reinstall and old-installation isolation are implemented. Actual sandbox reinstallation retained four historical reviews; Dashboard displayed six cards after new cart events. Operator-controlled signing-key overlap and encrypted-state migration passed automated tests; live sandbox keys were not rotated. Local build, 143 xUnit tests, 25 Python tests and 10 container HTTP smoke checks passed. Fixed API/worker media sharing discovered by the lifecycle test. See [latest evidence/runbook](docs/SALEOR_APP_LIFECYCLE.md). Task remains in-progress.

**2026-11-04 / Task 005 live UK contract:** corrected a real internal-resource-ID/singular-type mismatch discovered against the official legacy API. Added attributed response fixtures, 24 offline regressions and four manually enabled live CI probes. Local Release build, 167 offline xUnit cases (four live cases skipped), separate four-case live run, 25 Python tests, 10 general smoke assertions and six authenticated UK HTTP checks passed. Current managed-credentials migration, service-terms review, broader coverage and hosted monitoring remain open; TASK-005 stays in-progress. See [contract and actual evidence](docs/UK_LIVE_CONTRACT_VERIFICATION.md).

**2026-11-04 / Tasks 005 and 010 managed access / CI repair:** added opt-in official client-credentials authentication, shared bounded refresh and one 401 retry; public host never receives its token. Added 52 offline cases and four separately gated managed probes. Local build, 219 offline xUnit cases, four public live probes, 25 Python tests and both isolated HTTP configurations passed. Live managed credentials are absent locally and in repository secrets. Identified the old hosted failure as two documentation placeholders and added exact historical Gitleaks fingerprint exceptions. Both [PR checks](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879751626) and [manual public-live run](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879785451) passed for source commit 2673050. See [runbook](docs/UK_MANAGED_API.md).

**2026-11-04 / Tasks 003 and 010 container/PostgreSQL verification:** added an isolated integration runner covering fresh/legacy/restored databases, merchant isolation and revocation, concurrent idempotency/staging, persistence after container recreation and backup/restore. Found and fixed the real JSONB/text audit parameter mismatch behind HTTP 500. The final frozen-fixture local run passed 103 integration assertions and cleanup; Release build, 219 xUnit cases (eight live skips) and 32 Python cases passed. A frozen original v0.4 schema makes upgrade coverage independent of current migrations. The `container-postgres` CI job uploads hosted evidence; its result is tracked in [draft PR #2](https://github.com/abuzuhri/GlobalTradeTax/pull/2). [Runbook and remaining criteria](docs/POSTGRES_CONTAINER_VERIFICATION.md). Both tasks remain in-progress.

- TASK-011: BR, CL, CO and PE country reference guidance added; live authoritative integration and overall approval remain open.
**TASK-011 Malaysia, Indonesia, Thailand, Vietnam and Philippines slice (2026-11-09):** in progress: authenticated, tenant-budgeted commercial-import reference guidance for MY, ID, TH, VN and PH only; no rates, eligibility or approval. See [task scope](tasks/011-eu-taric-destination-expansion.md).
**TASK-011 AU/NZ slice (2026-11-09):** AU and NZ authenticated country reference guidance adds official source pointers and unresolved import, GST, origin, biosecurity and territorial checks. Calculations, rights and legal approval remain open. [Scope](tasks/011-eu-taric-destination-expansion.md) and [sources](docs/SOURCES.md).

**TASK-011 AR/UY/PY slice (2026-11-09):** in progress: extend authenticated Latin America reference guidance to Argentina, Uruguay and Paraguay; official source review and shipment-specific checks only. Rates, rights, live integration and independent approval remain open.
**2026-11-09 destination Region contract correction:** Restore the advertised EU/GCC/LATAM/OTHER grouping for recently added country guidance, retaining country-specific references and unresolved eligibility checks. Verify quote output and capability metadata; no legal/source review, rates, provider or migration is in scope. TASK-001 and TASK-011 remain in progress.

2026-11-09: Region compatibility correction: MY/ID/TH/VN/PH, IN/CN and AU/NZ now use OTHER; EU, GCC and LATAM routing, including MX North America references, remain intact. Country sources and unresolved checks remain. Release tests: 662 passed, 16 optional live skipped; Python: 39 passed; repository, secret and CI guards passed; local API smoke: 18 checks passed. Docker unavailable locally; hosted PostgreSQL/container verification is required on the PR head. Source dates were not re-reviewed; live data rights and independent legal approvals remain open. TASK-001 and TASK-011 remain in progress.

Hosted verification of implementation commit `d27df46f6e42061fa23c297ce6c9d9f28d66c782`: [run 42683768925](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42683768925) passed all six regular jobs, including 467 isolated PostgreSQL/container assertions and cleanup. Optional live jobs were skipped. Subsequent documentation-only changes require their own head checks.
