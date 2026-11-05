# v2.0 — UK geographical-area membership evidence

**2026-11-05 update:** the original assumed membership-resource parser below has been replaced by the recorded date-scoped `children_geographical_areas` tree contract. See [current behavior and compatibility](UK_APPLICABILITY_EVIDENCE.md). Undated/unverified structures stay unknown; positive paths remain candidate reference evidence only.

Reference: https://github.com/trade-tariff/trade-tariff-backend
Documentation: https://docs.trade-tariff.service.gov.uk/

This release adds `UkAreaMembership.Inspect`, an evidence-only parser, and:

`GET /v1/references/uk/geographical-areas/{id}/membership?origin=US&importDate=2026-11-02`

It deliberately fails closed when membership details or dates are absent. The assumed JSON relationship names must be validated against a live API response before treating a match as meaningful. Even a matching relationship is only *candidate evidence*, not a verified tariff rate or legal eligibility. The endpoint never returns payable duty or VAT.

The source project is used as architectural reference; no third-party code or tariff data is bundled. Review the upstream data terms separately from repository source-code licensing.

Tests were authored but not executed if .NET SDK is unavailable.
