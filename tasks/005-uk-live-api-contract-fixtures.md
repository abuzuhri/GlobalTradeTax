# TASK-005: UK live API contract & fixtures

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-05

## Objective
Verify official UK endpoints, API version, JSON:API relationships, pagination, status codes and real fixtures.

## Existing project references
src/GlobalTradeTax.Api/UkTariffReferenceClient.cs; tests/GlobalTradeTax.Api.Tests

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [x] Implement minimal, isolated C# changes with explicit error handling and fail-closed behavior.
- [x] Add unit tests for success, missing data, invalid input, and failure modes (executed 2026-11-04).
- [x] Add integration/contract tests where dependencies are involved (recorded public fixtures plus four opt-in live probes).
- [x] Update API documentation, source attribution and configuration examples.
- [x] Record test commands, outcomes and evidence in the task log.
- [x] Review security, tenant isolation and backward compatibility for this slice (existing authentication/allowlist retained; authenticated and anonymous routes verified).

## Definition of done
Recorded permitted fixtures; live smoke test; version drift alarm; documented response schema.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

- 2026-11-02: Added structural JSON:API contract guard and negative tests; no live UK API call or .NET execution verified. CI workflow added, not executed.

## 2026-11-04 — Current implementation slice
- Verify the configured read-only UK v2 commodity and geographical-area endpoints against the official serializers and live responses; record public fixtures with source URLs, retrieval times, hashes and licence attribution.
- Correct resource identity validation: a commodity resource uses an internal `data.id` and exposes its requested commodity code in `attributes.goods_nomenclature_item_id`; resource types are singular.
- Add fixture-backed offline regression tests and an explicitly opt-in live contract check in CI. Cover wrong identity/type, malformed documents, missing resources and conservative downstream projections.
- Document the legacy endpoint observed today separately from the newly documented managed-credentials API. Do not broaden the runtime allowlist, add credentials, infer dated membership or enable payable customs amounts.
- Status remains in-progress: this slice does not cover all managed-access, endpoint-coverage or operational-monitoring requirements.

## 2026-11-04 — Verification evidence
- Live official responses exposed the internal-ID/singular-type mismatch; corrected commodity identity validation and explicitly request the documented v2 media type. Invalid identity/schema responses still carry no data and invalid areas are not cached.
- Recorded four exact public responses (two HTTP 200, two HTTP 404) with hashes, timestamps and OGL v3 attribution. The official source serializers agree with the observed identities; their reviewed source revision is recorded, not claimed to be the deployed version.
- PASS: Release build with zero warnings/errors; 167 offline xUnit cases with four live cases explicitly skipped; separate opt-in run passed all four live cases; 25 Python tests; task integrity, static CI guard and limited secret-pattern scan.
- PASS: isolated running API, 10 general smoke assertions and six authenticated live-data route checks. Membership remains unknown without dated proof; failed area lookups retain null assessment duty/VAT.
- PASS: actionlint 1.7.12 workflow validation (optional ShellCheck/Pyflakes disabled) and `git diff --check`.
- Added a manually enabled CI live-contract job with a failing status and uploaded TRX on drift/outage; no scheduled monitoring or hosted run was performed.
- OPEN: the current docs require managed credentials on `api.trade-tariff.service.gov.uk`; this adapter still uses the observed legacy allowlist. Current service terms returned 403 during review. Credential migration/access review, broader endpoint/pagination coverage and operational drift monitoring remain open. Status stays in-progress.
- [Detailed contract, sources, commands and actual results](../docs/UK_LIVE_CONTRACT_VERIFICATION.md).

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

## 2026-11-04 — Managed API implementation slice
- Add opt-in support for the documented managed GB prefix `https://api.trade-tariff.service.gov.uk/uk/api/`, retaining the existing public v2 setup. Only the fixed official OAuth endpoint may receive operator-provided client credentials.
- Implement bounded client-credentials token acquisition, shared in-memory expiry/renewal, single-flight refresh and one retry after HTTP 401. Never fall back to public access after a managed authentication failure; never send managed credentials/tokens to the public host.
- Cover credential absence, unsafe URLs, malformed token responses, expiry, concurrent refresh, rotation, retry limits and output secrecy with offline tests. Add explicitly gated managed live probes without committing credentials or live token responses.
- Update configuration examples and the runbook; verify local build/test/runtime behavior. Real managed access requires operator-provisioned credentials, which are not present in the current environment. Inspect hosted CI separately and report its actual state.

## 2026-11-04 — Managed implementation evidence
- Implemented the managed GB prefix and fixed official OAuth endpoint, encoded client-credentials form, memory-only shared tokens, expiry validation/early renewal, concurrent refresh control, failure cooldown and at most one 401 retry. Public mode neither requests nor sends OAuth tokens; managed failures do not fall back.
- PASS: zero-warning Release build, 219 offline xUnit cases (52 new managed cases), eight live probes skipped by default, separate four-case public live run, 25 Python tests and repository checks. Both running API configurations passed general authorization smoke; public reference checks passed and missing managed credentials returned HTTP 503.
- Added a separately gated four-case managed live job and configuration/runbook. Actual credentials are absent in local configuration and Actions secrets, so live managed access remains unverified.
- Inspected historical hosted run 41683857783: four jobs passed, history scan failed on two documentation placeholders. TASK-010 tracks the exact-fingerprint repair and new hosted verification.
- [Managed configuration, contract and actual local evidence](../docs/UK_MANAGED_API.md). Status remains in-progress.
- Hosted verification for source commit 2673050 passed: [PR workflow](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879751626) (219 xUnit passes, eight live skips, build/publish/audits/history scan) and [manual public-live workflow](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/41879785451) (four live passes). Managed live probes remain unrun without credentials. Changes are in [draft PR #1](https://github.com/abuzuhri/GlobalTradeTax/pull/1).

## 2026-11-05 — Full UK engineering completion pass
- Requested scope: finish the remaining UK engineering across GB and XI. Implement territory-bound reference access/evidence, structured measure/components/conditions and document/quota/preference review, bounded duty arithmetic, line/shipment VAT cost reconciliation and UK compliance gates. Preserve null payable results whenever authority, eligibility or data is unverified. Add recorded official contracts, arithmetic regressions, authenticated integration checks and a final per-criterion readiness report. Managed-live credentials and independent rule/rights approval are external prerequisites, not assumed complete.

## 2026-11-05 — Expanded UK verification evidence
- Separate GB/XI public and managed prefixes, territory-bound caches/evidence, exact response hashes and both documented FX contracts are implemented. Seven attributed exact responses and ten opt-in public probes cover the expanded contracts. A weekly public drift workflow is included and starts after merge; six managed probes remain unrun without credentials.
- PASS locally: warnings-as-errors Release test build; 378 offline xUnit cases, 16 optional live skips; separate ten-case public live run; 32 Python tests; 112 PostgreSQL/container assertions with cleanup=true; repository/task/CI guards and whitespace check. Hosted verification is recorded in the review PR after execution.
- [Current API, compatibility, source attribution and per-criterion completion gates](../docs/UK_SHIPMENT_REVIEW.md). Status remains in-progress where those acceptance criteria are unmet; no deployment or collection is enabled.
