# TASK-006: UK geography & measure applicability

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-05

## Objective
Resolve memberships, exclusions, effective dates, origin, measure conditions, certificates, quotas and preferences.

## Existing project references
src/GlobalTradeTax.Api/UkAreaMembership.cs; src/GlobalTradeTax.Api/UkImportAssessment.cs; src/GlobalTradeTax.Api/UkMeasureApplicability.cs

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [x] Implement isolated UK review changes with explicit error handling and fail-closed behavior.
- [x] Add unit tests for success, missing data, invalid input and failure modes (executed 2026-11-05).
- [x] Add recorded/public live contracts and real authenticated container/database checks (executed 2026-11-05; managed live remains open).
- [x] Update API documentation, source attribution and configuration examples.
- [x] Record test commands, outcomes and evidence in the task log.
- [x] Review UK endpoint bounds, authentication, tenant audit isolation and evidence-v3 compatibility.

## Definition of done
Golden tests for country groups, exclusions, overlapping measures; unknown stays indeterminate.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.
- 2026-11-05: Started the next isolated slice: date-scoped upstream requests/cache, the recorded `children_geographical_areas` contract, import-measure linkage, effective windows and explicit exclusion evidence. Unknown or incomplete evidence stays indeterminate; overlapping measures remain separate. Conditions, preferences, quotas, legal review and payable calculation remain open. Implementation and verification results will be recorded below.
- 2026-11-05: Implemented the reference-evidence slice and evidence schema v2. Local PASS: zero-warning Release build/publish, 292 offline xUnit cases (73 new; ten live probes skipped), six separately enabled public live probes, 35 dated HTTP assertions plus ten smoke assertions and 25 Python cases. Four dated response fixtures retain source/date/hash/OGL attribution. [Contracts, safeguards, compatibility, reproduction and outstanding criteria](../docs/UK_APPLICABILITY_EVIDENCE.md); [PR #4](https://github.com/abuzuhri/GlobalTradeTax/pull/4) records hosted checks and review. The parent task remains in progress.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

### 2026-11-02 — v2.5 progress
- [x] Deterministic bounded area lookup plan with explicit omitted IDs.
- [x] Tests for exactly-at-limit, overflow, and invalid/duplicate identifiers (written, not executed).
- [ ] Verify live membership schema, exclusions and legally applicable measures.
- [ ] Run .NET CI and review results.

### 2026-11-05 — Verified reference slice
- [x] Confirm date-scoped tree membership and exclusion serializers against pinned official source and recorded public responses.
- [x] Keep dated cache entries separate and reject unsupported future dates without upstream work.
- [x] Preserve typed membership paths, direct/group exclusions, effective boundaries and overlapping import/export measures as reference evidence.
- [x] Test missing/invalid contracts, partial trees, cycles, budgets and unchanged payment gates.
- [x] Document additive API fields, stricter membership behavior, canonical evidence v2 and actual local results.
- [ ] Complete certificates, legal measure conditions, preference entitlement, quota eligibility/balance and independently reviewed applicability before claiming TASK-006 done.

## 2026-11-05 — Full UK engineering completion pass
- Requested scope: finish the remaining UK engineering across GB and XI. Implement territory-bound reference access/evidence, structured measure/components/conditions and document/quota/preference review, bounded duty arithmetic, line/shipment VAT cost reconciliation and UK compliance gates. Preserve null payable results whenever authority, eligibility or data is unverified. Add recorded official contracts, arithmetic regressions, authenticated integration checks and a final per-criterion readiness report. Managed-live credentials and independent rule/rights approval are external prerequisites, not assumed complete.

## 2026-11-05 — Expanded UK verification evidence
- Structured source rules now retain component codes/units, VAT/excise flags, certificate/action codes, condition alternatives, quota/additional/preference references, legal acts and footnotes. Missing/duplicate/malformed resources stay incomplete. GB membership cannot establish XI applicability. Canonical evidence v3 binds territory, source hashes and structured rules. Legal selection, authentic documents, preference entitlement and quota eligibility/balance remain open.
- PASS locally: warnings-as-errors Release test build; 380 offline xUnit cases, 16 optional live skips; separate ten-case public live run; 32 Python tests; 112 PostgreSQL/container assertions with cleanup=true; repository/task/CI guards and whitespace check. Hosted verification is recorded in the review PR after execution.
- [Current API, compatibility, source attribution and per-criterion completion gates](../docs/UK_SHIPMENT_REVIEW.md). Status remains in-progress where those acceptance criteria are unmet; no deployment or collection is enabled.
