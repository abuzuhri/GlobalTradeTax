# v2.7 — UK duty expression input requirements

## Scope
TASK-007: Corrected the illustrative arithmetic evaluator so that **specific** duties (GBP/kg, GBP/100 kg, GBP/tonne, GBP/item) need only the relevant quantity, not a customs value. Percentage and compound expressions still require an explicit customs value in GBP.

## Fail-closed behavior
- Unsupported expression formats, missing required inputs, negative inputs and arithmetic overflow return `requires_review` with `IllustrativeAmount = null`.
- `VerifiedForCalculation` remains `false` for **all** results, including zero.
- No API endpoint issues payable amounts from this evaluator.
- No claim of live UK tariff parsing, official currency conversion, quotas, preferences, valuation, duty liability, or legal eligibility.

## Test commands
```bash
dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj
```
Execution pending: dotnet SDK not installed in authoring environment. CI must validate before merging.

## Follow-up
Verify official live UK API schemas and published calculator examples, establish applicable measures and valuations, then implement legally grounded calculation separately from this illustrative arithmetic helper.
