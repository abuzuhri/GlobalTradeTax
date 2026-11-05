# v2.2 UK evidence package

**2026-11-05 compatibility update:** current envelopes use `uk-reference-evidence/v3` to bind territory, exact response digests and structured rules as well as dated applicability and membership. Preserve the version with stored hashes; v1/v2/v3 canonical digests are not interchangeable. See [current UK review contract](UK_SHIPMENT_REVIEW.md). The v2 slice is retained in [historical applicability evidence](UK_APPLICABILITY_EVIDENCE.md).

Adds `GET /v1/references/uk/assessments/{code}/evidence-package?origin=US&importDate=2026-11-02`.

The endpoint calls the existing UK reference assessment and wraps the result in a versioned, SHA-256-addressed evidence package. Its hash is computed from a canonical projection of the assessment, with measures, areas and warnings sorted, excluding capture timestamp. This makes it possible to detect changes to evidence independently of retrieval time. The hash is **not** a digital signature or proof that upstream data is accurate.

The endpoint is tenant-authenticated, read-only, and always `approvedForPayment=false`. No rates are invented; import duty and VAT remain null. The package is for audit/debugging, not customs declarations, payment collection, or legal advice.

The test project includes deterministic-hash tests. These were written but cannot be run without the .NET SDK.

References: https://github.com/trade-tariff/trade-tariff-backend and https://github.com/trade-tariff/trade-tariff-frontend . Code is independently authored in C#; no upstream code copied. UK coverage does not imply EU coverage.

Remaining UK production work: confirm live API schema and licensing/data rights, real integration tests, applicability/exclusion/condition and quota resolution, duty expression/valuation/currency engine, VAT treatment, reconciliation with official UK calculator, observability, and compliance review.
