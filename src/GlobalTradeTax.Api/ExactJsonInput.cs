using System.Globalization;
using System.Text.Json;

namespace GlobalTradeTax.Api;

// Financial inputs must not silently round an exponent or excessive precision to zero.
internal static class ExactJsonInput
{
    public static T? Deserialize<T>(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var nodes = 0;
        void Validate(JsonElement item)
        {
            if (++nodes > 10000) throw new JsonException("JSON input node limit exceeded.");
            if (item.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON field.");
                    Validate(property.Value);
                }
            }
            else if (item.ValueKind == JsonValueKind.Array) foreach (var child in item.EnumerateArray()) Validate(child);
            else if (item.ValueKind == JsonValueKind.Number)
            {
                var text = item.GetRawText();
                var parts = text.TrimStart('-').Split('.');
                var fraction = parts.Length > 1 ? parts[1].TrimEnd('0') : "";
                var significant = (parts[0] + fraction).TrimStart('0');
                if (text.Contains('e', StringComparison.OrdinalIgnoreCase) || fraction.Length > 28 || significant.Length > 28 ||
                    !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _))
                    throw new JsonException("Exact decimal notation required.");
            }
        }
        Validate(document.RootElement);
        return document.RootElement.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
