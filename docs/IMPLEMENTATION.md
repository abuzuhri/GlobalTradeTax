# v0.2 implementation notes

Added development API-key authentication, idempotent quote creation, tenant-scoped quote retrieval, Dockerfile, and example environment settings.

## Start

```bash
export TRADE_API_KEY='replace-with-a-long-random-secret'
export TRADE_TENANT_ID='demo-merchant'
dotnet run --project src/GlobalTradeTax.Api --urls http://localhost:5080
```

## Create quote

```bash
curl -X POST http://localhost:5080/v1/quotes \
 -H 'Content-Type: application/json' \
 -H 'X-API-Key: replace-with-a-long-random-secret' \
 -H 'Idempotency-Key: demo-order-001' \
 --data @samples/us-to-sweden.json
```

Repeat the same request with the same key to obtain the same quote; changing the payload with the same key returns HTTP 409. Quotes are scoped to the configured tenant. This is NOT multi-tenant production authentication: a single key and tenant are configured per deployment.

## Production blockers

- In-memory quote storage is lost on restart; replace with PostgreSQL and a unique (tenant_id, idempotency_key) constraint.
- Add real merchant onboarding, hashed per-merchant API credentials, rotation, audit logs, rate limits, HTTPS, and monitoring.
- Source licensed, effective-dated customs duty/VAT datasets, validate origin and HS/HTS/Schedule B classifications, and build an independently verified calculation engine.
- Preliminary EEI screening is not a legal determination; HS codes are not interchangeable with Schedule B numbers.
- No collection, customs filing, tax remittance, or settlement functionality exists yet.
- Do not use incomplete quotes as customer-facing final prices.

Authenticated `GET /v1/references/za-ke-ng-eg/{country}` provides ZA, KE, NG and EG national reference-only guidance under the shared tenant budget. Unsupported and malformed codes return 400. Routing stays OTHER; amounts and approval gates remain closed. See [source limits](SOURCES.md).

`GET /v1/references/rs-al-mk/{country}` is an authenticated, tenant-budgeted static reference for RS, AL and MK. It rejects unsupported or malformed codes, retains OTHER routing and supplies unresolved shipment checks; it does not calculate or approve any shipment.
