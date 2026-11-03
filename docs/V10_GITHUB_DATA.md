# v1.0 — GitHub and official data-source discovery

## What was implemented
- Added `GET /v1/data-sources`, a read-only catalog of candidate trade data sources.
- Explicit `discovery_only` status prevents confusing a GitHub repository with a production-grade tariff provider.
- Kept `IImportRateProvider` as the integration boundary; no tariff rates are invented.

## Open-source and official references to evaluate
- UK Trade Tariff backend: search GitHub for `trade-tariff-backend` (repository naming and license must be confirmed).
- EU TARIC official tariff reference: https://taxation-customs.ec.europa.eu/customs-4/calculation-customs-duties/customs-tariff/eu-customs-tariff-taric_en
- USITC HTS: https://hts.usitc.gov/
- World Bank WITS: https://wits.worldbank.org/

## Required next implementation steps
1. Verify the upstream repository's exact identity, license and data usage permissions.
2. Define source ingestion contract with source URL, retrievedAt, effectiveFrom, effectiveTo, country, tariff measure and source hash.
3. Build scheduled ingestion and validation tests against official published examples.
4. Add destination VAT providers separately from tariff providers.
5. Implement per-line tariff calculation and country-of-origin aware rules.
6. Add fallback statuses for missing rates, quotas, trade remedies and preferential-origin evidence.

No live tariff dataset is bundled. No actual remittance or payment functionality is implemented.
