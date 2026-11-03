# v3.1 UK VAT review consistency safeguards

TASK-008 remains **in-progress**. The quote review now suppresses even *illustrative* GBP VAT figures when currency is not GBP, territory is not GB, the proposed VAT rates disagree, a rate is outside its effective dates, or essential classification fields are invalid. This prevents contradictory input from being presented as a plausible GBP calculation.

This does **not** validate a legal VAT rate or enable payable totals. The existing sample with internally consistent inputs can still show explicitly illustrative values. All payable amounts remain null.

Run `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj` in a .NET 8 environment. Tests were not executed here because the SDK is unavailable. No live UK API contract was validated.
