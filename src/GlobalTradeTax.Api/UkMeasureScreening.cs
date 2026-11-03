using System.Text.Json;

namespace GlobalTradeTax.Api;

// Independent C# implementation informed by the public UK Trade Tariff API design.
// This is a conservative reference screen, not a statutory applicability determination.
public static class UkMeasureScreening
{
    public static UkScreeningResult Screen(UkReferenceResult upstream, string code, string origin, DateOnly date)
    {
        if (origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z'))
            return new("invalid_origin", code, origin, date, [], false, "Use uppercase ISO 3166-1 alpha-2 origin code.");
        if (upstream.Status != "reference_only" || upstream.Data is not { } doc)
            return new(upstream.Status, code, origin, date, [], false, upstream.Warning);
        var root = doc;
        if (!root.TryGetProperty("included", out var included) || included.ValueKind != JsonValueKind.Array)
            return new("insufficient_data", code, origin, date, [], false,
                "Upstream response does not contain included measures; applicability cannot be established.");
        var items = new List<UkScreenedMeasure>();
        foreach (var entry in included.EnumerateArray())
        {
            if (items.Count >= 200) break;
            if (entry.ValueKind != JsonValueKind.Object || Read(entry, "type") != "measure") continue;
            var id = Read(entry, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var attrs = Object(entry, "attributes");
            var start = Read(attrs, "effective_start_date") ?? Read(attrs, "validity_start_date");
            var end = Read(attrs, "effective_end_date") ?? Read(attrs, "validity_end_date");
            var reasons = new List<string>();
            if (!DateOnly.TryParse(start, out var from)) reasons.Add("start_date_not_verified");
            else if (date < from) reasons.Add("not_yet_effective");
            if (end is not null && (!DateOnly.TryParse(end, out var to) || date > to)) reasons.Add("end_date_unknown_or_expired");
            // Geographical area relationships can include groups and exclusions. Do not
            // assume a measure applies just because the country matches a label.
            reasons.Add("geographical_area_and_exclusions_not_resolved");
            reasons.Add("conditions_quotas_preferences_and_additional_codes_not_resolved");
            items.Add(new(id, Read(attrs, "measure_type_id"), start, end,
                reasons.ToArray(), "requires_review"));
        }
        return new(items.Count == 0 ? "no_embedded_measures" : "reference_screened", code, origin, date,
            items.ToArray(), false,
            "No duty rate, VAT, legal eligibility, or payable amount is inferred from this response.");
    }

    private static JsonElement Object(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Object
            ? value : default;
    private static string? Read(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : null;
    }
}

public sealed record UkScreenedMeasure(string MeasureId, string? MeasureTypeId, string? EffectiveFrom,
    string? EffectiveTo, string[] ReviewReasons, string Status);
public sealed record UkScreeningResult(string Status, string CommodityCode, string Origin, DateOnly ImportDate,
    UkScreenedMeasure[] Measures, bool VerifiedForCalculation, string Warning);
