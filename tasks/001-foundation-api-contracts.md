# TASK-001: Foundation & API contracts

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-02

## Objective
Define stable quote and assessment contracts; validate ISO codes, origin, money and dates.

## Existing project references
src/GlobalTradeTax.Api/QuoteService.cs; src/GlobalTradeTax.Api/Program.cs

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
Contract tests for errors, null monetary fields, and idempotency; OpenAPI documented.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

**2026-11-09 destination Region contract correction:** Restore the advertised EU/GCC/LATAM/OTHER grouping for recently added country guidance, retaining country-specific references and unresolved eligibility checks. Verify quote output and capability metadata; no legal/source review, rates, provider or migration is in scope. TASK-001 and TASK-011 remain in progress.

2026-11-09: Region compatibility correction: MY/ID/TH/VN/PH, IN/CN and AU/NZ now use OTHER; EU, GCC and LATAM routing, including MX North America references, remain intact. Country sources and unresolved checks remain. Release tests: 662 passed, 16 optional live skipped; Python: 39 passed; repository, secret and CI guards passed; local API smoke: 18 checks passed. Docker unavailable locally; hosted PostgreSQL/container verification is required on the PR head. Source dates were not re-reviewed; live data rights and independent legal approvals remain open. TASK-001 and TASK-011 remain in progress.

Hosted verification of implementation commit `d27df46f6e42061fa23c297ce6c9d9f28d66c782`: [run 42683768925](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42683768925) passed all six regular jobs, including 467 isolated PostgreSQL/container assertions and cleanup. Optional live jobs were skipped. Subsequent documentation-only changes require their own head checks.
