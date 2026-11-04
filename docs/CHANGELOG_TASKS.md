# Task register change log

## 2026-11-04 — TASK-005 managed access and TASK-010 hosted CI repair
- Added opt-in managed GB authentication against the fixed official OAuth endpoint with encoded form credentials, memory-only tokens, expiry/rotation handling, concurrent refresh control, bounded bodies/timeouts and one retry after 401. Public requests receive no managed tokens and failures never fall back.
- Added 52 offline cases and four separately gated managed probes. PASS locally: zero-warning build, 219 offline xUnit cases (eight live probes skipped), separate four-case public live run, 25 Python tests, 20 general HTTP smoke assertions, six public authenticated UK checks and two missing-credential checks. Real managed credentials are absent; its live probes were not run.
- Inspected old hosted run 41683857783: application/dependency/UI/repository checks passed; history scan failed on two initial documentation placeholders. Added only the exact two finding fingerprints to `.gitleaksignore`, retaining default rules/full-history coverage; updated scan/hosted verification is tracked in TASK-010.
- Hosted verification subsequently passed for [source commit 2673050](https://github.com/abuzuhri/GlobalTradeTax/commit/267305009a92ffbf94e8929c2ae95b2192118376): [PR workflow](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879751626) and [manual public-live workflow](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879785451). Confirmed 219 offline passes/eight skips, four public live passes, build/publish/audits and a clean full-history scan with the reviewed exact exceptions. [Draft PR #1](https://github.com/abuzuhri/GlobalTradeTax/pull/1) is ready for review; actual managed credentials and broader task criteria remain open.
- [Managed API runbook and evidence](UK_MANAGED_API.md). TASK-005 and TASK-010 remain in progress.

## 2026-11-04 — TASK-005 live UK contract correction
- Probed the official legacy UK v2 point endpoints and found commodity `data.id` is an internal identifier, with its code in `attributes.goods_nomenclature_item_id`; resource types are singular. Fixed validation and added the documented v2 Accept header.
- Added four exact public JSON fixtures with SHA-256/UTC provenance and OGL v3 attribution, 24 offline regression cases and four opt-in live probes. Added a manually enabled CI contract job; no scheduled monitor or hosted run.
- PASS: Release build (zero warnings/errors), 167 offline xUnit tests with four live cases skipped, separate four-case live run, 25 Python tests, 10 general HTTP smoke assertions and six authenticated live UK route checks; repository/static CI/limited secret guards.
- Current official docs describe a managed OAuth API on a different host. Legacy access worked during this check; migration, current service-terms review and broader coverage remain open. All payable calculation gates remain closed. [Evidence and reproduction](UK_LIVE_CONTRACT_VERIFICATION.md).

## 2026-11-02
- Created master TASKS.md and 14 detailed task files.
- Classified statuses conservatively; no new functionality claimed.

- Added UK JSON:API structural validation and failure tests for task 005; added GitHub Actions .NET 8 build/test workflow (not yet executed).

## 2026-11-02 — v2.5 UK lookup coverage
- TASK-006: avoid silent geographical lookup truncation; report omitted IDs when over 20.
- Exactly 20 lookups is no longer incorrectly flagged as truncated.
- Added three xUnit tests; execution pending .NET SDK/CI.

- 2026-11-02 v2.6: TASK-007 started. Added isolated fail-closed illustrative UK duty expression evaluator and unit test cases; no live API or .NET execution verification.

- 2026-11-02 v2.7: TASK-007 corrected specific-duty evaluator to work without customs value, added tests and explicit documentation. Build/test pending.

- 2026-11-02 v2.8: Started TASK-008; added fail-closed VAT readiness arithmetic and four tests. No legal VAT rate inferred; no payable amount. Compilation not verified.

## v2.9 — 2026-11-02 — TASK-008
- Added fail-closed UK VAT classification evidence gate and five xUnit test cases.
- Explicitly distinguish GB and XI; no rates, exemptions, or legal determinations are inferred.
- Not compiled or executed in this environment. TASK-008 remains in-progress.

## v3.0 — 2026-11-02
- TASK-008: UK VAT review coordinator, tenant-authenticated API and five unit tests; no payable calculations.
- Build and tests not run: .NET SDK unavailable.

## 2026-11-02 — v3.1
- TASK-008: Added fail-closed suppression of misleading illustrative GBP VAT figures for inconsistent input, plus tests.
- Build and tests not run (dotnet SDK unavailable); no production/legal approval.

## v3.2 — 2026-11-02 — TASK-010
- Added CI jobs with build warnings-as-errors, xUnit TRX artifacts and publish artifact.
- Added local offline integrity check for master task links and UK tasks; executed successfully.
- .NET build/tests and hosted CI still unverified. Security scanning and smoke tests remain open.

## v3.3 — 2026-11-02 — TASK-010
- Added CI dependency vulnerability audit with direct and transitive NuGet package coverage.
- Added fail-closed JSON report validation and Python unit tests.
- Build/test workflow has not been executed in GitHub Actions; no production certification.

### v3.4 — Secret scanning baseline (TASK-010)
- Added tracked-file high-confidence secret guard and five unit tests.
- Added secret guard to GitHub Actions repository-checks.
- Local verification: 9 Python tests, 14-task integrity check and tracked-file scan passed.
- No .NET build or hosted CI run; not production-ready.

### 2026-11-02 — v3.5 Git history secret scanning (TASK-010)
- Added a dedicated Git-history Gitleaks job and static workflow guard.
- Added 3 offline tests (12 Python tests total, locally passed).
- Build, hosted CI and Gitleaks execution not verified in this environment.

### v3.6 — Runtime smoke-test gate (2026-11-02)
- Task 010: added a bounded, no-credentials API smoke test and CI integration.
- Added three smoke contract tests; local Python test suite: 15 passed.
- No live .NET runtime or UK tariff source validation performed.

## v3.7 — Anonymous UK route smoke coverage
- Broadened smoke test from 2 to 7 protected API routes and added loopback-only target guard.
- Added regression coverage for every route; 16 offline Python tests passed.
- Updated task 010. .NET and hosted CI not run.

## v3.8 — Local smoke redirect safety (TASK-010)
- Disable HTTP redirects in smoke client; reject ambiguous target base URLs.
- Add two offline regression tests and documentation.
- .NET build, live UK API and GitHub Actions remain unverified.

## 2026-11-02 — TASK-010 Windows local verification
- Fixed two C# compilation errors and an exception on missing UK group-membership evidence found by executing the real build and xUnit suite.
- Selected stable .NET 8 through `global.json`; added geography regression cases.
- Corrected the NuGet audit parser for real clean SDK JSON, preserved fail-closed metadata checks and expanded CI auditing to both projects with output version 1.
- Added an explicit non-ignored untracked-file secret scan mode and inventory tests; preserved the repository index and all original files.
- Verified locally: restore/build/publish; 64 xUnit tests; 25 Python tests; 9 HTTP assertions against both built and published API; both NuGet audits; 14-task integrity; workflow guard and actionlint.
- Hosted CI, required PR checks, container smoke, Git-history scan and production readiness remain open. See [verification evidence](TASK010_WINDOWS_VERIFICATION.md). No commit/PR created; the local repository has no commits.

## 2026-11-02 — TASK-012 Saleor review-only pilot
- Added a disabled-by-default signed-payload review endpoint with merchant authentication, tenant/store binding, manually pinned RSA public keys, five-minute freshness and a streamed byte limit.
- Preserved nullable payable amounts and added deterministic review references, source-line mapping and unresolved customs evidence checks. No live tax webhook registration, order changes or durable event processing.
- Added JSON/GraphQL/configuration samples and [integration documentation](SALEOR_REVIEW_PILOT.md).
- Verified: Release build/publish, 44 new and 108 total xUnit tests, 25 Python tests, 10 API smoke assertions and independently signed HTTP requests to the published app. No new package dependencies.
- TASK-012 remains in progress; live sandbox/schema verification, secure App registration/token persistence, Dashboard UI, durable retries, verified line-level amounts and checkout failure controls remain open.

## 2026-11-02 — TASK-012 running Saleor sandbox and contract correction
- Started digest-pinned Saleor 3.23.45 and Dashboard 3.23.41 in WSL Docker, with separate PostgreSQL/Valkey data and loopback-only ports. Added repeatable start/stop, random sandbox credentials and runtime probe helpers.
- Runtime schema validation found a conflicting variant-field nullability assumption. Added distinct checkout/order aliases, compatible importer mapping and three regression cases.
- Verified runtime schema (0 errors), staff login, rendered Dashboard, Saleor-generated signature/JWKS matching and controlled replay to the published .NET API. Signed contents remained synthetic; no automatic checkout webhook or payable tax flow was enabled.
- Release build/publish and 111 xUnit / 25 Python tests passed. See [sandbox evidence](SALEOR_SANDBOX.md). App installation, review UI and real checkout-event delivery remain open.

## 2026-11-03 — TASK-012 lifecycle and key rotation

- Added signed self-lifecycle subscriptions, recipient-aware event binding, durable revocation, verified reinstallation and live-token gates without extra App permissions. Historical reviews survive; stale-installation events and writes are rejected.
- Added bounded operator-pinned signing-key overlap and lazy AES-GCM rewrapping using previous encryption keys. Sandbox keys were not rotated; automated cryptographic tests verify overlap/retirement/migration/tamper rejection.
- Fixed missing shared media between Saleor API and worker after real lifecycle delivery failed with a missing payload file. Retest passed deactivate/reactivate/delete/reinstall, preserved four historical references and delivered two new cart reviews; Dashboard rendered six cards.
- PASS: Release build, 143 xUnit tests, 25 Python tests, container build, 10 HTTP smoke checks, npm audit (zero vulnerabilities), reproducible frontend output, actionlint and repository guards. No hosted CI run or production deployment.
- [Evidence, configuration and remaining work](SALEOR_APP_LIFECYCLE.md). TASK-012 remains in-progress.

## 2026-11-03 — TASK-012 installed Saleor review App
- Added manifest installation, token verification against preconfigured Saleor stores, AES-256-GCM state protection, signed asynchronous checkout-event ingestion and bounded persistent delivery deduplication.
- Added a real product/cart test and Celery worker: checkout creation and quantity update automatically delivered signed reviews. Added an Arabic AppBridge-authenticated Dashboard screen and verified actual browser display.
- Preserved null payable customs outputs, limited App permissions to MANAGE_CHECKOUTS and confined HTTP/private-Docker exceptions to the explicit Development sandbox. Kept the manual review endpoint unchanged.
- Added exact UI dependencies, generated third-party notices and a lockfile/audit/generated-bundle CI job.
- Verified locally: 124 xUnit tests, 25 Python tests, container build, 10 HTTP smoke assertions, npm audit, static CI guards/actionlint, encrypted persistence and rendered Dashboard reviews. See [evidence](SALEOR_INSTALLED_APP.md). Production hardening, key lifecycle, distributed storage and authoritative calculation remain open.
