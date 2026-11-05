using System.Globalization;
using System.Text.Json;

namespace GlobalTradeTax.Api;

// Deliberately narrow readers for the recorded JSON:API contract, never legal eligibility.
internal static class UkEvidenceJson
{
    internal static JsonElement Object(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object ? value : default;

    internal static string? String(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static bool TryReferences(JsonElement resource, string name, string type, bool many, out string[] ids)
    {
        ids = [];
        var relation = Object(Object(resource, "relationships"), name);
        if (relation.ValueKind != JsonValueKind.Object || !relation.TryGetProperty("data", out var data)) return false;
        if (many ? data.ValueKind != JsonValueKind.Array : data.ValueKind != JsonValueKind.Object) return false;
        var items = many ? data.EnumerateArray().ToArray() : [data];
        if (items.Length > 1024) return false;
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = String(item, "id");
            if (String(item, "type") != type || string.IsNullOrWhiteSpace(id) || !result.Add(id)) return false;
        }
        ids = result.Order(StringComparer.Ordinal).ToArray();
        return true;
    }

    internal static bool TryDate(string? text, out DateOnly date, bool allowEndOfDay = false)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        // Upstream emits UTC midnight dates and sometimes the final second of an end date.
        if (DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp) &&
            (timestamp.TimeOfDay == TimeSpan.Zero || allowEndOfDay && timestamp.TimeOfDay >= new TimeSpan(23, 59, 59)))
        {
            date = DateOnly.FromDateTime(timestamp.UtcDateTime);
            return true;
        }
        return false;
    }

    internal static string EffectiveWindow(JsonElement attributes, DateOnly date)
    {
        if (!TryDate(String(attributes, "effective_start_date"), out var start) ||
            !attributes.TryGetProperty("effective_end_date", out var endValue)) return "unknown";
        DateOnly? end = null;
        if (endValue.ValueKind != JsonValueKind.Null)
        {
            if (endValue.ValueKind != JsonValueKind.String || !TryDate(endValue.GetString(), out var parsedEnd, allowEndOfDay: true)) return "unknown";
            end = parsedEnd;
        }
        if (end < start) return "unknown";
        return date < start || date > end ? "outside_window" : "within_window";
    }
}
