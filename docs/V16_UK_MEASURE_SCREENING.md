# v1.6 — UK measure screening (reference-only)

## Sources and attribution
- UK Trade Tariff Backend: https://github.com/trade-tariff/trade-tariff-backend
- UK Trade Tariff Frontend: https://github.com/trade-tariff/trade-tariff-frontend
- API documentation: https://docs.trade-tariff.service.gov.uk/
- Official tariff: https://www.gov.uk/trade-tariff

The public GitHub project informed the architecture (commodity, measures, relationships, rules of origin, quotas). The new `UkMeasureScreening.cs` is an independent C# implementation; no Ruby code or UK tariff dataset was copied. Verify applicable licenses and API/data terms before commercial use. The GitHub backend repository reports MIT licensing for its code, not necessarily for tariff datasets.

## API
`GET /v1/references/uk/commodities/0101210000/screen?origin=US&importDate=2026-11-02`
Header: `X-API-Key: <merchant key>`.
Requires `TRADE_UK_TARIFF_API_BASE_URL` configured to the verified API root. The current connector fetches the commodity document and screens **only embedded** `included` entries of JSON:API type `measure`; it does not follow pagination or resolve related entities.

All measures are `requires_review` because geography groups/exclusions, quotas, conditions, preferences, additional codes, end-use, trade remedies, and current applicability are not resolved. Missing embedded measures must **not** be interpreted as zero duty. No duty rate, VAT or landed-cost amount is produced.

## Run tests
`dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj`

## Next steps
1. Confirm actual UK API response structure using live integration tests and adapt parsing accordingly.
2. Resolve measures, geography groups and exclusions via documented API endpoints, preserving provenance and snapshot date.
3. Implement explicit deterministic eligibility with test fixtures, then independently verify against official UK examples.
4. Keep EU TARIC separate from UK tariffs.
