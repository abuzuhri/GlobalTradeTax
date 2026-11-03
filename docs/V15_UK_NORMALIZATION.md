# v1.5 UK reference normalization

## Reference and attribution
- Project: [UK Trade Tariff Backend](https://github.com/trade-tariff/trade-tariff-backend)
- API docs: https://docs.trade-tariff.service.gov.uk/
- Official UK tariff: https://www.gov.uk/trade-tariff
- Repository code license: MIT (GitHub metadata checked 2026-11-02). This does **not** establish permission to redistribute all datasets or make guarantees about API usage.

## New route
`GET /v1/references/uk/commodities/{code}/normalized` with merchant `X-API-Key`.

The endpoint calls the existing UK reference connector and projects a limited JSON:API response into `UkNormalizedCommodity` with commodity code, description, validity start date, and up to 100 relationship identifiers. Missing fields stay `null`. The raw route remains available.

**Important:** `VerifiedForCalculation` is always `false`. No percentage rates, duty amounts, VAT, import charges or landed totals are inferred. An import measure's applicability depends on origin, trade agreements, effective dates, quotas, safeguards, exemptions and other conditions. The UK dataset must not be used for EU import duties.

## Deployment and testing
Configure `TRADE_UK_TARIFF_API_BASE_URL` to the documented official HTTPS API prefix. Verify current API paths and permissions before enabling. The upstream schema and live endpoint have not been integration tested here. Add contract tests using authorized API fixtures, plus a real integration test, before production.

## Next steps
1. Validate live API JSON:API schema and current commodity paths.
2. Add versioned, auditable source snapshots and contract tests.
3. Implement applicability rules for GB imports using authoritative source evidence.
4. Add separate EU TARIC integration, not a reuse of UK rates.
