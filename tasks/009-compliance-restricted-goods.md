# TASK-009: Compliance & restricted goods

- **Status:** in-progress
- **Priority:** P1
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-05

## Objective
Review US export EEI/AES and UK import licensing, prohibitions, certificates, importer responsibility.

## Existing project references
src/GlobalTradeTax.Api/ShipmentCompliance.cs

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
Manual review gates for controlled goods; no legal clearance asserted by heuristic.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?

## 2026-11-05 — Full UK engineering completion pass
- Requested scope: finish the remaining UK engineering across GB and XI. Implement territory-bound reference access/evidence, structured measure/components/conditions and document/quota/preference review, bounded duty arithmetic, line/shipment VAT cost reconciliation and UK compliance gates. Preserve null payable results whenever authority, eligibility or data is unverified. Add recorded official contracts, arithmetic regressions, authenticated integration checks and a final per-criterion readiness report. Managed-live credentials and independent rule/rights approval are external prerequisites, not assumed complete.

## 2026-11-05 — Expanded UK verification evidence
- UK review requires GB/XI-specific classification, EORI, procedure, valuation, VAT collection, certificate alternatives, sanctions/product controls, preference/quota and importer responsibilities. NI risk/UKIMS remains separately unresolved. Export-control flags now require manual review regardless of shipping country. No declared certificate or heuristic clears goods. Broader US export compliance and actual legal validation remain outside this UK pass.
- PASS locally: warnings-as-errors Release test build; 380 offline xUnit cases, 16 optional live skips; separate ten-case public live run; 32 Python tests; 112 PostgreSQL/container assertions with cleanup=true; repository/task/CI guards and whitespace check. Hosted verification is recorded in the review PR after execution.
- [Current API, compatibility, source attribution and per-criterion completion gates](../docs/UK_SHIPMENT_REVIEW.md). Status remains in-progress where those acceptance criteria are unmet; no deployment or collection is enabled.
