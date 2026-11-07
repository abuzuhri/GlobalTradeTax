namespace GlobalTradeTax.Api;

// Combines two independent review gates. Never turns proposed VAT into payable VAT.
public sealed record UkVatQuoteReviewRequest(
    UkVatClassificationInput Classification,
    UkVatInput Amounts,
    string? AmountCurrency,
    string? ShipmentId = null);

public sealed record UkVatQuoteReviewResult(
    string Status,
    string? ShipmentId,
    UkVatClassificationDecision Classification,
    UkVatReadinessResult Amounts,
    string[] UnresolvedChecks,
    decimal? PayableVatGbp,
    decimal? PayableLandedTotalGbp,
    bool VerifiedForCalculation = false)
{
    public UkVatTreatmentDecision? VatTreatmentReview { get; init; }
}

public static class UkVatQuoteReview
{
    public static UkVatQuoteReviewResult Evaluate(UkVatQuoteReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Classification);
        ArgumentNullException.ThrowIfNull(request.Amounts);
        var classification = UkVatClassificationGate.Evaluate(request.Classification);
        var amounts = UkVatReadiness.Evaluate(request.Amounts);
        var checks = new List<string>();
        checks.AddRange(classification.UnresolvedChecks);
        checks.AddRange(amounts.UnresolvedChecks);
        if (request.AmountCurrency != "GBP") checks.Add("amount_currency_must_be_GBP_no_fx_engine");
        if (request.Classification.ProposedVatRatePercent != request.Amounts.VatRatePercent)
            checks.Add("vat_rate_mismatch_between_classification_and_amounts");
        if (request.Classification.DestinationTerritory != "GB")
            checks.Add("only_great_britain_review_supported");
        if (request.ShipmentId is { Length: > 128 }) checks.Add("shipment_id_too_long");
        // Do not display GBP illustrative figures when the currency, rate, or
        // territory is inconsistent. They would otherwise look like a valid GBP quote.
        var consistentBase = request.AmountCurrency == "GBP"
            && request.Classification.DestinationTerritory == "GB"
            && request.Classification.ProposedVatRatePercent == request.Amounts.VatRatePercent
            && classification.UnresolvedChecks.All(x => x is "independent_authoritative_vat_determination_not_implemented" or
                "vat_treatment_exception_requires_review" or "vat_seller_collection_requires_review")
            && amounts.Status == "illustrative_only";
        var treatment = UkVatTreatments.Assess(request.Classification.VatTreatment,request.Classification.DestinationTerritory,
            request.Classification.ImportProcedure,request.Classification.ProposedVatRatePercent,
            consistentBase ? amounts.IllustrativeTaxableBaseGbp : null,request.Classification.ShipFrom,request.Classification.CommodityCode);
        checks.AddRange(treatment.RequiredChecks);
        var canShowIllustration = consistentBase && !treatment.SuppressOrdinaryIllustration;
        if (!canShowIllustration)
        {
            amounts = amounts with
            {
                IllustrativeTaxableBaseGbp = null,
                IllustrativeVatGbp = null
            };
            checks.Add("illustrative_amounts_suppressed_due_to_unresolved_input_consistency");
        }
        // An unverified proposal must never be promoted to a legal rate by this join.
        checks.Add("authoritative_duty_and_vat_determination_required");
        var status = amounts.Status == "invalid_input" || treatment.Status == "invalid_input" || checks.Contains("invalid_vat_dispatch_country") || checks.Contains("shipment_id_too_long")
            ? "invalid_input" : "requires_review";
        return new(status, request.ShipmentId, classification, amounts,
            checks.Distinct(StringComparer.Ordinal).ToArray(), null, null) { VatTreatmentReview = treatment };
    }
}
