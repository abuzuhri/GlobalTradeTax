# v0.5: Multi-merchant API authentication

## Database migration
For an existing v0.4 database, execute `db/002_tenants.sql` manually with psql. Docker's `/docker-entrypoint-initdb.d` runs only when the database volume is initially created. Do not delete existing volumes to apply migrations.

For a fresh installation, both SQL scripts run in filename order when mounted as a directory.

## Provision merchant keys
Install Python package `psycopg[binary]` in an administrative environment. Set `DATABASE_URL` to a privileged PostgreSQL connection string, then run:

`python scripts/provision-tenant.py merchant-001 "First Merchant"`

Save the printed key securely; only its SHA-256 hash is stored in PostgreSQL. Send the key as `X-API-Key`.

Revoke keys with `UPDATE tenant_api_keys SET enabled=false, revoked_at=now() WHERE key_id=...` using an administrative DB session. Rotate by creating a new key, deploying it, then revoking the old one.

## Security notes
- PostgreSQL tenant lookup enforces active tenant and active key. Quotes remain tenant-filtered.
- Authentication is database-backed when a PostgreSQL connection is configured. Development fallback is only enabled without PostgreSQL and with ASPNETCORE_ENVIRONMENT=Development.
- API keys must only be sent over TLS. No key-management HTTP endpoint is exposed.
- This is an early implementation; add rate limiting, audit logs, migration tooling, operational monitoring and integration tests before production.
- Calculation remains `requires_provider_quote`; do not charge customers or promise accurate duties based on the current quote.
