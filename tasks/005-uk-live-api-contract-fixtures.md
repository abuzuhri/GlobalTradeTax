# TASK-005: UK live API contract & fixtures

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-02

## Objective
Verify official UK endpoints, API version, JSON:API relationships, pagination, status codes and real fixtures.

## Existing project references
src/GlobalTradeTax.Api/UkTariffReferenceClient.cs; tests/GlobalTradeTax.Api.Tests

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [x] Implement minimal, isolated C# changes with explicit error handling and fail-closed behavior.
- [x] Add unit tests for success, missing data, invalid input, and failure modes (written; execution pending).
- [ ] Add integration/contract tests where dependencies are involved.
- [ ] Update API documentation, source attribution and configuration examples.
- [ ] Record test commands, outcomes and evidence in the task log.
- [ ] Review security, tenant isolation and backward compatibility.

## Definition of done
Recorded permitted fixtures; live smoke test; version drift alarm; documented response schema.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

- 2026-11-02: Added structural JSON:API contract guard and negative tests; no live UK API call or .NET execution verified. CI workflow added, not executed.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?
