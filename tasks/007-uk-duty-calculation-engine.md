# TASK-007: UK duty calculation engine

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-02

## Objective
Select legally applicable measures; implement ad valorem, specific and compound expressions, units and quota uncertainty.

## Existing project references
src/GlobalTradeTax.Api/LandedCost.cs

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [x] Implement isolated illustrative arithmetic evaluator with explicit error handling and fail-closed behavior (not legally applicable duty).
- [x] Add unit tests for supported arithmetic, missing data, invalid input and unsupported expressions (execution pending).
- [ ] Add integration/contract tests where dependencies are involved.
- [ ] Update API documentation, source attribution and configuration examples.
- [ ] Record test commands, outcomes and evidence in the task log.
- [ ] Review security, tenant isolation and backward compatibility.

## Definition of done
Duty null if evidence insufficient; approved golden cases match official calculator; no silent zero.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

- 2026-11-02: Added UkDutyExpressionEngine with limited GBP percentage/specific/compound mathematical evaluation. Output explicitly illustrative; not connected to payable quote. Build and tests not run in this environment.

- 2026-11-02 v2.7: Fixed specific-duty input requirements: weight/count expressions no longer unnecessarily require customs value. Added regression tests; execution pending .NET SDK.
