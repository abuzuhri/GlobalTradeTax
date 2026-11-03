using System.Text.Json;

namespace GlobalTradeTax.Api;

// Structural guard only: this does NOT validate legal applicability or tariff amounts.
public static class UkJsonApiContract
{
    public static bool IsValidResource(JsonElement root, string expectedId)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
            !string.Equals(id.GetString(), expectedId, StringComparison.Ordinal) ||
            !data.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(type.GetString())) return false;
        if (root.TryGetProperty("included", out var included) && included.ValueKind != JsonValueKind.Array) return false;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0) return false;
        return true;
    }
}
