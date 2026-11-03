# v2.3 — UK API contract and transport safeguards

## Reference projects
- https://github.com/trade-tariff/trade-tariff-backend (MIT source code; check data terms separately)
- https://github.com/trade-tariff/trade-tariff-frontend
- https://docs.trade-tariff.service.gov.uk/

This release builds original C# tests and hardens the reference client. It does **not** import UK source code or certify rates.

## Changes
- Require the explicitly configured HTTPS UK API v2 prefix `/uk/api/v2/` and port 443, with no credentials, query or fragment.
- Disable redirects and cookies on the real HTTP transport to prevent upstream redirects to unexpected hosts.
- Offline mocked HTTP contract tests for request path, payload preservation, caching, oversized responses, invalid inputs, and non-2xx responses.
- No import duty, VAT, payable landed cost, or legal applicability is inferred.

## Verify on a machine with .NET 8 SDK
```
dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj
```
For an approved live UK API integration test, separately configure `TRADE_UK_TARIFF_API_BASE_URL=https://www.trade-tariff.service.gov.uk/uk/api/v2/` and use an authorized test environment. These tests are offline only.

## Limits
API schema and live availability have not been validated. The repository is not a source of verified EU tariffs. Never interpret a `reference_only` response as a payable quote.
