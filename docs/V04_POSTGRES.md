# v0.4 — durable quote storage (USA -> worldwide)

## Start

Set `POSTGRES_PASSWORD`, `TRADE_API_KEY`, and `TRADE_TENANT_ID` in `.env` (do not commit secrets), then run `docker compose up --build`.

POST `/v1/quotes` with `X-API-Key` and `Idempotency-Key`; GET `/v1/quotes/{id}` using the same API key. The schema is created automatically on **first initialization of a new database volume**. For an existing database, apply `db/001_init.sql` manually.

## Safety and limitations

- PostgreSQL persistence is enabled only when `TRADE_POSTGRES_CONNECTION` is configured. Otherwise the original in-memory store remains for local development.
- Tenant identification still comes from a **single configured API key**. It is not production multi-tenant authentication. Add a merchants/API-keys table and hashed rotating keys next.
- Database migration runner, integration tests, secrets management, rate limiting, backups, encryption policies, and operational monitoring are not implemented.
- No customs tariff provider is connected; duty and tax remain `null` rather than guessed. The export EEI screen is advisory only.
- Payment collection and government remittance are not implemented.
- This release has not been compiled or integration-tested in this environment; run `dotnet build` and container integration tests before deployment.
