# v1.8 UK geography evidence screening

Independent C# implementation informed by the open-source UK Trade Tariff Backend and Frontend:
- https://github.com/trade-tariff/trade-tariff-backend
- https://github.com/trade-tariff/trade-tariff-frontend
- https://docs.trade-tariff.service.gov.uk/

GET /v1/references/uk/commodities/{code}/geography?origin=US&importDate=2026-11-02
Header: X-API-Key: <tenant key>

Checks direct country references, reports missing group membership information and unresolved exclusions, conditions, quotas, and additional codes. Group memberships are **not** inferred from names or relationship surrogate IDs. This endpoint is evidence-only and always returns VerifiedForCalculation=false. No tariff rate, import VAT, or payable landed cost is produced.

Next: review official API relationship schemas and build a separate bounded, authenticated, cached geography lookup client with explicit provenance and freshness controls. The UK API is for UK measures, not EU TARIC.

Testing: xUnit tests added, but dotnet SDK was not available in the authoring environment; compilation and live API behavior remain unverified.
