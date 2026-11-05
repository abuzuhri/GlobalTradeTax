using System.Text.Json;

namespace GlobalTradeTax.Api;

// A dated upstream tree is candidate reference evidence, never tariff eligibility.
public static class UkAreaMembership
{
    public static UkAreaMembershipReport Inspect(UkReferenceResult response, string areaId, string origin, DateOnly date)
    {
        UkAreaMembershipReport Unknown(string reason) => new(areaId, origin, date, "unknown", [reason],
            response.SourceUrl, false, "Membership is indeterminate; no payable tariff conclusion.")
            { DestinationTerritory = response.DestinationTerritory };

        if (origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z')) return Unknown("invalid_origin");
        if (response.Status != "reference_only" || response.Data is not { } root ||
            !UkJsonApiContract.IsValidGeographicalArea(root, areaId)) return Unknown("area_contract_unverified");
        if (response.AsOfDate != date) return Unknown("membership_as_of_date_unverified");

        var resources = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var data = root.GetProperty("data");
        resources.Add(areaId, data);
        if (root.TryGetProperty("included", out var included))
        {
            if (included.GetArrayLength() > 1024) return Unknown("membership_resource_limit_exceeded");
            foreach (var item in included.EnumerateArray())
            {
                if (UkEvidenceJson.String(item, "type") != "geographical_area") continue;
                var id = UkEvidenceJson.String(item, "id")!;
                if (!resources.TryAdd(id, item)) return Unknown("duplicate_geographical_area_resource");
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        string[]? membershipPath = null;
        string? failure = null;
        void Walk(string id, string[] path)
        {
            if (failure is not null) return;
            if (path.Length > 16) { failure = "membership_depth_limit_exceeded"; return; }
            if (active.Contains(id)) { failure = "cyclic_geographical_area_relationship"; return; }
            if (!visited.Add(id)) return;
            if (!resources.TryGetValue(id, out var resource)) { failure = "membership_child_resource_missing:" + id; return; }
            if (!UkEvidenceJson.TryReferences(resource, "children_geographical_areas", "geographical_area", true, out var children))
            { failure = "membership_children_contract_unverified:" + id; return; }
            if (id == origin) membershipPath ??= path;
            active.Add(id);
            foreach (var child in children) Walk(child, [.. path, child]);
            active.Remove(id);
        }
        Walk(areaId, [areaId]);
        if (failure is not null) return Unknown(failure);
        if (membershipPath is null) return Unknown("no_explicit_dated_membership_proof");
        return new(areaId, origin, date, "candidate_membership_evidence", ["legal_eligibility_not_verified"],
            response.SourceUrl, false, "Dated reference membership only; exclusions, conditions and legal eligibility still require review.")
            { MembershipPath = membershipPath, DestinationTerritory = response.DestinationTerritory };
    }
}

public sealed record UkAreaMembershipReport(string AreaId, string Origin, DateOnly ImportDate,
    string EvidenceStatus, string[] UnresolvedChecks, string? SourceUrl,
    bool VerifiedForCalculation, string Warning)
{
    public string[] MembershipPath { get; init; } = [];
    public string DestinationTerritory { get; init; } = "GB";
}
