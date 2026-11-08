# v1.2 — PostgreSQL tariff reference staging

**2026-11-05 update:** migration 005 adds exact-byte archives and append-only operator review evidence. Existing volumes must apply it after 001–004. Matching original uploads can backfill older staged batches; no archive or licence approval is invented. See [current API, migration and verification](TARIFF_PROVENANCE_REVIEW.md).

## Scope
Validated CSV rows are written transactionally to PostgreSQL, with SHA-256 deduplication. This is **reference-only staging**. No imported row is activated for IImportRateProvider, no VAT is calculated, and no payable customs amount is derived.

## Setup
1. On existing PostgreSQL volumes, use the controlled [migration and recovery procedure](POSTGRES_CONTAINER_VERIFICATION.md#operator-migration-backup-and-recovery). Docker entrypoint scripts only run on first database initialization.
2. Set `TRADE_OPERATOR_KEY` to a separately generated random secret of at least 32 characters. Never reuse merchant API keys.
3. Send CSV with `Content-Type: text/csv` and `X-Operator-Key` to `POST /v1/admin/tariff-datasets/stage`. Max request body is 1 MiB. Returns batch UUID and SHA-256; duplicate byte-identical files reuse batch ID.
4. Retrieve metadata via `GET /v1/admin/tariff-datasets/{id}` with the same operator header.

## Limitations / production requirements
- The importer checks CSV format, source URL HTTPS, and row syntax; it **does not verify source authenticity, licensing, origin qualification, TARIC measures, quota conditions, anti-dumping, VAT, or applicable date of import**.
- Admin secret is an MVP operator gate, not a full RBAC system. Deploy behind HTTPS, private network / gateway, rate limits, and audit logging. Do not expose this endpoint publicly.
- Only one source URL/version is accepted per import batch. Datasets remain staged indefinitely and cannot affect customer quotes.
- Schema migration is not automated on existing volumes.
- .NET build and integration tests have not been run in this environment.

## GitHub reference
https://github.com/trade-tariff/trade-tariff-backend — MIT licensed, UK tariff backend. Reference architecture only; does not grant rights to third-party datasets or provide EU TARIC rates.
