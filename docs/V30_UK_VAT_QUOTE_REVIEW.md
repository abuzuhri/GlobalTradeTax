# v3.0 — UK VAT review orchestration (TASK-008)

## Implemented
- `UkVatQuoteReview` joins the independent classification and amount readiness checks.
- New tenant-authenticated `POST /v1/references/uk/vat/review` endpoint.
- Explicitly flags mismatched VAT proposals, non-GBP amounts, NI treatment, and negative values.
- All payable amounts remain `null`. Arithmetic remains illustrative and never constitutes a duty/VAT quote.
- Five xUnit tests covering fail-closed behavior and integration of the two gates.

## Example request
```json
{
  "classification": {
    "destinationTerritory": "GB", "importProcedure": "free_circulation",
    "commodityCode": "0101210000", "goodsOrigin": "US", "importDate": "2026-11-02",
    "proposedVatRatePercent": 20, "rateSourceUrl": "https://www.gov.uk/",
    "rateEffectiveFrom": "2026-01-01"
  },
  "amounts": {
    "customsValueGbp": 1000, "importDutyGbp": 100,
    "freightToFirstDestinationGbp": 30, "insuranceGbp": 10,
    "otherTaxableCostsGbp": 0, "vatRatePercent": 20
  },
  "amountCurrency": "GBP", "shipmentId": "example-1"
}
```
These are fabricated illustration inputs, NOT authoritative UK duty/VAT rates.

## Remaining blockers
- Live UK API schema fixtures and end-to-end contract validation.
- Legal valuation scope, NI rules, special procedures, tariff measures, quotas and reliefs.
- Independent official VAT classification and legally correct rounding.
- Real provider evidence, verified FX and settlement compliance.

## Validation
- `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj` — NOT RUN (SDK unavailable).
- ZIP integrity checked; no claim of successful compilation.
