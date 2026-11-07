# TASK-009: Compliance & restricted goods

- **Status:** in-progress
- **Priority:** P1
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-07

## 2026-11-07 — Enforce declared control holds
- Carry explicit licence/ITAR holds through quote creation and landed-cost estimation, without treating a provider's verified financial response as clearance. A declared hold must suppress monetary completion and avoid requesting a provider quote, regardless of shipping country or currency.
- Expose stable hold reasons and an unconditional false shipment-release approval in compliance results. Preserve existing UK manual review requirements; absent flags do not establish that goods are uncontrolled.
- Authenticate before bounded exact JSON parsing on the three quote/compliance routes, rejecting duplicate control flags and invalid nested/null/overflow inputs before persistence or provider access. Add unit and real authenticated database/API regressions, document compatibility and keep the finished PR ready for review. Actual licensing, sanctions checks and specialist clearance remain external.

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
- 2026-11-07: Implementation commit `8dc1bf6` passed all six regular hosted CI jobs, with 514 xUnit passes/16 optional skips, 33 Python passes, 11 HTTP smoke assertions and 297 database/container assertions with cleanup=true. Actual HTTP checks verify duplicate-flag rejection, consistent holds, audit persistence and durable replay after recreation/restoration. [Hosted run](https://github.com/abuzuhri/GlobalTradeTax/actions/runs/42355867265); [ready PR #9](https://github.com/abuzuhri/GlobalTradeTax/pull/9) tracks per-commit verification.
- 2026-11-07: Reproduced eight quote/provider hold failures, then enforced declared licence/ITAR holds before provider access and quote completion. Added structured hold/release evidence, durable quote/idempotency coverage and exact authenticated request parsing. Warnings-as-errors Release tests pass 514 offline cases/16 optional tariff API skips; 33 Python cases and repository guards pass. [Contract, official sources, compatibility and final hosted evidence](../docs/COMPLIANCE_HOLDS.md). No licence is authenticated or shipment released; task remains in progress.
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
