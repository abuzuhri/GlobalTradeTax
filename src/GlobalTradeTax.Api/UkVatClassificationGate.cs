namespace GlobalTradeTax.Api;

// An evidence gate, NOT an authority on VAT rates, exemptions or import procedures.
public sealed record UkVatClassificationInput(
    string? DestinationTerritory,
    string? ImportProcedure,
    string? CommodityCode,
    string? GoodsOrigin,
    DateOnly? ImportDate,
    decimal? ProposedVatRatePercent,
    string? RateSourceUrl,
    DateOnly? RateEffectiveFrom,
    DateOnly? RateEffectiveTo,
    bool CommodityClassificationVerified = false,
    bool ImportProcedureVerified = false,
    bool VatTreatmentVerified = false,
    bool ReliefsAndExemptionsVerified = false,
    bool SourceContractVerified = false,
    UkVatTreatmentClaim? VatTreatment = null,
    string? ShipFrom = null);

public sealed record UkVatClassificationDecision(
    string Status,
    decimal? ConfirmedVatRatePercent,
    decimal? ProposedVatRatePercent,
    string[] UnresolvedChecks,
    bool VerifiedForCalculation = false);

public static class UkVatClassificationGate
{
    public static UkVatClassificationDecision Evaluate(UkVatClassificationInput input)
    {
        var checks = new List<string>();
        if (input.DestinationTerritory is not ("GB" or "XI"))
            checks.Add("destination_territory_must_be_GB_or_XI");
        // Northern Ireland goods movements have distinct treatment; do not infer GB treatment.
        if (input.DestinationTerritory == "XI") checks.Add("northern_ireland_specific_rules_not_implemented");
        if (string.IsNullOrWhiteSpace(input.ImportProcedure)) checks.Add("missing_import_procedure");
        else if (input.ImportProcedure != "free_circulation") checks.Add("special_vat_import_procedure_requires_review");
        checks.AddRange(UkVatTreatments.Validate(input.VatTreatment));
        if (input.VatTreatment is { Treatment: not "ordinary" }) checks.Add("vat_treatment_exception_requires_review");
        if (input.VatTreatment?.AccountingMethod == "seller_collected") checks.Add("vat_seller_collection_requires_review");
        if (input.ShipFrom is not null && (input.ShipFrom.Length != 2 || !input.ShipFrom.All(c=>c is >= 'A' and <= 'Z')))
            checks.Add("invalid_vat_dispatch_country");
        if (input.ShipFrom is "GB" or "XI") checks.Add("intra_uk_vat_movement_requires_review");
        if (!input.ImportProcedureVerified) checks.Add("import_procedure_not_verified");
        if (string.IsNullOrWhiteSpace(input.CommodityCode) ||
            input.CommodityCode.Length is < 8 or > 10 ||
            !input.CommodityCode.All(char.IsAsciiDigit))
            checks.Add("missing_or_invalid_commodity_code");
        if (!input.CommodityClassificationVerified) checks.Add("commodity_classification_not_verified");
        if (string.IsNullOrWhiteSpace(input.GoodsOrigin) || input.GoodsOrigin.Length != 2 ||
            !input.GoodsOrigin.All(c => c is >= 'A' and <= 'Z'))
            checks.Add("missing_or_invalid_goods_origin_iso2");
        if (input.ImportDate is null) checks.Add("missing_import_date");
        if (input.ProposedVatRatePercent is null) checks.Add("missing_vat_rate");
        else if (input.ProposedVatRatePercent < 0 || input.ProposedVatRatePercent > 100)
            checks.Add("invalid_vat_rate");
        if (!input.VatTreatmentVerified) checks.Add("vat_treatment_not_verified");
        if (!input.ReliefsAndExemptionsVerified) checks.Add("reliefs_and_exemptions_not_verified");
        if (!input.SourceContractVerified) checks.Add("source_contract_not_verified");
        if (!UkDutyComponents.SafeHttps(input.RateSourceUrl))
            checks.Add("missing_https_authoritative_rate_source");
        if (input.RateEffectiveFrom is null) checks.Add("missing_rate_effective_from");
        if (input.ImportDate is not null && input.RateEffectiveFrom is not null &&
            input.ImportDate < input.RateEffectiveFrom) checks.Add("rate_not_yet_effective");
        if (input.ImportDate is not null && input.RateEffectiveTo is not null &&
            input.ImportDate > input.RateEffectiveTo) checks.Add("rate_expired");
        if (input.RateEffectiveFrom is not null && input.RateEffectiveTo is not null &&
            input.RateEffectiveTo < input.RateEffectiveFrom) checks.Add("invalid_rate_effective_window");
        // Even when every self-reported flag is true, this app has no independent
        // authoritative classification verification or UK procedure/NI rule engine.
        checks.Add("independent_authoritative_vat_determination_not_implemented");
        return new("requires_review", null, input.ProposedVatRatePercent,
            checks.Distinct(StringComparer.Ordinal).ToArray());
    }
}
