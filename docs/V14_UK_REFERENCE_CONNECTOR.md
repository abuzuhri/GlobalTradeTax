# v1.4 - UK Trade Tariff read-only connector

## Attribution
UK Trade Tariff Backend: https://github.com/trade-tariff/trade-tariff-backend
API documentation: https://docs.trade-tariff.service.gov.uk/
UK tariff website: https://www.gov.uk/trade-tariff
The repository is MIT licensed according to GitHub metadata checked 2026-11-02. Data/API rights are separate and must be checked before commercial use.

## Implementation
- `UkTariffReferenceClient` performs read-only HTTPS GET for an 8-10 digit commodity code.
- `GET /v1/references/uk/commodities/{code}` requires a merchant API key.
- Explicit `TRADE_UK_TARIFF_API_BASE_URL` required; host allowlisted to the official UK tariff service and path must contain `/api/`.
- Eight-second timeout, 1 MiB response cap, no redirects to third-party hosts (configure HttpClient handler if deployment needs stricter redirect handling), errors handled without leaking credentials.
- Responses are marked `reference_only`; **no duties, VAT or payable totals** are calculated.
- Verify API endpoint shape and terms against live documentation before enabling; upstream request was not integration-tested.

## Example
```bash
curl -H 'X-API-Key: YOUR_KEY' http://localhost:8080/v1/references/uk/commodities/0101210000
```

## Next
Validate official API schema and availability, add a schema-normalization layer, provenance snapshots, tests with mocked HTTP responses, and independent GB customs duty rules. Do not reuse GB tariff data for EU import calculations.
