# PostgreSQL/container verification — TASK-003 and TASK-010

Implemented on 2026-11-04. The integration runner builds the repository Dockerfile, starts PostgreSQL 16 from the root Compose configuration and exercises the API using generated test merchants. It verifies application-schema upgrades, durable data and authenticated API isolation. Financial customs fields remain unavailable.

**2026-11-05 extension:** [tariff provenance/review verification](TARIFF_PROVENANCE_REVIEW.md) adds migration 005, exact-byte archives, append-only review revisions/expiry and frozen v1.2 staging upgrade coverage. The current runner checks eight tables and five scripts; the dated 103-assertion baseline below describes the previous slice.

## Discovered runtime defect

The first real database run reached 53 assertions, then `POST /v1/landed-cost/estimate` returned HTTP 500. PostgreSQL reported SQLSTATE 42804: `audit_events.details_json` is JSONB, while `AuditStore` sent its serialized value as a text parameter. Previously, the local HTTP smoke checks did not use PostgreSQL and could not expose this failure.

`AuditStore.WriteAsync` now explicitly binds the serialized details as `NpgsqlDbType.Jsonb`. The integration test verifies a successful HTTP response, a JSON object stored for the authenticated merchant and audit writes after backup restoration. Calculation and payment behavior are unchanged.

## Repeatable test

Requirements: Docker Engine, Docker Compose v2, and Python 3's standard library. No local PostgreSQL server, host database port or Python database package is required.

On Linux, from the repository:

```bash
python3 scripts/check_postgres_integration.py --report TestResults/postgres-integration.json
```

On this Windows checkout, use the existing WSL Docker installation:

```powershell
wsl -d Ubuntu -u root --exec python3 /mnt/d/Project/Source/Repos/abuzuhri/GlobalTradeTax/scripts/check_postgres_integration.py --report /mnt/d/Project/Source/Repos/abuzuhri/GlobalTradeTax/TestResults/postgres-integration.json
```

The runner reads the real root Compose configuration with an empty environment file, assigns a random `gtt-pgtest-*` project, adds ownership labels and replaces the API port with a dynamic loopback port. The application image retains the real Dockerfile/build context. It adds a second database initialized from [the frozen v0.4 fixture](../tests/fixtures/postgres/v04-schema.sql), copied from `db/001_init.sql` at commit `bcec3f994af5ac04cb7dc5f1fa15f41bc6452f02`. The fixture deliberately stays separate from current migration files.

Generated database passwords and merchant/operator keys are supplied through the child environment. Neither key values nor verifiers are printed; failure diagnostics are redacted. Backups stay in process memory. `TestResults/` is ignored by Git and excluded from the Docker context.

Startup rejects external/shared resource names, explicit container names and writable host bind mounts. Before teardown, the runner verifies that the containers, volumes and networks carry this run's project and ownership labels. It removes only this generated project and its labelled image, then verifies that its resources are gone. Guard tests check these refusal paths and diagnostic redaction.

## Coverage

| Area | Actual checks |
|---|---|
| Fresh initialization | All eight tables, column/default/identity metadata, constraints and indexes from the five current SQL files. |
| Authentication | Hashed PostgreSQL merchant keys; development fallback rejected while a database is configured; disabled/revoked keys and disabled merchants rejected. |
| Tenant isolation | Merchants can reuse the same idempotency key independently; each merchant receives HTTP 404 for another merchant's quote. |
| Concurrent idempotency | Eight simultaneous identical requests return one quote and create one stored row; changed payload with an existing key returns HTTP 409. |
| Tariff staging | Four concurrent operator requests deduplicate to one batch with two complete rows; provenance fingerprint preserved; merchant credentials cannot invoke operator routes; invalid CSV adds no batch. |
| Provenance/reviews | Exact BOM/CRLF archive/digest, streamed limits, duplicate/precision rejection, review expiry/replay, concurrent revision preconditions and bounded retained history. |
| Non-payable behavior | Staged test rates do not populate duty, tax or landed total; landed-cost estimates stay incomplete while their audit events persist. |
| Container lifecycle | API and PostgreSQL containers are actually recreated, with changed container IDs; quotes, idempotency, revocations, staging and audits persist. |
| Backup/restore | Custom-format dump restored into a new database; schema/data/authentication verified through a recreated API; new quote and audit writes prove the restored identity sequence works. |
| Legacy upgrade | Historical quote plus frozen v1.2 tariff staging seeded before migration 005; upgraded schema matches fresh initialization; all five scripts reapply without data loss; matching archive backfill/review and new quote writes succeed. |
| Transaction failure | Intentional SQLSTATE 22012 aborts a migration transaction; its test table is rolled back and the prior schema/data remain readable. |
| Constraints/runtime | Invalid tenant foreign key produces SQLSTATE 23503; application container runs as a non-root user; anonymous API smoke assertions pass. |

Both the old application fixture and the current schema run on PostgreSQL 16.15 in the local verification. No PostgreSQL engine-major upgrade is implied.

## Local evidence

- Initial run: **53 assertions passed before the real audit-write failure; cleanup succeeded**.
- Corrected full run: **103 assertions passed; cleanup succeeded**. PostgreSQL reported **16.15** using the root `postgres:16-alpine` image.
- Frozen legacy fixture was compared to the original committed SQL and matched, excluding its provenance comments.
- Release build with warnings as errors: **zero warnings/errors**; xUnit: **219 passed, eight live probes intentionally skipped**.
- Python suite: **32 passed**, including seven new isolation/cleanup/redaction guard cases.
- Repository integrity, static workflow guard, limited tracked/untracked secret checks and whitespace checks passed.

The six existing Saleor sandbox containers remained running alongside the isolated test project. Final generated-project cleanup is checked by the runner itself. Reports contain synthetic test data only; no external tariff API is contacted.

## CI

The `container-postgres` job runs the same script on every normal workflow run and uploads `postgres-integration-results` even when a check fails. The JSON records passed assertions, server version, timing, cleanup outcome and redacted failure diagnostics. A failure in building, initialization, HTTP behavior, upgrade, restore or cleanup fails the job.

Hosted execution and its uploaded evidence are tracked in [draft PR #2](https://github.com/abuzuhri/GlobalTradeTax/pull/2/checks), which builds on PR #1. The PR records the final hosted outcome separately from the local results above.

## Database upgrade and recovery procedure

The official PostgreSQL container executes initialization scripts only for a new data directory; existing volumes need an explicit upgrade. The sorted initialization behavior and persistence path for PostgreSQL 16 are documented by the [official image](https://hub.docker.com/_/postgres). Recreating an existing volume is unnecessary for applying these scripts.

The following commands are for an operator-managed deployment, inside a Linux/WSL shell with the existing Compose configuration and required password environment already supplied. Stop application writers for a controlled upgrade and choose a protected backup location outside the repository.

```bash
set -euo pipefail
backup_file=/secure-backups/trade-before-upgrade.dump
docker compose exec -T postgres pg_dump -U trade -d trade -Fc --no-owner --no-acl > "$backup_file"
docker compose exec -T postgres pg_restore --list < "$backup_file"

cat db/001_init.sql db/002_tenants.sql db/003_audit.sql db/004_tariff_staging.sql db/005_tariff_provenance.sql |
  docker compose exec -T postgres psql -X -U trade -d trade \
    -v ON_ERROR_STOP=1 --single-transaction -f -
```

`ON_ERROR_STOP` and a single transaction make a failed script roll back its transaction; these options and their restrictions are documented in [psql](https://www.postgresql.org/docs/16/app-psql.html). The runner exercises this failure path. These five scripts use idempotent creation statements; there is no version/checksum ledger or automatic downgrade runner.

Before trusting a backup, restore it into a new, unused database and verify it. The test does this using the documented [custom-format dump](https://www.postgresql.org/docs/16/app-pgdump.html) and [pg_restore](https://www.postgresql.org/docs/16/app-pgrestore.html) flow:

```bash
docker compose exec -T postgres createdb -U trade trade_restore_test
docker compose exec -T postgres pg_restore -U trade -d trade_restore_test \
  --no-owner --no-acl --exit-on-error --single-transaction < "$backup_file"
```

Use the restored database for validation before switching the application connection. The tested backup covers this database's schema/data/sequences; global database roles, separate Saleor state, production TLS, scheduled retention, disaster-recovery timing and database least-privilege policies need their own operational work. Merchant isolation here is verified at the authenticated API boundary.

TASK-003 and TASK-010 remain in progress. Required-check enforcement, migration release/ledger policy, production recovery exercises and broader security review remain open.
