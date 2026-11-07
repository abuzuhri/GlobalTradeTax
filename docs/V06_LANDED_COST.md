# v0.6 — US to Worldwide landed-cost integration boundary

**2026-11-07 update:** [declared control holds](COMPLIANCE_HOLDS.md) now block provider calls and financial completion. Every new estimate carries a separate compliance result with false shipment-release approval; the authenticated request uses bounded exact JSON.

Added `POST /v1/landed-cost/estimate` (authenticated) and `IImportRateProvider`.

## Security and correctness
- Default provider intentionally returns `null` duty, VAT and landed total.
- A future provider must return amounts with currency, destination, HS classification, origin, effective date, expiry, provider name and audit reference.
- The service rejects stale evidence, mismatched currency/destination, and negative amounts.
- These checks are necessary but not sufficient for a production tax engine: validate each line's HS/origin, taxable base, exemptions, Incoterms, customs valuation, VAT registration and exchange rate with the provider.
- No IOSS, customs filing, remittance, guaranteed DDP, or payment processing is implemented.

## Next milestones
1. Add country-specific provider adapters and contract tests with official tariff fixtures.
2. Extend rate evidence to per-line HS/origin and fee components; validate complete coverage.
3. Add persisted quote snapshots, rate source licensing, rule versioning and expiration.
4. Add integration tests using PostgreSQL and cross-tenant isolation checks.
5. Obtain compliance review before publishing payable totals.

## Example
`curl -X POST http://localhost:5080/v1/landed-cost/estimate -H 'X-API-Key: ...' -H 'Content-Type: application/json' --data @samples/us-to-sweden.json`
