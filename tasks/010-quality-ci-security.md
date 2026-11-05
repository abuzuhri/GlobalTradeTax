# TASK-010: Quality CI & security

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-04

## 2026-11-04 — Hosted history-scan repair slice
- Inspected hosted run 41683857783 for commit b8eb349: application, dependency, frontend and repository checks succeeded; Gitleaks failed with two findings.
- Reproduced both findings locally using checksum-verified Gitleaks 8.24.2 with redacted reporting. Both are `curl-auth-header` matches in the initial commit's documentation examples (`README.md:41`, `docs/IMPLEMENTATION.md:16`). The README uses `your-secret-key`; the multi-line implementation example contains `replace-with-a-long-random-secret` in its API-key header.
- Added only the two exact historical finding fingerprints to the scanner ignore file, with their rationale. All default rules and full-history coverage remain enabled. Local rescan with checksum-verified 8.24.2 passed with zero remaining findings; actionlint 1.7.12 and `git diff --check` passed.
- Hosted [PR run](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879751626) and [manual public-live run](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879785451) both passed for source commit 2673050. Hosted logs confirm three commits scanned with no remaining findings, 219 xUnit passes/eight live skips, zero build warnings/errors, and ten HTTP smoke assertions; the manual job separately passed four public live probes. Changes are in [draft PR #1](https://github.com/abuzuhri/GlobalTradeTax/pull/1).
- Status remains in-progress: required PR-check enforcement, current container/PostgreSQL validation and broader production security/recovery work remain open. No history rewrite, merge or production deployment was performed.

## Objective
Compile with .NET 8, run xUnit, integration tests, static analysis, dependency and secret scans in CI.

## 2026-11-04 — Container/PostgreSQL verification slice
- Build the actual Dockerfile and run a unique, disposable Compose project with PostgreSQL 16; keep existing local databases and the Saleor sandbox separate.
- Verify all four SQL scripts on a new volume, upgrade a seeded v0.4 database without deleting data, reapply the scripts, compare schemas and test transactional failure rollback.
- Exercise database-backed merchant authentication, cross-merchant quote isolation, concurrent idempotency, key/tenant revocation, tariff staging and audit writes over the running API. Keep duty/tax/payable totals null.
- Verify data after API/database recreation and an isolated pg_dump/pg_restore exercise. Clean up only the resources created by the test run and redact generated credentials from reports.
- Add the repeatable integration runner to CI and document commands, actual results and remaining operational limits. Address concrete runtime defects found by these checks.

## 2026-11-04 — Container/database local evidence
- The first actual PostgreSQL-backed API run passed 53 assertions before exposing HTTP 500 on landed-cost audit writes (SQLSTATE 42804). Bind AuditStore's serialized details explicitly as JSONB; the corrected full run passed 103 assertions and cleanup succeeded.
- Coverage includes the real Dockerfile/root Compose configuration, fresh initialization, a frozen v0.4 upgrade, idempotent SQL reapplication, transaction rollback, merchant isolation/revocation, concurrent quote/staging deduplication, non-payable outputs, API/database recreation and isolated backup/restore with new writes.
- Local Release build passed with zero warnings/errors; 219 xUnit cases passed with eight live cases skipped; 32 Python cases passed, including cleanup/isolation/redaction guard failures. Repository/static CI/limited secret/whitespace checks passed.
- The final run against the frozen original schema also passed all 103 assertions and cleanup. Checksum-verified actionlint 1.7.12 passed with optional ShellCheck/Pyflakes disabled. All six existing Saleor containers remained running.
- Added the `container-postgres` CI job and JSON artifact; hosted execution is tracked in [draft PR #2](https://github.com/abuzuhri/GlobalTradeTax/pull/2). [Commands, sources, actual results and operational limits](../docs/POSTGRES_CONTAINER_VERIFICATION.md).

## Existing project references
tests/GlobalTradeTax.Api.Tests; .github/workflows

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [x] Fix discovered C# build/runtime failures while retaining fail-closed behavior.
- [x] Execute the existing unit/mocked-contract suite and add regressions for discovered geography failures (64 xUnit tests passed).
- [ ] Add integration/contract tests where dependencies are involved.
- [ ] Update API documentation, source attribution and configuration examples.
- [x] Record test commands, outcomes and evidence in the task log.
- [ ] Review security, tenant isolation and backward compatibility.

## Definition of done
dotnet restore/build/test pass; CI required on PR; container smoke test passes.

## Evidence / decision log
- 2026-11-05 / TASK-004: extended the existing isolated container job for byte archives, streamed limits, review concurrency/expiry/history and frozen v1.2 upgrade/backfill. Local corrected run passed 154 assertions and cleanup; suites passed 344 offline xUnit/33 Python cases. Added a regression after the chunked-upload helper failed its first local run. Hosted evidence remains attached to the implementation PR; [details](../docs/TARIFF_PROVENANCE_REVIEW.md). No broader security/production completion claimed.
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

## 2026-11-02 — v3.2 evidence
- Added separate CI jobs for .NET restore/build/test/publish and offline repository integrity checks.
- Added test-result and publish-artifact upload; workflow permissions read-only and no persisted checkout credentials.
- `python3 scripts/check_project_integrity.py`: **PASS** locally (14 tasks).
- `dotnet restore`, `dotnet build`, `dotnet test`: **NOT RUN** (SDK unavailable in this environment).
- Dependency vulnerability scanning, secret scanning, integration tests, container smoke tests, branch-protection rules remain **OPEN**.
- GitHub Actions workflow has **not been run**; no claims of successful compilation.

## 2026-11-02 — v3.3 evidence
- Added dedicated `dependency-audit` CI job: NuGet restore audit with all transitive packages and warning-as-error NU1901-NU1904.
- Added JSON audit parser that fails on known vulnerabilities and malformed or empty reports; added four Python unit tests.
- `python3 -m unittest discover -s scripts/tests -v`: run locally; see release verification output.
- `dotnet` and hosted GitHub Actions execution still unverified. Secret scanning, smoke tests, branch protections and production readiness remain OPEN.
- This scan depends on NuGet advisory availability and is not a substitute for full SCA or manual security review.

## 2026-11-02 — v3.4 evidence
- Added `scripts/check_secrets.py`: fail-closed high-confidence secret checks over Git-tracked files; matched values are never logged.
- Added five scanner tests, and wired scan into repository-checks CI job.
- Local: 9 Python tests passed; task integrity check passed; secret scan passed against 106 locally staged tracked files.
- Limitations: no history scanning, entropy detection, provider verification or hosted GitHub Actions run. Dedicated scanner and branch protections still OPEN.
- .NET compilation and xUnit remain NOT RUN (SDK unavailable).

## 2026-11-02 — v3.5 evidence
- Added `git-history-secrets` CI job with full checkout history (`fetch-depth: 0`) and Gitleaks v8.24.2 container scan of all refs.
- Scanner runs read-only with `--redact` and fails CI when a finding occurs; no secrets are printed intentionally.
- Added `scripts/check_ci_workflow.py` and three offline unit tests to prevent accidental removal of essential CI controls.
- Local: 12 Python unit tests passed; task integrity and working-tree secret pattern scan passed.
- Hosted GitHub Actions, Gitleaks image pull/execution, .NET build/xUnit and full historical scan remain **NOT RUN**; no assertion of clean Git history.
- Configure branch protection to require CI jobs before merge. Review false positives and any actual credentials out of band; rotate compromised credentials rather than merely deleting files.

## 2026-11-02 — v3.6 evidence
- Added `scripts/smoke_api.py` to check running `/health`, `/v1/capabilities`, and unauthorized quote and landed-cost requests.
- Wired smoke check into CI after Release build/test, with bounded startup wait, cleanup trap and logs on failure.
- Added three offline tests for the smoke-test contract; all 15 Python tests pass locally.
- `python3 scripts/check_project_integrity.py`, `check_ci_workflow.py`, and `check_secrets.py` passed locally.
- **NOT RUN:** actual HTTP smoke test, .NET compilation/xUnit, hosted GitHub Actions, Gitleaks history scan.
- Next: run hosted CI, review build/test failures, and enforce required checks on pull requests.

## 2026-11-02 — v3.7 smoke authorization coverage
- Expanded local HTTP smoke coverage to seven protected endpoints, including UK commodity, assessment, evidence package and VAT review.
- Anonymous requests must receive HTTP 401 before any upstream tariff access; checks use valid route parameters.
- Restricted smoke-test target to local loopback HTTP to avoid inadvertently sending probes to remote services.
- Added regression tests for every protected route and invalid remote target.
- Offline Python tests passed (16). Live ASP.NET smoke, xUnit, GitHub Actions and Gitleaks remain NOT RUN.

## 2026-11-02 — v3.8 evidence
- Hardened local smoke HTTP client against 30x redirects, including off-loopback redirects.
- Required explicit loopback port and no URL prefix; added two regression tests with local HTTP server.
- Hosted CI and .NET build/test still NOT RUN.

## 2026-11-02 — Windows local execution
- Environment: Windows x64; .NET SDK 8.0.308; ASP.NET Core/.NET runtime 8.0.33; Python 3.12.14; actionlint 1.7.12.
- Initial Git state: unborn `main`, zero tracked files and no commits; all 115 original project files backed up before edits. No commits, staging, resets or branch changes performed.
- Fixed CS8803 in `Program.cs` by placing the partial Program declaration after top-level local functions; fixed CS1061 in `UkMeasureEvidence` by treating projected geography IDs as strings.
- Fixed the failing `MissingGroupMembershipFailsClosed` test: missing/non-object membership evidence now returns unresolved checks instead of throwing on an undefined JsonElement.
- Added two xUnit geography cases, two Python inventory tests and five additional NuGet parser tests. The parser accepts the real clean SDK report (which omits frameworks), while rejecting incomplete audit metadata and diagnostic-bearing reports.
- Added `global.json` to select stable .NET 8 instead of the machine's default .NET 11 preview. CI audits the API and test projects separately with JSON output version 1.
- Added explicit `check_secrets.py --include-untracked` for local scans without changing the Git index; default CI scanning remains tracked-only.
- **PASS:** restore; Release build with warnings as errors (0 warnings/errors); xUnit **64/64**; publish; source-build HTTP smoke **9/9** and published-artifact HTTP smoke **9/9**; Python **25/25**; integrity check **14 tasks**; static workflow guard; actionlint; NuGet direct/transitive audit of both projects (no reported vulnerabilities).
- **PASS:** limited high-confidence secret pattern scan across tracked and non-ignored untracked files. This is not a Git-history or comprehensive secret scan.
- **NOT RUN / OPEN:** hosted GitHub Actions and required PR check enforcement; Docker/container smoke (Docker CLI unavailable); Gitleaks history scan (no local commits/history and Gitleaks unavailable); PostgreSQL/live UK integration; full tenant/security review.
- Task remains **in-progress**. Detailed commands, limits and evidence: [Windows local verification](../docs/TASK010_WINDOWS_VERIFICATION.md). Earlier NOT RUN entries are retained as historical evidence.
