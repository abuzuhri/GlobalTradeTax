using System.Text.Json;

namespace GlobalTradeTax.Api;

// Evidence-only analysis. A matching geographical code does NOT prove legal eligibility.
public static class UkMeasureEvidence
{
    public static UkEvidenceReport Analyze(UkReferenceResult upstream, string code, string origin, DateOnly date)
        => AnalyzeCore(upstream, code, origin, date) with { DestinationTerritory = upstream.DestinationTerritory, ResponseSha256 = upstream.ResponseSha256 };

    private static UkEvidenceReport AnalyzeCore(UkReferenceResult upstream, string code, string origin, DateOnly date)
    {
        var screened = UkMeasureScreening.Screen(upstream, code, origin, date);
        if (upstream.Status != "reference_only" || upstream.Data is not { } root ||
            root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("included", out var included) ||
            included.ValueKind != JsonValueKind.Array)
            return new(screened.Status, code, origin, date, [], false, "No verified tariff calculation is possible.");

        var lookup = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in included.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var type = Get(entry, "type"); var id = Get(entry, "id");
            if (type is not null && id is not null) lookup.TryAdd(type + ":" + id, entry);
        }
        var details = new List<UkMeasureEvidenceItem>();
        foreach (var measure in screened.Measures)
        {
            if (!lookup.TryGetValue("measure:" + measure.MeasureId, out var raw)) continue;
            var attrs = Obj(raw, "attributes");
            var relations = Obj(raw, "relationships");
            var geoRefs = Refs(relations, "geographical_area");
            var conditions = Refs(relations, "measure_conditions");
            var exclusions = UkMeasureApplicability.ReadExclusions(raw, out var exclusionsComplete);
            var additional = Refs(relations, "additional_code");
            var quota = Refs(relations, "order_number");
            var rate = Get(attrs, "duty_expression") ?? Get(attrs, "duty_expression_description");
            var evidence = new List<string>();
            var geo = "unknown";
            if (geoRefs.Count == 0) evidence.Add("geographical_area_relationship_missing");
            else
            {
                // Country-level references can be identified but geographical groups cannot
                // be resolved without authoritative membership and exclusion data.
                var ids = geoRefs.Select(x => x.Id).ToArray();
                if (ids.Contains(origin, StringComparer.OrdinalIgnoreCase))
                {
                    geo = "direct_country_reference";
                    evidence.Add("country_reference_found_not_proof_of_eligibility");
                }
                else if (ids.All(id => id.Length == 2 && id.All(char.IsLetter)))
                {
                    geo = "different_direct_country_reference";
                    evidence.Add("different_country_reference_not_proof_of_inapplicability");
                }
                else evidence.Add("geographical_group_membership_unresolved");
            }
            if (exclusions.Length > 0) evidence.Add("geographical_exclusions_unresolved");
            if (!exclusionsComplete) evidence.Add("exclusion_relationship_unverified");
            if (conditions.Count > 0) evidence.Add("measure_conditions_unresolved");
            if (additional.Count > 0) evidence.Add("additional_codes_unresolved");
            if (quota.Count > 0) evidence.Add("quota_balance_and_eligibility_unresolved");
            if (rate is not null) evidence.Add("duty_expression_unverified_and_not_calculated");
            evidence.AddRange(measure.ReviewReasons);
            details.Add(new(measure.MeasureId, geo, geoRefs.Select(x => x.Id).ToArray(),
                conditions.Count, exclusions.Length, additional.Count, quota.Count,
                rate, evidence.Distinct().ToArray(), "requires_review"));
        }
        return new(screened.Status, code, origin, date, details.ToArray(), false,
            "Reference-only evidence; no measure is legally confirmed applicable, and no duty/VAT is calculated.");
    }

    private static JsonElement Obj(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object ? v : default;
    private static string? Get(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : null;
    private static List<(string Type, string Id)> Refs(JsonElement relations, string key)
    {
        var result = new List<(string, string)>();
        var relation = Obj(relations, key);
        if (relation.ValueKind != JsonValueKind.Object || !relation.TryGetProperty("data", out var data)) return result;
        void Add(JsonElement item)
        {
            var type = Get(item, "type"); var id = Get(item, "id");
            if (type is not null && id is not null && result.Count < 100) result.Add((type, id));
        }
        if (data.ValueKind == JsonValueKind.Array) foreach (var item in data.EnumerateArray()) Add(item);
        else if (data.ValueKind == JsonValueKind.Object) Add(data);
        return result;
    }
}

public sealed record UkMeasureEvidenceItem(string MeasureId, string GeographyEvidence,
    string[] GeographicalAreaReferences, int ConditionReferences, int ExclusionReferences,
    int AdditionalCodeReferences, int QuotaReferences, string? RawDutyExpression,
    string[] UnresolvedChecks, string Status);
public sealed record UkEvidenceReport(string Status, string CommodityCode, string Origin,
    DateOnly ImportDate, UkMeasureEvidenceItem[] Measures, bool VerifiedForCalculation, string Warning)
{
    public string DestinationTerritory { get; init; } = "GB";
    public string? ResponseSha256 { get; init; }
}
