# v3.1 UK VAT review consistency safeguards

**Current update (2026-11-06):** [VAT exception/accounting review](UK_VAT_EXCEPTIONS.md) adds procedure, declared relief and known GB/XI dispatch suppression, segregated conditional valuation and strict financial JSON. Local tests now execute successfully; the SDK note below records the original slice.

TASK-008 remains **in-progress**. The quote review now suppresses even *illustrative* GBP VAT figures when currency is not GBP, territory is not GB, the proposed VAT rates disagree, a rate is outside its effective dates, or essential classification fields are invalid. This prevents contradictory input from being presented as a plausible GBP calculation.

This does **not** validate a legal VAT rate or enable payable totals. The existing sample with internally consistent inputs can still show explicitly illustrative values. All payable amounts remain null.

Run `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj` in a .NET 8 environment. Tests were not executed here because the SDK is unavailable. No live UK API contract was validated.
