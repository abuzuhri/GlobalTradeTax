# UK import assessment orchestration (v2.1)

## Source and attribution
- UK Trade Tariff Backend: https://github.com/trade-tariff/trade-tariff-backend (MIT-licensed source code)
- UK Trade Tariff Frontend: https://github.com/trade-tariff/trade-tariff-frontend
- API documentation: https://docs.trade-tariff.service.gov.uk/
- Official UK tariff: https://www.gov.uk/trade-tariff

This C# assessment implementation is original code, inspired by the separation of measures, geography, and tariff data in the UK service. No UK repository code or UK tariff dataset is copied into this project. API data rights, update cadence and live schema must be checked independently.

## New endpoint
`GET /v1/references/uk/assessments/{code}?origin=US&importDate=2026-11-02`

**2026-11-05 update:** import dates now reach both upstream lookups and cache keys. Each measure adds explicit candidate evidence, including typed memberships, effective windows and exclusions; overlapping measures remain separate. [Current contract, evidence v2 and verified results](UK_APPLICABILITY_EVIDENCE.md) supersede the original reference-parser assumptions below. All payable gates remain closed.
Header: `X-API-Key: ...`

The endpoint fetches a commodity, extracts measure evidence, screens geography, performs bounded (max 20, concurrency 4) geography lookups and returns an audit-friendly list of unresolved checks. It never claims an applicable rate or calculates payable customs duty or VAT. The UK reference client caches geographical lookups for ten minutes.

## Run
Set `TRADE_UK_TARIFF_API_BASE_URL` to the *verified* UK API v2 HTTPS root, for example `https://www.trade-tariff.service.gov.uk/uk/api/v2/` after confirming against the API docs. Use `dotnet test` and `dotnet run --project src/GlobalTradeTax.Api` in an environment with .NET 8 SDK.

## Remaining requirements for a production UK landed-cost calculator
1. Verify live API v2 schema and the relationship structures against recorded fixtures.
2. Validate measure types, effective dates, exclusions and geographical memberships using official data.
3. Resolve origin/preference evidence, commodity classifications, quotas, licences, anti-dumping and other trade remedies.
4. Implement UK customs valuation, currency conversion and specific/mixed duty expressions with versioned rules.
5. Implement UK import VAT, excise and reliefs using authoritative UK guidance; distinguish GB from Northern Ireland.
6. Compare calculations against official UK examples and obtain domain/legal review.
7. Add retries, circuit breaking, monitoring, persisted evidence, provenance and integration tests before accepting payments.

**Fail closed:** unknown rates, import duty and VAT remain `null`. This release is not a legally verified customs calculator.
