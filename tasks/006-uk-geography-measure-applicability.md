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
- [ ] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [ ] Implement minimal, isolated C# changes with explicit error handling and fail-closed behavior.
- [ ] Add unit tests for success, missing data, invalid input, and failure modes.
- [ ] Add integration/contract tests where dependencies are involved.
- [ ] Update API documentation, source attribution and configuration examples.
- [ ] Record test commands, outcomes and evidence in the task log.
- [ ] Review security, tenant isolation and backward compatibility.

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
