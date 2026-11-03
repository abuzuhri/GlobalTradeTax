# v2.9 UK VAT classification evidence gate

The gate is a pure C# service (`UkVatClassificationGate.Evaluate`) that collects and validates user-supplied metadata. It does **not** validate that a URL is legally authoritative, verify a proposed rate against HMRC, or determine import VAT treatment. An HTTPS URL is a transport check only. Every response is `requires_review` with a null confirmed rate, including fully self-attested inputs.

- `GB` means Great Britain; `XI` is a distinct Northern Ireland goods context, and is always flagged for additional rules.
- Goods origin is not the shipping origin.
- Effective-date checks cannot replace authoritative historical tariff/VAT data.
- VAT payable and landed cost remain null pending verified import duty, rate classification, reliefs, valuation, legal rounding, and procedure handling.

Run: `dotnet test tests/GlobalTradeTax.Api.Tests/GlobalTradeTax.Api.Tests.csproj`.

Official starting points (not embedded rates): https://www.gov.uk/guidance/rates-of-vat-on-different-goods-and-services and https://www.gov.uk/government/collections/vat-notices-numerical-order .
