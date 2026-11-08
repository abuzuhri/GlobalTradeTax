# Task register change log

## 2026-11-08 — TASK-003 migration ledger
- Added an operator-invoked, checksum-verified ordered migration ledger with explicit catalog-checked adoption, tracked schema/history preflight, transaction-scoped locking and atomic migration records. Adoption records its actual time rather than asserting historical execution. [Operator and recovery guidance](POSTGRES_CONTAINER_VERIFICATION.md).
- Local Release tests passed 525 offline cases with 16 optional live skips, 37 Python tests and 334 PostgreSQL/container assertions with isolated cleanup. A repeat-preflight comparison defect found in an intermediate run was fixed. Hosted evidence for each commit is tracked in the PR. Privilege, index and production recovery review remain open.

## 2026-11-07 — TASK-002 tenant request budgets
- Added bounded process-local tenant request budgets for authenticated merchant routes, shared across keys and checked before body parsing and upstream work. See [configuration and limitations](TENANT_RATE_LIMITS.md). Distributed enforcement and independent approvals remain open.
- Implementation commit `a960d8c81e0116686cdb689cae39865ed08f90dd` passed [hosted run 42377007491](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42377007491): six regular jobs, 524 xUnit passes/16 optional skips, 33 Python passes and 309 PostgreSQL/container assertions with cleanup. [PR #10](https://github.com/abuzuhri/GlobalTradeTax/pull/10) tracks later-head status.

## 2026-11-07 — TASK-009 declared control holds and TASK-002 input safeguards
- Implementation commit `8dc1bf6` passed all six regular hosted jobs: 514 xUnit cases/16 optional skips, 33 Python cases, 11 HTTP smoke assertions and 297 PostgreSQL/container assertions with cleanup=true. [Hosted run](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42355867265); [ready PR #9](https://github.com/abuzuhri/GlobalTradeTax/pull/9) tracks per-commit verification.
- Reproduce eight failures, then enforce declared export licence/ITAR holds before rate-provider access and financial completion. Persist compliance evidence with quote/idempotency snapshots and landed-cost audits; shipment-release approval is always false.
- Authenticate before bounded exact JSON on all three quote/compliance routes. Reject duplicate flags, null nested inputs, field/count violations and overflow; preserve case compatibility and existing unflagged financial provider gates. No database migration.
- Release warnings-as-errors suite passes 514 offline xUnit cases/16 optional tariff API skips, 33 Python tests and repository guards. Add actual authenticated database/API, audit, recreation and restore regressions; final hosted evidence is recorded in the ready PR. [Contract, official sources and limitations](COMPLIANCE_HOLDS.md). TASK-009/002 remain in progress.

## 2026-11-06 — TASK-008 VAT exceptions and accounting review
- Implementation commit `d9663d1` passed all six regular hosted jobs, including 486 xUnit cases/16 optional live skips, 33 Python cases, 11 HTTP smoke assertions and 199 PostgreSQL/container assertions with cleanup=true. [Hosted run](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42294049414); [ready PR #8](https://github.com/abuzuhri/GlobalTradeTax/pull/8) records final-head status.
- Add declared per-line treatment/accounting claims and sourced review decisions. Relief claims never establish zero VAT; conditional art valuation is segregated from ordinary totals, postponed accounting preserves liability illustrations, and NI/special procedures remain unresolved.
- Guard invoice/cost/VAT precision loss; authenticate before bounded exact JSON parsing on the legacy VAT route. Version shipment evidence as `uk-shipment-review/v2`; no database migration.
- Local warnings-as-errors Release tests pass 486 offline xUnit cases/16 optional API skips, 33 Python cases and repository guards. Extend authenticated container coverage; final hosted verification is recorded in the review PR. [Contract, current sources and compatibility](UK_VAT_EXCEPTIONS.md). Independent eligibility, valuation/rounding and payable approval remain open; TASK-008 stays in progress.

## 2026-11-06 — TASK-007 official duty reference cases
- Capture five synthetic live calculator journeys and pin four further arithmetic examples to maintained official tests. Archive inputs, duty rows, screenshots, exact API/source files, hashes and OGL/MIT attribution. Keep displayed duty separate from headline duty+VAT and source comparisons separate from deployed results.
- Reproduce four positive-precision-loss failures before fixing unit scaling, percentages and currency multiplication in both evaluators. Explicit zero rates remain valid; unknown/unsupported evidence and payment gates remain unchanged.
- Local warnings-as-errors Release suite passes 447 offline xUnit cases/16 optional API skips. [Evidence, replay and independent approval limits](UK_DUTY_REFERENCE_CASES.md); hosted results are recorded in the review PR. TASK-007 remains in progress.

## 2026-11-05 — TASK-004 exact-byte provenance and operator review
- Preserve bounded strict UTF-8 upload bytes/digests, reject duplicate/overlapping intervals and rates requiring storage rounding, and require consistent source provenance. Archive documents transactionally with database digest/length/foreign-key checks.
- Add operator-only reference review/history, explicit attestation/expiry, idempotent replay and predecessor-bound revisions; renewals/rejections retain earlier decisions. Migration 005 preserves older staging, with exact document/row verification for backfill before review. No calculation provider or publication gate is enabled.
- Local Release build/publish, 344 xUnit cases (52 new; ten optional live skips), 33 Python cases and repository guards passed. Corrected container run passed 154 assertions with cleanup, including review races/expiry and frozen v1.2 upgrade/backfill. [Current contract, migration and local evidence](TARIFF_PROVENANCE_REVIEW.md); [PR #5](https://github.com/abuzuhri/GlobalTradeTax/pull/5) retains final hosted checks/review. TASK-004 remains in progress.

## 2026-11-05 — UK GB/XI shipment review (TASK-005–009)
- Added separate territory-bound public/managed reference access, exact response digests, structured components/conditions/document alternatives and evidence v3. Added purpose-specific monthly invoice and XI tariff conversion; unknown evidence never selects zero or a cheapest rate.
- Added merchant-authenticated bounded shipment review with explicit line origin/measure selections, cost scope/inclusion and exact penny allocation, mixed proposed VAT rates and reconciliation. Conditional VAT, NI, low-value/gift and special procedure paths remain unresolved; payable values always null. Fixed actual-origin provider matching and legacy VAT evidence suppression; UK/controlled-goods checks require manual review.
- PASS locally: zero-warning Release test build, 380 offline xUnit cases/16 live skips, ten separate public live probes, 32 Python cases and 112 database/container assertions with cleanup. Added weekly public drift workflow, active only after merge. Managed probes remain unrun without provisioned credentials.
- [API, sources, migration-free compatibility and exact remaining completion gates](UK_SHIPMENT_REVIEW.md). Task status reflects unmet acceptance criteria; no legal approval or deployment is implied.


## 2026-11-04 — TASK-003/010 PostgreSQL and container lifecycle
- Added a disposable, labelled Compose test project derived from the root configuration and real Dockerfile. Verify fresh schema, original v0.4 upgrade/reapplication/rollback, authenticated merchant isolation/revocation, concurrent idempotency/staging, container recreation and isolated backup/restore.
- Real PostgreSQL integration exposed AuditStore sending text into a JSONB column (SQLSTATE 42804 / HTTP 500). Explicit Npgsql JSONB binding fixes the endpoint; tests verify persisted JSON and subsequent restored audit writes. Duty/tax/payable fields remain null.
- Final frozen-fixture local run: 103 integration assertions passed with cleanup; zero-warning Release build; 219 xUnit cases passed/eight live skips; 32 Python cases, actionlint and repository guards passed. Added the historical schema fixture and a CI job with redacted evidence upload; hosted execution is tracked in the review PR.
- [Verification and upgrade/recovery runbook](POSTGRES_CONTAINER_VERIFICATION.md). TASK-003/010 remain in-progress; migration ledger/release policy, required-check enforcement and production operational review remain open.
- Review and hosted evidence: [draft PR #2](https://github.com/abuzuhri/GlobalTradeTax/pull/2), based on the UK-authentication PR #1.

## 2026-11-05 — TASK-006 dated membership and measure candidates
- Pass import dates to upstream commodity/area requests and partition the cache by date. Reject dates the upstream would silently replace. Interpret the recorded typed child tree, preserve membership paths and handle both official exclusion fields with unknown evidence kept explicit.
- Add per-measure reference candidate evidence for commodity import linkage, shared import/export measures, inclusive effective windows and direct/group exclusions; preserve overlaps without selecting rates. Introduce canonical evidence v2 covering new fields with invariant-calendar dates.
- Captured four unmodified, attributed dated responses with hashes, including current/historical group 200/404 cases. PASS locally: 292 offline xUnit cases (73 new; ten live skips), six separate public live probes, 35 dated HTTP plus ten general smoke assertions, 25 Python cases, Release build/publish and repository guards.
- [Scope, source contracts, compatibility and evidence](UK_APPLICABILITY_EVIDENCE.md); [PR #4](https://github.com/abuzuhri/GlobalTradeTax/pull/4) records hosted checks and review. TASK-006 remains in progress for legal conditions, certificates, quotas, preferences and reviewed applicability. No production deployment or payable outputs.


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
