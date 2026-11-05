using System.Text.Json;

namespace GlobalTradeTax.Api;

// A deliberately lossy, non-financial projection of the UK API's JSON:API document.
// The upstream schema may evolve: missing fields remain null, never inferred.
public static class UkTariffNormalizer
{
    public static UkNormalizedCommodity Normalize(string requestedCode, UkReferenceResult result)
        => NormalizeCore(requestedCode, result) with { DestinationTerritory = result.DestinationTerritory, ResponseSha256 = result.ResponseSha256 };

    private static UkNormalizedCommodity NormalizeCore(string requestedCode, UkReferenceResult result)
    {
        if (result.Status != "reference_only" || result.Data is not { } document)
            return new(requestedCode, result.Status, null, null, null, [], result.SourceUrl,
                "UK Trade Tariff Backend", false, result.Warning);

        JsonElement primary = default;
        if (document.ValueKind == JsonValueKind.Object && document.TryGetProperty("data", out var data))
            primary = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().FirstOrDefault() : data;
        if (primary.ValueKind != JsonValueKind.Object)
            return new(requestedCode, "unsupported_schema", null, null, null, [], result.SourceUrl,
                "UK Trade Tariff Backend", false, "Upstream data is not a supported JSON:API commodity object.");

        var attributes = GetObject(primary, "attributes");
        var code = ReadString(attributes, "goods_nomenclature_item_id") ?? ReadString(primary, "id");
        var description = ReadString(attributes, "description") ?? ReadString(attributes, "formatted_description");
        var validFrom = ReadString(attributes, "validity_start_date");
        var relationships = GetObject(primary, "relationships");
        var measureReferences = new List<UkMeasureReference>();
        if (relationships.ValueKind == JsonValueKind.Object && relationships.TryGetProperty("import_trade_summary", out var summary))
            AddReferences(summary, "import_trade_summary", measureReferences);
        if (relationships.ValueKind == JsonValueKind.Object && relationships.TryGetProperty("import_measures", out var measures))
            AddReferences(measures, "import_measures", measureReferences);
        // No rates are parsed here. Measures require geography, origin, date, conditions,
        // suspensions, quotas and legal validation before they can inform a duty quote.
        return new(requestedCode, "reference_only", code, description, validFrom,
            measureReferences.Take(100).ToArray(), result.SourceUrl, "UK Trade Tariff Backend", false,
            "Reference only. Duty, VAT and landed-cost amounts are not calculated or verified.");
    }

    private static void AddReferences(JsonElement relation, string kind, List<UkMeasureReference> destination)
    {
        if (relation.ValueKind != JsonValueKind.Object || !relation.TryGetProperty("data", out var data)) return;
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray().Take(100)) AddReference(item, kind, destination);
        }
        else AddReference(data, kind, destination);
    }

    private static void AddReference(JsonElement item, string kind, List<UkMeasureReference> destination)
    {
        if (item.ValueKind != JsonValueKind.Object) return;
        var id = ReadString(item, "id");
        if (!string.IsNullOrWhiteSpace(id) && destination.Count < 100)
            destination.Add(new(kind, id));
    }

    private static JsonElement GetObject(JsonElement parent, string key) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(key, out var child) && child.ValueKind == JsonValueKind.Object
            ? child : default;

    private static string? ReadString(JsonElement parent, string key)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(key, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

public sealed record UkMeasureReference(string Relation, string Id);
public sealed record UkNormalizedCommodity(string RequestedCode, string Status, string? CommodityCode,
    string? Description, string? ValidFrom, UkMeasureReference[] MeasureReferences, string? SourceUrl,
    string Attribution, bool VerifiedForCalculation, string Warning)
{
    public string DestinationTerritory { get; init; } = "GB";
    public string? ResponseSha256 { get; init; }
}
