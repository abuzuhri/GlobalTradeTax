# v2.4 UK API contract guard

Task: [005](../tasks/005-uk-live-api-contract-fixtures.md).

Changes:
- `UkJsonApiContract.IsValidResource` rejects missing data, missing resource type, mismatched resource ID, malformed included collections and nonempty JSON:API errors.
- UK commodity and geographical-area reference endpoints now fail closed with `invalid_upstream_contract` and no payload on structural mismatch.
- Invalid geographical-area responses are not cached.
- Contract tests added for malformed/mismatched responses.
- `.github/workflows/dotnet-ci.yml` builds and runs xUnit on pushes and PRs.

This is intentionally a minimal structural check, NOT a full JSON:API schema or proof of UK tariff legal applicability. No external source data is bundled.

## Validation still required
1. Run `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj`.
2. Run a controlled live smoke test with a documented official UK API URL and version.
3. Capture permitted, sanitized real fixtures and validate resource types and included relationships.
4. Confirm UK and Northern Ireland distinctions, data licensing and service terms.
5. Complete duty/VAT applicability checks before returning monetary estimates.

Reference: https://github.com/trade-tariff/trade-tariff-backend and https://docs.trade-tariff.service.gov.uk/
