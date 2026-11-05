# TASK-004: Tariff ingestion & provenance

- **Status:** in-progress
- **Priority:** P0
- **Owner:** Unassigned
- **Dependencies:** Review related tasks in [master register](../TASKS.md) before starting.
- **Last reviewed:** 2026-11-05

## Objective
Import licensed effective-dated tariff records; source URLs, hashes, review and publication workflow.

## Existing project references
src/GlobalTradeTax.Api/TariffDataset.cs; src/GlobalTradeTax.Api/TariffStagingStore.cs

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
No staged records used as payable rates; duplicates rejected; provenance and expiry verified.

## Evidence / decision log
- 2026-11-02: Task created. Existing implementation may be partial; completion has not been established.
- 2026-11-05: Started reference provenance and review slice: bounded strict UTF-8 uploads with exact-byte archives/hashes, duplicate/overlapping interval and precision checks, immutable operator review evidence with explicit licence metadata/expiry, and fresh/legacy/restore verification. Review is an operator attestation, not automatic licence verification or publication to payable providers. Historical unarchived batches remain visible and require matching document/row verification before review. Implementation and test evidence will follow below.
- 2026-11-05: Implemented byte archives, append-only predecessor-bound reviews, explicit review expiry/history and migration 005. Local PASS: 344 offline xUnit cases (52 new; ten live skips), 33 Python cases, warnings-as-errors Release build/publish and 154 PostgreSQL/container assertions with cleanup. Frozen original v1.2 staging upgrades without invented provenance; mismatched backfill is rejected and matching bytes/rows can be archived/reviewed. [Contract, migration, limitations and local evidence](../docs/TARIFF_PROVENANCE_REVIEW.md); [PR #5](https://github.com/abuzuhri/GlobalTradeTax/pull/5) retains hosted verification/review.

### Verified reference workflow slice
- [x] Preserve exact UTF-8 bytes and enforce bounded uploads and database archive checksums.
- [x] Reject within-batch duplicates/overlaps, unsupported precision and inconsistent source metadata without partial writes.
- [x] Record operator-attested licence/expiry and append-only renewals/rejections with concurrent revision preconditions.
- [x] Keep staged/reviewed data disconnected from payable rate providers and expose expiry/effective-row evidence explicitly.
- [x] Verify fresh/legacy/restore behavior and record local results; document migration and API compatibility.
- [ ] Onboard authoritative licensed providers and independently review permission/effective-date evidence.
- [ ] Implement separately controlled publication and cross-batch version selection before treating a reviewed reference as an authoritative rule.

## Risks and open questions
- Who will own and review this task?
- What official source or provider contract is required?
- What evidence proves correctness in staging?
