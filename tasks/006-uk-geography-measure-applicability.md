# TASK-006: UK geography & measure applicability

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-02

## Objective
Resolve memberships, exclusions, effective dates, origin, measure conditions, certificates, quotas and preferences.

## Existing project references
src/GlobalTradeTax.Api/UkAreaMembership.cs; src/GlobalTradeTax.Api/UkImportAssessment.cs

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

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

### 2026-11-02 — v2.5 progress
- [x] Deterministic bounded area lookup plan with explicit omitted IDs.
- [x] Tests for exactly-at-limit, overflow, and invalid/duplicate identifiers (written, not executed).
- [ ] Verify live membership schema, exclusions and legally applicable measures.
- [ ] Run .NET CI and review results.
