# TASK-002: Tenant security & access control

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-07

## Objective
Review tenant isolation, API key lifecycle, admin authorization, secrets and rate limits.

## Existing project references
src/GlobalTradeTax.Api/TenantAuth.cs; db/002_tenants.sql

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
Negative cross-tenant tests; rotated keys rejected; admin operations audited; threat model approved.

## Evidence / decision log
- 2026-11-07: [Hosted run 42355867265](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42355867265) passed all six regular jobs for `8dc1bf6`, including 297 authenticated database/API assertions with cleanup, 514 xUnit passes/16 optional skips and 33 Python passes. [Ready PR #9](https://github.com/abuzuhri/GlobalTradeTax/pull/9) records final-head status; the new checks prove request rejection before writes and persisted hold/idempotency evidence after restore.
- 2026-11-07 / TASK-009: Authenticate before 64 KiB exact JSON parsing on quote, landed-cost and compliance routes. Reject duplicate control flags, null nested inputs, bounded-field violations and arithmetic overflow before persistence/provider access. Preserve tenant-bound idempotency and audit declared holds. Release testing passes 514 offline cases/16 optional skips, 33 Python cases and repository guards; real database/API regressions run in CI. [Contract and remaining security/compliance work](../docs/COMPLIANCE_HOLDS.md). This does not complete rate limiting, production authorization or threat-model approval.
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?
