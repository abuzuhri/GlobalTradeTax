# v1.7 — UK tariff measure evidence (non-payable)

## Provenance
The independent C# implementation was informed by the publicly available architecture of:
- https://github.com/trade-tariff/trade-tariff-backend
- https://github.com/trade-tariff/trade-tariff-frontend
- https://docs.trade-tariff.service.gov.uk/

No UK repository code or tariff database was copied. Check API/data licensing and permitted commercial usage separately.

## Endpoint
`GET /v1/references/uk/commodities/0101210000/evidence?origin=US&importDate=2026-11-02`

Header: `X-API-Key: <merchant key>`.

The endpoint examines embedded JSON:API measure records and their direct relationships, identifying geographic references, conditions, quotas, additional codes, exclusions and raw duty expressions. It does not follow remote relationships or establish that any measure applies. A direct country match is only evidence, never legal eligibility. A raw duty expression is **not** a computed tax rate. Missing measures are **not** zero duty.

All results are `requires_review`; `verifiedForCalculation` is always `false`. No duty, VAT or landed-cost amount is generated.

## Next engineering steps
1. Validate API response structure against documented live UK API fixtures.
2. Resolve linked geography memberships, exclusions and measures with bounded, cached requests.
3. Evaluate legal conditions and quotas with provenance and effective dates.
4. Build official-source regression fixtures before enabling calculation.

Tests: `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj`.
