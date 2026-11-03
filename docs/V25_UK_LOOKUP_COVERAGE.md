# v2.5 — UK geographical lookup coverage

The assessment now sorts and deduplicates geographical IDs, excludes the origin, and reports omitted IDs if its 20-lookup safety cap is exceeded. Exactly 20 IDs is not truncated. This is evidence coverage, not legal eligibility or a payable customs quote.

Run `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj` on .NET 8. Not executed here. Live UK membership contracts and exclusion handling remain unresolved. Import duty and VAT remain null.
