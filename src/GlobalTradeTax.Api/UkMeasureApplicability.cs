using System.Text.Json;

namespace GlobalTradeTax.Api;

// Screens reference candidates only. It neither selects a duty rate nor resolves legal conditions.
public static class UkMeasureApplicability
{
    public static UkMeasureApplicabilityEvidence Analyze(UkReferenceResult upstream, string code, string measureId,
        string origin, DateOnly date, IReadOnlyDictionary<string, UkAreaMembershipReport> areas)
    {
        UkMeasureApplicabilityEvidence Unknown(string reason) =>
            new("indeterminate", "unknown", "unknown", "unknown", "unknown", [reason], false);
        if (origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z')) return Unknown("invalid_origin");
        if (upstream.Status != "reference_only" || upstream.AsOfDate != date || upstream.Data is not { } root ||
            !UkJsonApiContract.IsValidCommodity(root, code)) return Unknown("dated_commodity_contract_unverified");
        if (!root.TryGetProperty("included", out var included)) return Unknown("measure_resource_missing");
        var matches = included.EnumerateArray().Where(item => UkEvidenceJson.String(item, "type") == "measure" &&
            UkEvidenceJson.String(item, "id") == measureId).ToArray();
        if (matches.Length != 1) return Unknown("measure_resource_missing_or_duplicate");
        var raw = matches[0];
        var checks = new List<string>();
        var data = root.GetProperty("data");
        var attributes = UkEvidenceJson.Object(raw, "attributes");
        var direction = "unknown";
        if (!UkEvidenceJson.TryReferences(data, "import_measures", "measure", true, out var imports))
            checks.Add("commodity_import_measure_linkage_unverified");
        else if (!imports.Contains(measureId, StringComparer.Ordinal))
        {
            direction = "other_measure";
            checks.Add("not_linked_as_commodity_import_measure");
        }
        else if (attributes.ValueKind == JsonValueKind.Object &&
            attributes.TryGetProperty("import", out var import) && import.ValueKind == JsonValueKind.True &&
            attributes.TryGetProperty("export", out var export) && export.ValueKind is JsonValueKind.False or JsonValueKind.True)
        {
            if (export.ValueKind == JsonValueKind.False) direction = "import";
            else if (UkEvidenceJson.TryReferences(data, "export_measures", "measure", true, out var exports) &&
                exports.Contains(measureId, StringComparer.Ordinal)) direction = "import_and_export";
            else checks.Add("export_direction_linkage_missing_or_conflicting");
        }
        else checks.Add("import_direction_attributes_missing_or_conflicting");

        var window = UkEvidenceJson.EffectiveWindow(attributes, date);
        if (window == "unknown") checks.Add("effective_window_unverified");
        if (window == "outside_window") checks.Add("outside_effective_window");

        bool HasMembership(string id) => areas.TryGetValue(id, out var area) && area.AreaId == id &&
            area.Origin == origin && area.ImportDate == date && area.DestinationTerritory == upstream.DestinationTerritory &&
            area.EvidenceStatus == "candidate_membership_evidence";

        var geography = "unknown";
        if (UkEvidenceJson.TryReferences(raw, "geographical_area", "geographical_area", false, out var references))
        {
            if (references[0] == origin) geography = "direct_country_reference";
            else if (HasMembership(references[0])) geography = "candidate_group_membership";
            else checks.Add("origin_membership_indeterminate:" + references[0]);
        }
        else checks.Add("measure_geographical_relationship_unverified");

        var exclusions = ReadExclusions(raw, out var exclusionsComplete);
        var exclusion = "unknown";
        if (exclusions.Any(id => id == origin || HasMembership(id)))
        {
            exclusion = "origin_listed";
            checks.Add("origin_has_explicit_exclusion_evidence");
        }
        else if (exclusionsComplete && exclusions.Length == 0) exclusion = "empty_exclusion_list";
        else checks.Add(exclusionsComplete ? "other_exclusion_memberships_indeterminate" : "exclusion_relationship_unverified");

        var status = direction == "other_measure" || window == "outside_window" || exclusion == "origin_listed"
            ? "not_candidate"
            : direction is "import" or "import_and_export" && window == "within_window" && geography != "unknown" && exclusion == "empty_exclusion_list"
                ? "candidate_for_review" : "indeterminate";
        checks.Add("conditions_certificates_quotas_preferences_and_legal_eligibility_require_review");
        return new(status, direction, window, geography, exclusion,
            checks.Distinct().Order(StringComparer.Ordinal).ToArray(), false);
    }

    // The declarable serializer uses excluded_countries; the general serializer uses
    // excluded_geographical_areas. Neither missing nor malformed linkage means an empty list.
    internal static string[] ReadExclusions(JsonElement raw, out bool complete)
    {
        var relations = UkEvidenceJson.Object(raw, "relationships");
        var found = false;
        complete = true;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "excluded_countries", "excluded_geographical_areas" })
        {
            if (relations.ValueKind != JsonValueKind.Object || !relations.TryGetProperty(name, out _)) continue;
            found = true;
            if (!UkEvidenceJson.TryReferences(raw, name, "geographical_area", true, out var references)) complete = false;
            else ids.UnionWith(references);
        }
        complete &= found;
        return ids.Order(StringComparer.Ordinal).ToArray();
    }
}

public sealed record UkMeasureApplicabilityEvidence(string Status, string Direction, string EffectiveWindow,
    string GeographyEvidence, string ExclusionEvidence, string[] EvidenceChecks, bool VerifiedForCalculation);
