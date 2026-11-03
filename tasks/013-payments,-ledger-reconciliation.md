# TASK-013: Payments, ledger & reconciliation

- **Status:** not-started
- **Priority:** P2
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-02

## Objective
Design partner-led duty collection, settlement, refunds, ledger, invoice matching and licensing review.

## Existing project references
docs/ROADMAP.md

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
Partner and counsel signoff; double-entry ledger and reconciliation tests; no funds custody before approval.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?
