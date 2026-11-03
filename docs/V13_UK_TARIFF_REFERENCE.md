# v1.3 - UK Trade Tariff open-source reference

## Registered source

- Project: UK Trade Tariff Backend
- Repository: https://github.com/trade-tariff/trade-tariff-backend
- Public API documentation: https://docs.trade-tariff.service.gov.uk/
- Official online tariff: https://www.gov.uk/trade-tariff
- Repository license as reported by GitHub on 2026-11-02: MIT
- Scope: UK and Northern Ireland services, **not EU TARIC**.
- Technology: Ruby on Rails, Sequel, PostgreSQL, OpenSearch, Redis/Sidekiq.
- Data domains described by repository README: commodity codes, duties, measures, quotas, certificates, rules of origin.

## How we can benefit

1. Study its separation of commodity classification, measures, quotas and rules of origin.
2. Evaluate its documented API for a **future** read-only GB/NI adapter.
3. Reuse architectural concepts in our .NET domain model without copying UK tariff rates into EU calculations.
4. Preserve source URL, retrieval time, source version, license and evidence for every eventual import.

## Legal and technical guardrails

- MIT applies to repository code; it does **not automatically license upstream tariff datasets, trademarks, third-party content or unrestricted API usage**.
- Do not describe this source as a certified EU or global tariff provider.
- No automatic downloads, live API requests, or tariff data imports are implemented in v1.3.
- Do not populate import duty, VAT or payable totals from this reference registry.
- Review API terms, rate limits, data licensing and production availability before implementing the GB/NI connector.

## API

- `GET /v1/data-sources` - all references, including provenance metadata.
- `GET /v1/data-sources/uk-trade-tariff` - UK project details, licensing, attribution and warnings.

## Verification

Repository metadata and README were inspected through the GitHub connector on 2026-11-02. No .NET build or runtime tests were executed in this environment.
