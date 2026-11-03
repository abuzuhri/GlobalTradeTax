using System.Text.Json;

namespace GlobalTradeTax.Api;

// Conservative evidence resolver. Does not infer group membership from labels or missing relationships.
public static class UkGeographyResolver
{
    public static UkGeographyReport Analyze(UkReferenceResult upstream, string commodityCode, string origin, DateOnly date)
    {
        var evidence = UkMeasureEvidence.Analyze(upstream, commodityCode, origin, date);
        if (upstream.Data is not { } root || root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("included", out var included) || included.ValueKind != JsonValueKind.Array)
            return new(evidence.Status, commodityCode, origin, date, [], false, "No embedded geographical evidence; duty remains unknown.");

        var areas = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in included.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Str(item, "type") != "geographical_area") continue;
            var id = Str(item, "id");
            if (!string.IsNullOrEmpty(id)) areas.TryAdd(id, item);
        }

        var results = new List<UkGeographyMeasure>();
        foreach (var measure in evidence.Measures)
        {
            var checks = new List<string>();
            var matches = new List<string>();
            foreach (var areaId in measure.GeographicalAreaReferences)
            {
                if (string.Equals(areaId, origin, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(areaId);
                    checks.Add("direct_country_reference_only");
                    continue;
                }
                if (!areas.TryGetValue(areaId, out var area))
                {
                    checks.Add("area_not_embedded:" + areaId);
                    continue;
                }
                var rel = Object(area, "relationships");
                var members = Object(rel, "geographical_area_memberships");
                if (members.ValueKind != JsonValueKind.Object ||
                    !members.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    checks.Add("group_memberships_not_embedded:" + areaId);
                    continue;
                }
                // Membership relationship IDs may be surrogate identifiers rather than country codes.
                // We intentionally do not treat them as country membership proof.
                checks.Add("group_membership_details_require_authoritative_lookup:" + areaId);
            }
            if (measure.ExclusionReferences > 0) checks.Add("geographical_exclusions_require_lookup");
            if (measure.ConditionReferences > 0) checks.Add("measure_conditions_require_review");
            if (measure.QuotaReferences > 0) checks.Add("quota_eligibility_requires_review");
            if (measure.AdditionalCodeReferences > 0) checks.Add("additional_codes_require_review");
            if (matches.Count == 0) checks.Add("origin_membership_not_confirmed");
            results.Add(new UkGeographyMeasure(measure.MeasureId, matches.ToArray(), checks.Distinct().ToArray(), "requires_review"));
        }
        return new(evidence.Status, commodityCode, origin, date, results.ToArray(), false,
            "Reference screening only. Geography evidence never authorizes a payable tariff calculation.");
    }

    private static JsonElement Object(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;
    private static string? Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

public sealed record UkGeographyMeasure(string MeasureId, string[] DirectCountryReferences,
    string[] UnresolvedChecks, string Status);
public sealed record UkGeographyReport(string Status, string CommodityCode, string Origin, DateOnly ImportDate,
    UkGeographyMeasure[] Measures, bool VerifiedForCalculation, string Warning);
