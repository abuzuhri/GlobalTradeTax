namespace GlobalTradeTax.Api;

// Illustrative arithmetic, not a UK tax determination. Never emit a payable amount.
public sealed record UkVatInput(decimal? CustomsValueGbp, decimal? ImportDutyGbp,
    decimal? FreightToFirstDestinationGbp, decimal? InsuranceGbp,
    decimal? OtherTaxableCostsGbp, decimal? VatRatePercent,
    bool CustomsValueVerified = false, bool DutyVerified = false,
    bool CostsVerified = false, bool VatRateVerified = false,
    bool ReliefsAndExemptionsVerified = false);

public sealed record UkVatReadinessResult(string Status, decimal? IllustrativeTaxableBaseGbp,
    decimal? IllustrativeVatGbp, decimal? PayableVatGbp, string[] UnresolvedChecks,
    bool VerifiedForCalculation = false);

public static class UkVatReadiness
{
    public static UkVatReadinessResult Evaluate(UkVatInput input)
    {
        var checks = new List<string>();
        if (input.CustomsValueGbp is null) checks.Add("missing_customs_value_gbp");
        if (input.ImportDutyGbp is null) checks.Add("missing_verified_import_duty_gbp");
        if (input.FreightToFirstDestinationGbp is null) checks.Add("missing_freight_to_first_destination_gbp");
        if (input.InsuranceGbp is null) checks.Add("missing_insurance_gbp");
        if (input.OtherTaxableCostsGbp is null) checks.Add("missing_other_taxable_costs_gbp");
        if (input.VatRatePercent is null) checks.Add("missing_verified_vat_rate");
        if (!input.CustomsValueVerified) checks.Add("customs_valuation_not_verified");
        if (!input.DutyVerified) checks.Add("duty_applicability_not_verified");
        if (!input.CostsVerified) checks.Add("taxable_cost_scope_not_verified");
        if (!input.VatRateVerified) checks.Add("vat_classification_not_verified");
        if (!input.ReliefsAndExemptionsVerified) checks.Add("vat_reliefs_and_exemptions_not_verified");
        if (new decimal?[] { input.CustomsValueGbp, input.ImportDutyGbp,
            input.FreightToFirstDestinationGbp, input.InsuranceGbp, input.OtherTaxableCostsGbp }
            .Any(x => x < 0) || input.VatRatePercent is < 0 or > 100)
            return new("invalid_input", null, null, null, ["negative_amount_or_invalid_rate"]);
        if (checks.Count > 0)
            return new("requires_review", null, null, null, checks.ToArray());
        try
        {
            var taxableBase = checked(input.CustomsValueGbp!.Value + input.ImportDutyGbp!.Value
                + input.FreightToFirstDestinationGbp!.Value + input.InsuranceGbp!.Value
                + input.OtherTaxableCostsGbp!.Value);
            var vat = UkDutyComponents.MultiplyDivide(taxableBase,input.VatRatePercent!.Value,100m);
            if (vat is null) return new("requires_review",taxableBase,null,null,["vat_arithmetic_precision_lost"]);
            // Even with user-supplied verification flags, independent authoritative evidence,
            // legal rounding, and import procedure must be checked before payable output.
            return new("illustrative_only", taxableBase, vat, null,
                ["authoritative_evidence_and_import_procedure_review_required", "legal_rounding_not_verified"]);
        }
        catch (OverflowException)
        {
            return new("invalid_input", null, null, null, ["arithmetic_overflow"]);
        }
    }
}
