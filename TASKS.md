# GlobalTradeTax — Master task register

**Single source of truth for planned work.** Detailed task files live in `tasks/`. This tracker describes implementation status, not regulatory approval or production readiness.

## Status vocabulary
- `not-started`: not implemented.
- `in-progress`: partial code exists; acceptance criteria unmet.
- `blocked`: external prerequisite required.
- `done`: acceptance criteria evidenced and reviewed.

**Current local baseline (2026-11-04):** .NET 8 Release build passed with zero warnings/errors; 219 offline xUnit tests (eight live probes skipped), four separate public live UK probes, 25 Python tests, 20 general HTTP smoke assertions, six public authenticated UK checks and two missing-managed-credential checks passed. See [managed authentication and evidence](docs/UK_MANAGED_API.md). The latest Saleor lifecycle/container evidence is recorded [separately](docs/SALEOR_APP_LIFECYCLE.md). The historical hosted run passed four jobs and failed on two confirmed documentation-placeholder Gitleaks matches; exact fingerprint exceptions are under verification. Live managed credentials, current access/terms review, authoritative duty/VAT calculation and production security remain open. Earlier results below describe their recorded dates. Never infer `done` from a version number.

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
| 011 | EU TARIC & destination expansion | P1 | not-started | [Task](tasks/011-eu-taric-destination-expansion.md) |
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

**2026-11-04 / Tasks 005 and 010 managed access / CI repair:** added opt-in official client-credentials authentication, shared bounded refresh and one 401 retry; public host never receives its token. Added 52 offline cases and four separately gated managed probes. Local build, 219 offline xUnit cases, four public live probes, 25 Python tests and both isolated HTTP configurations passed. Live managed credentials are absent locally and in repository secrets. Identified the old hosted failure as two documentation placeholders and added exact historical Gitleaks fingerprint exceptions. Updated hosted results will be linked after review-branch verification. See [runbook](docs/UK_MANAGED_API.md).
