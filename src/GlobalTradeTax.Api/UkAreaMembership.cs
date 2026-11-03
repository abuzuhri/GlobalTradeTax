using System.Text.Json;

namespace GlobalTradeTax.Api;

// Evidence-only interpretation of geographical-area relationships.
// A positive membership is NOT evidence of tariff eligibility or a payable rate.
public static class UkAreaMembership
{
    public static UkAreaMembershipReport Inspect(UkReferenceResult response, string areaId, string origin, DateOnly date)
    {
        if (response.Status != "reference_only" || response.Data is not { } root || root.ValueKind != JsonValueKind.Object)
            return new(areaId, origin, date, "unknown", [], response.SourceUrl, false,
                "Reference data unavailable; no membership conclusion.");

        var notes = new List<string>();
        var matches = new List<string>();
        var candidates = new List<JsonElement>();
        if (root.TryGetProperty("included", out var included) && included.ValueKind == JsonValueKind.Array)
            candidates.AddRange(included.EnumerateArray());
        if (root.TryGetProperty("data", out var data))
        {
            if (data.ValueKind == JsonValueKind.Array) candidates.AddRange(data.EnumerateArray());
            else if (data.ValueKind == JsonValueKind.Object) candidates.Add(data);
        }

        foreach (var item in candidates)
        {
            if (item.ValueKind != JsonValueKind.Object || Read(item, "type") != "geographical_area_membership") continue;
            if (!item.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
            {
                notes.Add("membership_attributes_missing"); continue;
            }
            var start = Read(attrs, "validity_start_date");
            var end = Read(attrs, "validity_end_date");
            if (start is null || !DateOnly.TryParseExact(start, "yyyy-MM-dd", out var startDate))
            {
                notes.Add("membership_start_date_unverified"); continue;
            }
            if (date < startDate) continue;
            if (end is not null)
            {
                if (!DateOnly.TryParseExact(end, "yyyy-MM-dd", out var endDate))
                { notes.Add("membership_end_date_unverified"); continue; }
                if (date > endDate) continue;
            }
            if (!item.TryGetProperty("relationships", out var rel) || rel.ValueKind != JsonValueKind.Object)
            { notes.Add("membership_relationships_missing"); continue; }
            var member = RelationId(rel, "geographical_area");
            var parent = RelationId(rel, "geographical_area_group");
            // API representations differ. Never infer which side is country/group from IDs alone.
            if (string.Equals(member, origin, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parent, areaId, StringComparison.OrdinalIgnoreCase))
                matches.Add(Read(item, "id") ?? "unidentified");
            else notes.Add("membership_relationship_not_confirmed_for_origin_and_group");
        }
        if (matches.Count == 0) notes.Add("no_explicit_dated_membership_proof");
        // Even a matching relationship is not sufficient for legal applicability.
        return new(areaId, origin, date, matches.Count > 0 ? "candidate_membership_evidence" : "unknown",
            notes.Distinct().ToArray(), response.SourceUrl, false,
            "Reference-only. Verify upstream schema, exclusions, conditions, quotas and legal applicability before calculating duty.");
    }

    private static string? RelationId(JsonElement rel, string name)
    {
        if (!rel.TryGetProperty(name, out var relationship) || relationship.ValueKind != JsonValueKind.Object ||
            !relationship.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return null;
        return Read(data, "id");
    }
    private static string? Read(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}

public sealed record UkAreaMembershipReport(string AreaId, string Origin, DateOnly ImportDate,
    string EvidenceStatus, string[] UnresolvedChecks, string? SourceUrl,
    bool VerifiedForCalculation, string Warning);
