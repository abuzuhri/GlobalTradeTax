# v0.9 — US outbound foundation: multiple delivery steps

## Delivered
- HTTPS external landed-cost provider adapter (contract-based; not connected to a live tariff vendor)
- Evidence validation including provider, reference, effective date, origin, destination, HS and currency
- Fail-closed handling for provider outage and unsupported multi-line verified quotes
- US outbound compliance checklist API (advisory only)
- Tenant-scoped PostgreSQL audit events for landed-cost estimates
- Database migration 003 and smoke tests for source-level contracts

## Critical limitations
- No actual tariff data subscription, no IOSS registration, no live settlement or payment routing.
- Evidence for a multi-line quote cannot be represented safely by the existing single-HS record; such quotes remain unverified.
- This is not a legally binding customs assessment. A carrier/broker and current official rules remain necessary.
- A live provider must implement POST JSON QuoteRequest -> RateResult with verified evidence.

## Setup
Set TRADE_RATE_PROVIDER_URL to an HTTPS endpoint and TRADE_RATE_PROVIDER_TOKEN to a secret.
Run db/003_audit.sql against existing PostgreSQL deployments; compose initialization scripts run only on new volumes.
Run docker compose up --build. Use POST /v1/landed-cost/estimate and POST /v1/compliance/check.

## Next milestones
1. Per-line evidence and duty/tax base breakdown, exchange-rate evidence, VAT/IOSS eligibility.
2. Live licensed rate-provider contract and golden test cases verified with customs brokers.
3. Shopify checkout integration, quote expiry, webhooks, tenant usage billing.
4. Carrier broker settlement reconciliation; never move tax funds without approved agreements.
