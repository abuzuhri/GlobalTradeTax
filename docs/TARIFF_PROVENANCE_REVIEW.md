# Tariff upload archives and operator review evidence — TASK-004

Implemented on 2026-11-05; [PR #5](https://github.com/abuzuhri/GlobalTradeTax/pull/5) records hosted checks and review. This reference workflow preserves the uploaded document and an operator's review history. It does not verify licences automatically, publish an active tariff dataset or feed `IImportRateProvider`. Duty, VAT and payable totals remain unavailable without their existing verified provider/rule prerequisites. TASK-004 remains in progress for authoritative source onboarding, rights review and a separately controlled publication process.

## Upload validation and integrity

The existing eight-column [CSV schema](../data/SCHEMA.csv) is unchanged. Both merchant validation and operator staging now consume bounded bytes instead of a `StreamReader` that could discard a BOM or replace invalid encoding.

- The limit is **1 MiB of bytes**, including chunked requests without `Content-Length`. Input must be strict UTF-8; an optional leading UTF-8 BOM, CRLF and trailing newlines are preserved. Malformed UTF-8/UTF-16 is rejected. SHA-256 covers exact uploaded bytes, not normalized text or remotely fetched source content.
- Rates use ASCII decimal notation with one to four integer digits and up to four fractional digits, within 0–1000. Values requiring PostgreSQL rounding, exponent notation and tiny values that could round to zero are rejected.
- Duplicate or overlapping inclusive date intervals are rejected for the same normalized destination/origin/HS key, even if their rates agree or the input rows are out of order. The next day after an interval is allowed. Distinct keys can share dates. Historical/future rows remain valid references; their effective status is reported separately.
- One canonical HTTPS source URL and one nonempty version per upload are required by validation as well as staging. URLs reject credentials, fragments, nonstandard HTTPS ports, malformed syntax, controls and excessive length. Version length is limited to 256 characters. These fields identify the operator's declared source; their presence does not establish authority or data rights. No supplied URL is fetched.
- Invalid files return HTTP 400 with no partial rows. Oversized bodies return HTTP 413. Byte-identical valid uploads retain the existing batch-level idempotency; byte changes such as BOM/line endings produce a distinct digest.

New migration [005_tariff_provenance.sql](../db/005_tariff_provenance.sql) adds `tariff_import_documents` and `tariff_reference_reviews`. An archive is inserted in the same transaction as its batch/rows. PostgreSQL independently checks its byte length, SHA-256 and composite relationship to the batch digest. The existing `SourceSha256` C# record field remains a legacy name for the **upload** digest, not an independently verified upstream-content hash.

The staging store reparses its owned byte copy before writing and verifies the supplied parsed result. Review creation reparses the archive and compares every persisted row and source field with that document. Divergence returns HTTP 409. No archive bytes are returned by the metadata endpoints.

## Review API

All administrative routes require the existing `X-Operator-Key`; merchant API keys cannot invoke them. The shared operator key authenticates the operator role. `reviewedBy` is an operator-supplied attribution tag, not independently authenticated personal identity.

| Route | Behavior |
|---|---|
| `POST /v1/admin/tariff-datasets/stage` | Existing CSV staging route; now archives exact bytes transactionally and can backfill a matching legacy upload. |
| `GET /v1/admin/tariff-datasets/{id}` | Existing metadata fields plus `documentArchived`, latest `review`, validity range and UTC-date counts `expiredRows`, `futureRows`, `effectiveRows`; always `approvedForCalculation=false`. |
| `POST /v1/admin/tariff-datasets/{id}/reviews` | Append a review attestation tied to the exact dataset digest and expected preceding review. Maximum JSON body 32 KiB, including streamed requests. Authentication precedes body reading/parsing. |
| `GET /v1/admin/tariff-datasets/{id}/reviews?afterRevision=0&limit=50` | Ascending, bounded history; limit 1–100, `nextAfterRevision` and `hasMore` for subsequent pages. |

Example first-review JSON (illustrative metadata only; use the real batch digest and reviewed rights evidence):

```json
{
  "expectedDatasetSha256": "<sha256 returned by staging>",
  "disposition": "reviewed_reference_only",
  "reviewedBy": "operator attribution tag",
  "licenceUrl": "https://example.test/source-licence",
  "expiresAt": "2026-12-01T00:00:00Z",
  "note": "Reference source and permissions reviewed; not a payable rate determination.",
  "previousReviewId": null
}
```

A `reviewed_reference_only` attestation requires an HTTPS licence URL and a future expiry. Expiry is stored as UTC at PostgreSQL microsecond precision; finer precision is rejected instead of rounded. A `rejected` decision requires a reviewer/note, permits a null licence URL and requires null expiry, so missing permission can itself be recorded. Notes are limited to 2,000 characters and attribution tags to 128.

Review results are `recorded` or `already_recorded`. The snapshot binds upload digest, declared source/version, disposition, attribution, licence, expiry, note and previous review ID into a versioned request fingerprint. A per-batch row lock serializes revisions. Identical normalized requests replay the same review, including after expiry or later revisions; they never move the current review backward. A changed request must supply the latest `previousReviewId`, otherwise HTTP 409 prevents a stale overwrite. Renewals and rejections append revisions; earlier records remain available through history. Creation against an unknown batch returns 404; malformed metadata or an already-expired new attestation returns 400.

Each read recomputes `evidenceStatus` from the stored decision and the current clock. At the expiry instant, `reviewed_reference_only` becomes `expired_reference_review`; the stored record is unchanged. `licenceVerification=operator_attestation_only` and `approvedForCalculation=false` remain explicit. Review expiry is separate from row validity: an unexpired attestation does not make expired/future rows effective. No version precedence or conflict resolution across separate batches is introduced.

Append-only review history is enforced by the API and database revision/link/deduplication constraints; this is not a tamper-proof external archive or a substitute for production database roles/audit controls. There is no publication/activation endpoint, and none of the new tables are read by a calculation provider.

## Upgrade and legacy batches

Fresh Compose volumes run all five scripts. For an existing database, back up and stop writers as described in the [database recovery runbook](POSTGRES_CONTAINER_VERIFICATION.md), then apply **005 after 001–004** before running the updated application. The schema now has eight tables. No existing staged records are deleted, repriced or given a fabricated archive/review.

Older batches return `documentArchived=false` and a null review. To archive one, upload the exact original bytes again. Only a matching digest, source/version, record count and every stored row permit backfill; mismatches return 409 without an archive. The stricter parser also applies. A legacy file that no longer meets the validation contract must be corrected and staged as a new, separately identified batch. Review requires an archive.

The full runner exercises v0.4 quotes and a [frozen original v1.2 staging schema](../tests/fixtures/postgres/v12-tariff-schema.sql) from initial commit `bcec3f994af5ac04cb7dc5f1fa15f41bc6452f02`. It seeds unarchived historical staging before migration 005, verifies it survives, rejects mismatching backfill, then archives/reviews the exact matching upload. Reapplication, schema equality and intentional migration rollback are also verified. This is an application-schema upgrade on PostgreSQL 16, not an engine-major upgrade or a migration ledger.

Official database references: [binary SHA-256/byte-length functions](https://www.postgresql.org/docs/16/functions-binarystring.html), [row locks](https://www.postgresql.org/docs/16/explicit-locking.html) and [check/unique/foreign-key constraints](https://www.postgresql.org/docs/16/ddl-constraints.html). No new external data source or licence has been approved by this implementation.

## Verification

- Local .NET Release build/publish passed; **343 offline xUnit cases** (51 new) passed, with ten optional live probes skipped. No new external tariff API calls are needed for this slice.
- **33 Python cases** passed, including the streamed-request regression. Task integrity, static CI guard, limited source secret scan and whitespace checks passed.
- The corrected local Docker/PostgreSQL run passed **154 assertions with cleanup=true**, including legacy upgrade/backfill and backup restoration. Its report is ignored `TestResults/postgres-provenance-integration.json`; the hosted artifact records the final checked runner separately.
- The isolated Docker/PostgreSQL runner covers exact BOM/CRLF archives, database checksum/foreign-key constraints, streamed body limits, duplicate/precision rejection, operator/merchant separation, concurrent first reviews, real-clock expiry, post-expiry replay, renewal/rejection history and competing revision preconditions. Container recreation and backup restoration retain the archive, four review revisions, quotes and merchant revocations; staged/reviewed rates still produce null monetary outputs.
- The runner uploads a redacted JSON report in the existing `container-postgres` CI job. [PR #5](https://github.com/abuzuhri/GlobalTradeTax/pull/5) records final hosted outcomes and exact assertion counts. The initial local run found a chunked-request generator bug in the test helper; it cleaned up its private project after 55 assertions. The helper and a targeted regression test were corrected before the final checks.

Reproduce with the existing [Docker/WSL commands](POSTGRES_CONTAINER_VERIFICATION.md#repeatable-test), or run locally:

```powershell
dotnet build tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj -c Release -warnaserror
dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj --no-build -c Release
python -m unittest discover -s scripts/tests -v
python scripts/check_project_integrity.py
python scripts/check_ci_workflow.py
python scripts/check_secrets.py --include-untracked
```

The test uses generated keys, synthetic data and a private labelled Compose project; no existing merchant database or Saleor sandbox is selected. Cleanup removes only owned resources. Production source agreements, independent rights/rule review, controlled publication and database least-privilege policy remain open work.
