namespace GlobalTradeTax.Api;

// Caller declarations only. No flag can authenticate a claim or approve payment.
public sealed record UkVatTreatmentClaim(string? Treatment, string? AccountingMethod = null, string? EvidenceReference = null);
public sealed record UkConditionalVatScenario(decimal FullBaseGbp, decimal ValuationFraction,
    decimal ConditionalBaseGbp, decimal AssumedVatRatePercent, decimal ConditionalVatGbp)
{
    public string Status => "conditional_arithmetic_only";
    public bool VerifiedForCalculation => false;
    public bool ApprovedForPayment => false;
}
public sealed record UkVatTreatmentDecision(string ClaimedTreatment, string AccountingMethod,
    string Status, bool SuppressOrdinaryIllustration, UkConditionalVatScenario? ConditionalScenario,
    string[] RequiredChecks, string[] SourceUrls)
{
    public bool VerifiedForCalculation => false;
    public bool ApprovedForPayment => false;
    public decimal? ConfirmedVatRatePercent => null;
    public decimal? PayableVatGbp => null;
    public string? DeclaredEvidenceReference { get; init; }
    public DateOnly SourcesReviewedOn => new(2026,11,6);
}

public static class UkVatTreatments
{
    public const string ValuationSource = "https://www.gov.uk/guidance/how-to-value-goods-for-import-vat";
    public const string ReturnedGoodsSource = "https://www.gov.uk/guidance/pay-less-import-duty-and-vat-when-re-importing-goods-to-the-uk-and-eu";
    public const string TemporaryAdmissionSource = "https://www.gov.uk/guidance/apply-to-import-goods-temporarily-to-the-uk-or-eu";
    public const string PostponedSource = "https://www.gov.uk/guidance/check-when-you-can-account-for-import-vat-on-your-vat-return";
    public const string NiSource = "https://www.gov.uk/guidance/how-vat-will-apply-to-goods-moving-between-great-britain-and-northern-ireland";

    public static string[] Validate(UkVatTreatmentClaim? claim)
    {
        if (claim is null) return [];
        var checks = new List<string>();
        if (claim.Treatment is not ("ordinary" or "reduced_valuation_art" or "returned_goods_relief" or "temporary_admission" or
            "vat_exemption" or "other_relief" or "unknown")) checks.Add("invalid_vat_treatment_claim");
        if (claim.AccountingMethod is not (null or "unknown" or "pay_at_import" or "postponed_accounting" or "seller_collected"))
            checks.Add("invalid_vat_accounting_method");
        if (claim.EvidenceReference is not null && (string.IsNullOrWhiteSpace(claim.EvidenceReference) ||
            claim.EvidenceReference.Length > 256 || claim.EvidenceReference.Any(char.IsControl))) checks.Add("invalid_vat_claim_reference");
        return checks.ToArray();
    }

    // Full base is supplied only after upstream amount/context consistency checks.
    public static UkVatTreatmentDecision Assess(UkVatTreatmentClaim? claim, string? territory, string? procedure,
        decimal? proposedRate, decimal? fullBaseGbp = null, string? shipFrom = null, string? commodityCode = null)
    {
        var checks = new HashSet<string>(Validate(claim),StringComparer.Ordinal);
        var invalid = checks.Count > 0;
        var treatment = claim?.Treatment ?? "unspecified";
        var accounting = claim?.AccountingMethod ?? "unspecified";
        var sources = new HashSet<string>(StringComparer.Ordinal) { ValuationSource };
        checks.Add("vat_treatment_and_relief_eligibility_require_independent_review");
        if (claim is null) checks.Add("vat_treatment_not_declared_legacy_reference_scope");
        var suppress = invalid || treatment is not ("ordinary" or "unspecified");
        switch (treatment)
        {
            case "reduced_valuation_art":
                checks.Add("art_antiques_collectors_item_classification_and_reduced_valuation_eligibility_unverified");
                checks.Add("reduced_valuation_scenario_not_included_in_shipment_totals");
                break;
            case "returned_goods_relief":
                sources.Add(ReturnedGoodsSource);
                checks.Add("returned_goods_identity_export_reimport_condition_and_vat_ownership_evidence_required");
                break;
            case "temporary_admission":
                sources.Add(TemporaryAdmissionSource);
                checks.Add("temporary_admission_authorisation_use_reexport_and_full_or_partial_relief_require_review");
                break;
            case "vat_exemption":
            case "other_relief":
                checks.Add("claimed_exemption_or_relief_is_not_a_verified_zero_rate");
                break;
            case "unknown": checks.Add("vat_treatment_unknown"); break;
        }
        if (procedure != "free_circulation")
        {
            suppress = true;
            checks.Add("special_or_unknown_vat_import_procedure_requires_review");
        }
        if (territory != "GB")
        {
            suppress = true;
            if (territory == "XI")
            {
                sources.Add(NiSource);
                checks.Add("ni_duty_risk_declaration_does_not_determine_vat_treatment");
                checks.Add(shipFrom == "GB" ? "gb_to_ni_vat_registration_and_movement_facts_required" :
                    "ni_dispatch_customs_status_and_import_or_acquisition_scope_required");
            }
            else checks.Add("vat_territory_unverified");
        }
        if (territory == "GB" && shipFrom is "GB" or "XI")
        {
            suppress = true;
            sources.Add(NiSource);
            checks.Add("intra_uk_vat_movement_scope_requires_separate_review");
        }
        if (accounting == "postponed_accounting")
        {
            sources.Add(PostponedSource);
            checks.Add("postponed_accounting_eligibility_registration_and_declaration_require_review");
            checks.Add("postponed_accounting_does_not_zero_vat_liability");
        }
        if (accounting == "seller_collected")
        {
            suppress = true;
            checks.Add("seller_collection_consignment_and_supply_regime_require_separate_review");
        }
        UkConditionalVatScenario? scenario = null;
        if (!invalid && treatment == "reduced_valuation_art")
        {
            var headingListed = commodityCode is { Length: >= 4 } && commodityCode[..4] is
                "4911" or "5805" or "9701" or "9702" or "9703" or "9704" or "9705" or "9706";
            if (!headingListed) checks.Add("commodity_heading_not_in_current_reduced_valuation_guidance");
            else if (proposedRate != 20m) checks.Add("reduced_valuation_not_combined_with_reduced_or_unknown_rate");
            else if (territory == "GB" && shipFrom is not ("GB" or "XI") && procedure == "free_circulation" &&
                accounting != "seller_collected" && fullBaseGbp is >= 0)
            {
                try
                {
                    var reduced = UkDutyComponents.MultiplyDivide(fullBaseGbp.Value,1m,4m);
                    var tax = reduced is { } value ? UkDutyComponents.MultiplyDivide(value,20m,100m) : null;
                    if (reduced is null || tax is null) checks.Add("conditional_vat_arithmetic_precision_lost");
                    else scenario = new(fullBaseGbp.Value,.25m,reduced.Value,20m,decimal.Round(tax.Value,2,MidpointRounding.AwayFromZero));
                }
                catch (OverflowException) { checks.Add("conditional_vat_arithmetic_overflow"); }
            }
            else checks.Add("conditional_vat_base_or_context_unavailable");
        }
        return new(treatment,accounting,invalid ? "invalid_input" : "requires_review",suppress,scenario,
            checks.Order(StringComparer.Ordinal).ToArray(),sources.Order(StringComparer.Ordinal).ToArray())
            { DeclaredEvidenceReference = claim?.EvidenceReference };
    }
}
