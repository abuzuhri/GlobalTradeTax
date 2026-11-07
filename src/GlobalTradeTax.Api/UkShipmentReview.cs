using System.Security.Cryptography;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public sealed record UkImportLineInput(string? LineId, string? CommodityCode, string? GoodsOrigin, decimal? CustomsValue,
    decimal? NetMassKg = null, decimal? ItemCount = null, string? DutyMeasureId = null, string? VatMeasureId = null,
    string[]? DeclaredDocuments = null, UkVatTreatmentClaim? VatTreatment = null);
public sealed record UkImportCost(string? CostId, decimal? Amount, bool? IncludedInCustomsValue,
    string[]? LineIds = null, string? Category = "incidental");
public sealed record UkShipmentReviewRequest(string? DestinationTerritory, DateOnly? ImportDate, string? ShipFrom,
    string? ImportProcedure, string? Currency, UkImportLineInput[]? Lines, UkImportCost[]? Costs,
    bool UsePublishedFx = false, decimal? IntrinsicValueGbp = null, string? NorthernIrelandRisk = null,
    string? TransactionKind = null, string? ShipmentId = null);
public sealed record UkDocumentDeclaration(string Code, string Status);
public sealed record UkDutyScenario(string MeasureId, string? MeasureTypeId, string CandidateStatus, UkDutyArithmetic Arithmetic);
public sealed record UkShipmentLineReview(string LineId, string CommodityCode, string GoodsOrigin,
    UkAssessmentReport Assessment, decimal? DeclaredCustomsValueGbp, decimal? AdditionalVatCostsGbp,
    UkDutyScenario[] DutyScenarios, decimal? IllustrativeDutyGbp, decimal? ProposedVatRatePercent,
    decimal? IllustrativeVatBaseGbp, decimal? IllustrativeVatGbp, UkDocumentDeclaration[] DocumentDeclarations, string[] Checks)
{
    public bool VerifiedForCalculation => false;
    public UkVatTreatmentDecision? VatTreatmentReview { get; init; }
}
public sealed record UkIllustrativeTotals(decimal CustomsValueGbp, decimal ImportDutyGbp,
    decimal AdditionalVatCostsGbp, decimal VatBaseGbp, decimal ImportVatGbp, decimal ValuePlusTaxesGbp);
public sealed record UkShipmentReviewResult(string Status, string? ShipmentId, string? DestinationTerritory,
    DateOnly? ImportDate, UkShipmentLineReview[] Lines, UkFxRate[] FxRates, UkIllustrativeTotals? IllustrativeTotals,
    string[] Checks, string EvidenceSha256)
{
    public string SchemaVersion => "uk-shipment-review/v2";
    public bool VerifiedForCalculation => false;
    public bool ApprovedForPayment => false;
    public decimal? PayableDutyGbp => null;
    public decimal? PayableVatGbp => null;
    public decimal? PayableLandedTotalGbp => null;
}

public sealed class UkShipmentReview(UkImportAssessment assessment, UkTariffReferenceClient references)
{
    public async Task<UkShipmentReviewResult> ReviewAsync(UkShipmentReviewRequest request, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Length > 0) return Finish("invalid_input", request, [], [], null, errors);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(40));
        var lookups = new Dictionary<(string Code, string Origin), UkAssessmentReport>();
        var checks = new List<string>();
        foreach (var line in request.Lines!)
        {
            var key = (line.CommodityCode!, line.GoodsOrigin!);
            if (lookups.ContainsKey(key)) continue;
            try { lookups[key] = await assessment.AssessAsync(key.Item1, key.Item2, request.ImportDate!.Value, request.DestinationTerritory!, budget.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lookups[key] = new("upstream_error", key.Item1, key.Item2, request.ImportDate!.Value, [], [], null, null, null,
                    ["shipment_reference_time_budget_exceeded"], false) { DestinationTerritory = request.DestinationTerritory! };
            }
        }
        var rates = new List<UkFxRate>();
        if (request.UsePublishedFx)
        {
            try
            {
                if (request.Currency != "GBP")
                {
                    var source = await references.GetMonthlyExchangeRatesAsync(request.ImportDate!.Value, budget.Token);
                    var parsed = UkExchangeRates.Monthly(source, request.ImportDate.Value);
                    rates.AddRange(parsed.Rates.Where(r => r.Currency == request.Currency)); checks.AddRange(parsed.Checks);
                }
                if (request.DestinationTerritory == "XI" && lookups.Values.SelectMany(a => a.Measures)
                    .Any(m => m.Rule?.Components.Any(c => c.Currency == "EUR") == true))
                {
                    var source = await references.GetTariffExchangeRatesAsync(request.ImportDate!.Value, budget.Token);
                    var parsed = UkExchangeRates.Tariff(source, request.ImportDate.Value);
                    rates.AddRange(parsed.Rates); checks.AddRange(parsed.Checks);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { checks.Add("shipment_fx_time_budget_exceeded"); }
        }
        return Compose(request, lookups, rates, checks);
    }

    public static string[] Validate(UkShipmentReviewRequest request)
    {
        var errors = new HashSet<string>(StringComparer.Ordinal);
        if (request.DestinationTerritory is not ("GB" or "XI")) errors.Add("destination_territory_must_be_GB_or_XI");
        if (request.ImportDate is null || request.ImportDate > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(20)) errors.Add("valid_import_date_required");
        if (!Country(request.ShipFrom) || request.ShipFrom == request.DestinationTerritory) errors.Add("international_ship_from_required");
        if (request.Currency is not { Length: 3 } || !request.Currency.All(c => c is >= 'A' and <= 'Z')) errors.Add("uppercase_currency_required");
        if (!Tag(request.ImportProcedure, 64)) errors.Add("import_procedure_required");
        if (request.ShipmentId is not null && !Tag(request.ShipmentId, 128)) errors.Add("invalid_shipment_id");
        if (request.IntrinsicValueGbp is < 0 or > 1_000_000_000_000m) errors.Add("invalid_intrinsic_value");
        if (request.NorthernIrelandRisk is not (null or "unknown" or "at_risk" or "not_at_risk")) errors.Add("invalid_ni_risk_declaration");
        if (request.TransactionKind is not (null or "commercial" or "gift" or "unknown")) errors.Add("invalid_transaction_kind");
        var lines = request.Lines;
        if (lines is null || lines.Length is < 1 or > 20 || lines.Any(x => x is null)) errors.Add("one_to_twenty_complete_lines_required");
        else
        {
            if (lines.Select(x => x.LineId).Distinct(StringComparer.Ordinal).Count() != lines.Length) errors.Add("duplicate_line_id");
            foreach (var line in lines)
            {
                errors.UnionWith(UkVatTreatments.Validate(line.VatTreatment));
                if (!Tag(line.LineId, 64) || line.CommodityCode is not { Length: 10 } || !line.CommodityCode.All(char.IsAsciiDigit) || !Country(line.GoodsOrigin))
                    errors.Add("invalid_line_identity_classification_or_origin");
                if (line.CustomsValue is null or < 0 or > 1_000_000_000_000m || line.NetMassKg is <= 0 or > 1_000_000_000m ||
                    line.ItemCount is <= 0 or > 1_000_000_000_000m || line.ItemCount is { } count && count != decimal.Truncate(count)) errors.Add("invalid_line_value_or_quantity");
                if (line.DutyMeasureId is not null && !Tag(line.DutyMeasureId, 64) || line.VatMeasureId is not null && !Tag(line.VatMeasureId, 64)) errors.Add("invalid_measure_selection");
                if (line.DeclaredDocuments is { } documents && (documents.Length > 100 || documents.Any(x => x is null || x.Length is < 4 or > 8 || !x.All(char.IsAsciiLetterOrDigit)) ||
                    documents.Distinct(StringComparer.Ordinal).Count() != documents.Length)) errors.Add("invalid_document_declarations");
            }
        }
        if (request.Costs is null || request.Costs.Length > 100 || request.Costs.Any(x => x is null)) errors.Add("explicit_cost_list_required_maximum_100");
        else
        {
            if (request.Costs.Select(x => x.CostId).Distinct(StringComparer.Ordinal).Count() != request.Costs.Length) errors.Add("duplicate_cost_id");
            foreach (var cost in request.Costs)
            {
                if (!Tag(cost.CostId, 64) || cost.Amount is null or < 0 or > 1_000_000_000_000m || !Tag(cost.Category, 64)) errors.Add("invalid_cost");
                if (cost.LineIds is { } ids && (ids.Length is < 1 or > 20 || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
                    ids.Any(id => lines is null || !lines.Any(line => line?.LineId == id)))) errors.Add("invalid_cost_line_allocation");
            }
        }
        return errors.Order(StringComparer.Ordinal).ToArray();
    }

    // Public pure join for replay/testing; source assessments must be bound to line/date/territory.
    public static UkShipmentReviewResult Compose(UkShipmentReviewRequest request,
        IReadOnlyDictionary<(string Code, string Origin), UkAssessmentReport> assessments, IReadOnlyList<UkFxRate> fx, IEnumerable<string>? extraChecks = null)
    {
        var invalid = Validate(request);
        if (invalid.Length > 0) return Finish("invalid_input", request, [], [], null, invalid);
        var date = request.ImportDate!.Value;
        var checks = new HashSet<string>(extraChecks ?? [], StringComparer.Ordinal);
        checks.UnionWith(["independent_classification_valuation_and_legal_eligibility_required", "illustrative_rounding_not_a_statutory_determination",
            "declared_documents_do_not_establish_authenticity_or_entitlement", "vat_collection_reliefs_and_import_procedure_require_review"]);
        if (request.DestinationTerritory == "XI") checks.Add("northern_ireland_risk_union_tariff_ukims_and_vat_regime_require_review");
        if (request.ShipFrom is "GB" or "XI") checks.Add("intra_uk_movement_rules_require_separate_review");
        if (request.ImportProcedure != "free_circulation") checks.Add("special_or_unknown_import_procedure_no_shipment_illustration");
        if (request.TransactionKind != "commercial" || request.IntrinsicValueGbp is null || request.IntrinsicValueGbp <= 135m)
            checks.Add("gift_low_value_or_unknown_consignment_regime_no_shipment_illustration");
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        bool fxOk = UkDutyComponents.TryFactor(fx, request.Currency!, "invoice_customs_value", date, out var valueFactor);
        if (!fxOk) checks.Add("dated_invoice_currency_conversion_missing");
        try
        {
            if (fxOk) foreach (var line in request.Lines!)
            {
                var value = UkDutyComponents.MultiplyDivide(line.CustomsValue!.Value,valueFactor,1m);
                if (value is null)
                {
                    fxOk = false; values.Clear(); checks.Add("invoice_currency_conversion_precision_lost"); break;
                }
                values[line.LineId!] = value.Value;
            }
        }
        catch (OverflowException) { fxOk = false; values.Clear(); checks.Add("invoice_currency_conversion_overflow"); }
        var allocations = request.Lines!.ToDictionary(x => x.LineId!, _ => 0m, StringComparer.Ordinal);
        var costsOk = fxOk;
        foreach (var cost in request.Costs!.OrderBy(x => x.CostId, StringComparer.Ordinal))
        {
            if (cost.IncludedInCustomsValue is null) { costsOk = false; checks.Add("cost_inclusion_unknown:" + cost.CostId); continue; }
            if (cost.Category is not ("incidental" or "transport" or "insurance" or "packing" or "commission" or "excise" or "levy"))
            { costsOk = false; checks.Add("unsupported_cost_scope:" + cost.CostId); continue; }
            if (cost.IncludedInCustomsValue == true) continue;
            if (!fxOk) continue;
            try
            {
                var weights = values.Where(x => cost.LineIds is null || cost.LineIds.Contains(x.Key, StringComparer.Ordinal)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
                var convertedCost = UkDutyComponents.MultiplyDivide(cost.Amount!.Value,valueFactor,1m);
                if (convertedCost is null) { costsOk = false; checks.Add("cost_conversion_precision_lost:"+cost.CostId); continue; }
                var split = UkMoneyAllocation.Allocate(convertedCost.Value, weights);
                if (split is null) { costsOk = false; checks.Add("cost_allocation_indeterminate:" + cost.CostId); continue; }
                foreach (var item in split) allocations[item.Key] = checked(allocations[item.Key] + item.Value);
            }
            catch (OverflowException) { costsOk = false; checks.Add("cost_allocation_overflow:" + cost.CostId); }
        }
        var results = new List<UkShipmentLineReview>();
        var contextSupportsIllustration = request.DestinationTerritory == "GB" && request.ShipFrom is not ("GB" or "XI") &&
            request.ImportProcedure == "free_circulation" && request.TransactionKind == "commercial" && request.IntrinsicValueGbp > 135m;
        foreach (var line in request.Lines!.OrderBy(x => x.LineId, StringComparer.Ordinal))
        {
            var local = new HashSet<string>(StringComparer.Ordinal);
            if (!assessments.TryGetValue((line.CommodityCode!, line.GoodsOrigin!), out var report) || report.CommodityCode != line.CommodityCode ||
                report.Origin != line.GoodsOrigin || report.ImportDate != date || report.DestinationTerritory != request.DestinationTerritory)
            {
                report = new("insufficient_data", line.CommodityCode!, line.GoodsOrigin!, date, [], [], null, null, null, ["line_reference_context_unverified"], false)
                    { DestinationTerritory = request.DestinationTerritory! };
                local.Add("line_reference_context_unverified");
            }
            if (report.Status != "requires_review") local.Add("line_reference_unavailable:" + report.Status);
            var scenarios = report.Measures.Where(m => m.Rule is { Vat: false, Excise: false, ImportLinked: true } && m.Rule.MeasureTypeId != "109")
                .Select(m => new UkDutyScenario(m.MeasureId, m.Rule!.MeasureTypeId, m.Applicability?.Status ?? "indeterminate",
                    m.Rule.Complete && m.Applicability?.Status == "candidate_for_review"
                        ? UkDutyComponents.Evaluate(m.Rule.Components, new(fxOk ? values[line.LineId!] : null, line.NetMassKg, line.ItemCount), date, fx)
                        : new("requires_review", null, [], ["measure_graph_origin_or_effective_date_not_confirmed"]))).ToArray();
            decimal? duty = null;
            var selected = report.Measures.Where(m => m.MeasureId == line.DutyMeasureId).ToArray();
            if (line.DutyMeasureId is null) local.Add("explicit_duty_measure_selection_required_no_cheapest_rate_inferred");
            else if (selected.Length != 1 || selected[0].Rule is not { Complete: true, Vat: false, Excise: false, ImportLinked: true } rule ||
                selected[0].Applicability?.Status != "candidate_for_review" || rule.MeasureTypeId == "109") local.Add("selected_duty_measure_unverified");
            else if (rule.QuotaIds.Length > 0 || rule.AdditionalCodeIds.Length > 0 || rule.Conditions.Length > 0 || rule.PreferenceCode is not (null or "100"))
                local.Add("selected_duty_quota_preference_conditions_or_additional_code_entitlement_unverified");
            else if (fxOk && contextSupportsIllustration) duty = scenarios.SingleOrDefault(x => x.MeasureId == line.DutyMeasureId)?.Arithmetic.IllustrativeGbp;
            decimal? vatRate = null;
            var vatTreatmentCanIllustrate = false;
            var vat = report.Measures.Where(m => m.MeasureId == line.VatMeasureId).ToArray();
            if (line.VatMeasureId is null) local.Add("explicit_vat_measure_selection_required_no_rate_assumed");
            else if (vat.Length == 1 && vat[0].Rule is { Complete: true, Vat: true, Excise: false, ImportLinked: true } vr && vr.Components.Length == 1 &&
                vr.Components[0] is { Complete: true, ExpressionCode: "01", Currency: null, Unit: null, Qualifier: null, Amount: >= 0 and <= 100 } component &&
                vat[0].Applicability?.Status == "candidate_for_review")
            {
                vatRate = component.Amount;
                vatTreatmentCanIllustrate = vr.Conditions.Length == 0 && vr.QuotaIds.Length == 0 && vr.AdditionalCodeIds.Length == 0 &&
                    vr.FootnoteIds.Length == 0 && vr.PreferenceCode is null or "100";
                if (!vatTreatmentCanIllustrate) local.Add("vat_conditions_additional_code_or_footnote_treatment_requires_review");
            }
            else local.Add("selected_vat_measure_or_treatment_unverified");
            decimal? fullBase = null, vatBase = null, vatAmount = null;
            try
            {
                if (contextSupportsIllustration && fxOk && costsOk && duty is not null)
                    fullBase = checked(values[line.LineId!] + duty.Value + allocations[line.LineId!]);
            }
            catch (OverflowException) { local.Add("vat_arithmetic_overflow"); }
            var treatment = UkVatTreatments.Assess(line.VatTreatment,request.DestinationTerritory,request.ImportProcedure,
                vatRate,fullBase,request.ShipFrom,line.CommodityCode);
            local.UnionWith(treatment.RequiredChecks);
            try
            {
                if (fullBase is not null && vatRate is not null && vatTreatmentCanIllustrate && !treatment.SuppressOrdinaryIllustration)
                {
                    var amount = UkDutyComponents.MultiplyDivide(fullBase.Value,vatRate.Value,100m);
                    if (amount is null) local.Add("vat_arithmetic_precision_lost");
                    else { vatBase = fullBase; vatAmount = decimal.Round(amount.Value,2,MidpointRounding.AwayFromZero); }
                }
            }
            catch (OverflowException) { vatBase = vatAmount = null; local.Add("vat_arithmetic_overflow"); }
            if (report.Measures.Any(m => m.Rule?.Conditions.Length > 0 || m.Rule?.QuotaIds.Length > 0)) local.Add("import_controls_certificates_quotas_and_condition_alternatives_require_review");
            var references = report.Measures.SelectMany(x => x.Rule?.Conditions ?? []).Select(x => x.DocumentCode).Where(x => x is not null).ToHashSet(StringComparer.Ordinal);
            var documents = (line.DeclaredDocuments ?? []).Order(StringComparer.Ordinal).Select(code => new UkDocumentDeclaration(code,
                references.Contains(code) ? "declared_reference_match_not_verified" : "declared_document_not_matched_not_verified")).ToArray();
            results.Add(new(line.LineId!, line.CommodityCode!, line.GoodsOrigin!, report, fxOk ? values[line.LineId!] : null,
                costsOk ? allocations[line.LineId!] : null, scenarios, duty, vatRate, vatBase, vatAmount, documents, local.Order(StringComparer.Ordinal).ToArray())
                { VatTreatmentReview = treatment });
        }
        UkIllustrativeTotals? totals = null;
        if (contextSupportsIllustration && results.All(x => x.IllustrativeVatGbp is not null))
        {
            try
            {
                var customs = results.Sum(x => x.DeclaredCustomsValueGbp!.Value);
                var duty = results.Sum(x => x.IllustrativeDutyGbp!.Value);
                var costs = results.Sum(x => x.AdditionalVatCostsGbp!.Value);
                var bases = results.Sum(x => x.IllustrativeVatBaseGbp!.Value);
                var vat = results.Sum(x => x.IllustrativeVatGbp!.Value);
                totals = new(customs, duty, costs, bases, vat, checked(customs + duty + costs + vat));
            }
            catch (OverflowException) { checks.Add("shipment_arithmetic_overflow"); }
        }
        return Finish("requires_review", request, results.ToArray(), fx.ToArray(), totals, checks.Order(StringComparer.Ordinal).ToArray());
    }

    private static UkShipmentReviewResult Finish(string status, UkShipmentReviewRequest request, UkShipmentLineReview[] lines,
        UkFxRate[] rates, UkIllustrativeTotals? totals, string[] checks)
    {
        var payload = new { Schema = "uk-shipment-review/v2", Request = request with
            { Lines = request.Lines?.OrderBy(x => x?.LineId, StringComparer.Ordinal).Select(x => x is null ? null! : x with
                { DeclaredDocuments = x.DeclaredDocuments?.Order(StringComparer.Ordinal).ToArray() }).ToArray(),
                Costs = request.Costs?.OrderBy(x => x?.CostId, StringComparer.Ordinal).Select(x => x is null ? null! : x with
                { LineIds = x.LineIds?.Order(StringComparer.Ordinal).ToArray() }).ToArray() },
            Status = status, Lines = lines, Rates = rates.OrderBy(r => r.Purpose, StringComparer.Ordinal).ThenBy(r => r.Currency, StringComparer.Ordinal).ToArray(),
            Totals = totals, Checks = checks.Order(StringComparer.Ordinal).ToArray() };
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload))).ToLowerInvariant();
        return new(status, request.ShipmentId, request.DestinationTerritory, request.ImportDate, lines, rates, totals, checks, hash);
    }

    private static bool Country(string? value) => value is { Length: 2 } && value.All(c => c is >= 'A' and <= 'Z');
    private static bool Tag(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
}
