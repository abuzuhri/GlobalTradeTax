# TASK-003: PostgreSQL schema & migrations

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-04

## Objective
Create repeatable migrations, constraints, backup/restore and index review.

## Existing project references
db/001_init.sql; db/004_tariff_staging.sql

## Detailed checklist
- [x] Inspect existing code and identify gaps against objective.
- [ ] Confirm authoritative specifications, data rights, and versioned external contracts where relevant.
- [x] Implement the isolated operator migration path with explicit error handling and fail-closed behavior.
- [x] Add migration manifest and argument guards and isolated database failure-mode tests.
- [x] Add integration/contract tests where dependencies are involved (disposable PostgreSQL 16 stack).
- [x] Update API documentation, source attribution and configuration examples (upgrade/rollback/restore runbook).
- [x] Record test commands, outcomes and evidence in the task log.
- [ ] Review security, tenant isolation and backward compatibility.

## Definition of done
Fresh and upgraded DB pass migration tests; rollback and backup procedure documented.

## Evidence / decision log
- 2026-11-07: Authorized independent migration-ledger slice based on current main: operator-invoked versioned upgrades, explicit verified-schema adoption with honest provenance, transactional ledger, drift/concurrency checks, disposable PostgreSQL tests and recovery/release guidance. Production privilege review and external tax/legal gates remain open.
- 2026-11-08: Added operator-invoked manifest/ledger, explicit schema-compared adoption, history and schema drift rejection, serialized transactional application, and backup/recovery/release guidance. Local verification: 525 offline xUnit cases passed, 16 optional live probes skipped; 37 Python cases and 334 isolated PostgreSQL/container assertions passed with cleanup. An intermediate repeat-apply run exposed ledger-index metadata in the comparison and was corrected; the final run passed. Hosted results and subsequent per-commit evidence are tracked in the PR. Production role/ownership and index review, independent operational approval and real recovery exercises remain open. [Runbook](../docs/POSTGRES_CONTAINER_VERIFICATION.md).
- 2026-11-05 / TASK-004: migration 005 adds exact upload archives and review history with digest/length/composite-FK constraints. Local 154-assertion integration run passed fresh/legacy/restored schema equality, original v1.2 staging preservation, guarded archive backfill and reapplication/rollback. [Migration and provenance evidence](../docs/TARIFF_PROVENANCE_REVIEW.md). Migration ledger/release policy and operational review remain open.
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.
- 2026-11-04: Start the authorized container/database slice tracked under TASK-010: fresh initialization, seeded v0.4 upgrade, idempotent reapplication, transactional rollback, schema comparison and isolated backup/restore. Reuse the existing SQL scripts; do not claim a versioned migration ledger or automatic downgrade support.
- 2026-11-04: Added a frozen v0.4 schema fixture checked against original commit bcec3f9. Fresh/upgraded/restored schemas are compared by columns, defaults, identities, constraints and indexes; historical and new quotes remain readable. Failed transaction DDL rolls back. Container recreation preserves quote/idempotency/revocation/staging/audit data, and restored databases accept new audit identity values.
- 2026-11-04: The final frozen-fixture local run passed 103 assertions with PostgreSQL 16.15 and successful cleanup. The initial run found a JSONB/text parameter mismatch in AuditStore; an explicit JSONB binding fixes the real HTTP 500. Local Release build, 219 xUnit tests (eight live skips), 32 Python cases and repository checks passed. Hosted verification is tracked by the new CI job and review PR. [Runbook and actual evidence](../docs/POSTGRES_CONTAINER_VERIFICATION.md).
- [Draft PR #2](https://github.com/abuzuhri/GlobalTradeTax/pull/2) contains the container/database slice and tracks hosted verification; its base is the preceding UK-authentication PR #1.
- Status remains in-progress: production least-privilege/index review and recovery exercises remain open.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?
