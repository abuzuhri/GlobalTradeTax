# TASK-007: UK duty calculation engine

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-05

## Objective
Select legally applicable measures; implement ad valorem, specific and compound expressions, units and quota uncertainty.

## Existing project references
src/GlobalTradeTax.Api/LandedCost.cs

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [x] Implement isolated illustrative arithmetic evaluator with explicit error handling and fail-closed behavior (not legally applicable duty).
- [x] Add unit tests for supported arithmetic, missing data, invalid input and unsupported expressions (executed 2026-11-05).
- [x] Add recorded/public live contracts and real authenticated container/database checks (executed 2026-11-05; managed live remains open).
- [x] Update API documentation, source attribution and configuration examples.
- [x] Record test commands, outcomes and evidence in the task log.
- [x] Review UK endpoint bounds, authentication, tenant audit isolation and evidence-v3 compatibility.

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

## 2026-11-05 — Full UK engineering completion pass
- Requested scope: finish the remaining UK engineering across GB and XI. Implement territory-bound reference access/evidence, structured measure/components/conditions and document/quota/preference review, bounded duty arithmetic, line/shipment VAT cost reconciliation and UK compliance gates. Preserve null payable results whenever authority, eligibility or data is unverified. Add recorded official contracts, arithmetic regressions, authenticated integration checks and a final per-criterion readiness report. Managed-live credentials and independent rule/rights approval are external prerequisites, not assumed complete.

## 2026-11-05 — Expanded UK verification evidence
- Typed 01/04 component arithmetic covers percentage and GBP/EUR specific/additive expressions using explicit KGM/DTN/TNE/NAR units. Recorded reference examples reproduce GB 10%, GB 12% + GBP 253/100 kg and XI 12.8% + EUR 303.4/100 kg with the dated XI conversion. Meursing/unknown operations, missing quantities, ambiguous FX, overflow or incomplete rules stay null. These are reference arithmetic regressions, not independently approved official-calculator golden cases.
- PASS locally: warnings-as-errors Release test build; 378 offline xUnit cases, 16 optional live skips; separate ten-case public live run; 32 Python tests; 112 PostgreSQL/container assertions with cleanup=true; repository/task/CI guards and whitespace check. Hosted verification is recorded in the review PR after execution.
- [Current API, compatibility, source attribution and per-criterion completion gates](../docs/UK_SHIPMENT_REVIEW.md). Status remains in-progress where those acceptance criteria are unmet; no deployment or collection is enabled.
