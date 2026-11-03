# Implementation roadmap

## Milestone 0 — completed in scaffold
- ASP.NET Core minimal API and JSON contract
- Quote validation and line-item merchandise calculation
- Ship-from vs country-of-origin separation
- Preliminary US EEI screen with warnings
- No invented tariff or tax results
- Two sample trade lanes

## Milestone 1 — production-grade quoting
- PostgreSQL: tenants, merchants, quotes, lines, rule snapshots, provider evidence
- API-key authentication, tenant isolation, quotas, idempotency, audit logging
- Destination tax/tariff provider adapter; FX source with timestamp
- HS code versioning, origin preference and restrictions
- Duty/VAT basis, freight allocation, currency and rounding per jurisdiction
- Automated golden test cases reviewed with customs specialist

## Milestone 2 — US outbound
- Country coverage based on signed data provider contracts
- US Schedule B / AES prefiling workflow and carrier integrations
- EU IOSS handling, EU 2026 low-value duty, Gulf and LATAM rule modules
- Shopify and WooCommerce checkout integration

## Milestone 3 — China and worldwide inbound to US
- HTSUS and Chapter 99 stacking, AD/CVD, Section 301/232, fees
- CBP entry type, broker and importer-of-record workflow
- Country-of-origin and preferential trade treatment checks
- Landed-cost quote to post-entry reconciliation

## Milestone 4 — payments and settlement
- Partner-led collection, ledger and webhook event processing
- Broker invoices, reconciliation, disputes, refunds
- Licensing and legal review before any custody or remittance

## Important non-goals of MVP
- No direct AES/CBP filing
- No payment custody or tax remittance
- No duty guarantee
- No broad country-rate assumptions
