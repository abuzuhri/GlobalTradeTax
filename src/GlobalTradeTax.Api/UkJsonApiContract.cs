using System.Text.Json;

namespace GlobalTradeTax.Api;

// Structural guard only: this does NOT validate legal applicability or tariff amounts.
public static class UkJsonApiContract
{
    public static bool IsValidCommodity(JsonElement root, string expectedCode) =>
        IsValidResource(root, "commodity", out var data) &&
        data.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object &&
        Read(attributes, "goods_nomenclature_item_id") == expectedCode;

    public static bool IsValidGeographicalArea(JsonElement root, string expectedId) =>
        IsValidResource(root, "geographical_area", out var data) && Read(data, "id") == expectedId;

    private static bool IsValidResource(JsonElement root, string expectedType, out JsonElement data)
    {
        data = default;
        if (root.ValueKind != JsonValueKind.Object ||
            root.TryGetProperty("errors", out _) ||
            !root.TryGetProperty("data", out data) || !IsResourceObject(data) ||
            Read(data, "type") != expectedType) return false;
        if (root.TryGetProperty("included", out var included) &&
            (included.ValueKind != JsonValueKind.Array || !included.EnumerateArray().All(IsResourceObject))) return false;
        return true;
    }

    private static bool IsResourceObject(JsonElement resource) =>
        resource.ValueKind == JsonValueKind.Object &&
        !string.IsNullOrWhiteSpace(Read(resource, "id")) && !string.IsNullOrWhiteSpace(Read(resource, "type")) &&
        (!resource.TryGetProperty("attributes", out var attributes) || attributes.ValueKind == JsonValueKind.Object) &&
        (!resource.TryGetProperty("relationships", out var relationships) || relationships.ValueKind == JsonValueKind.Object);

    private static string? Read(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
