using System.Text.Json;
using System.Text.Json.Nodes;
using GlobalTradeTax.Api;

internal static class UkApplicabilityTestData
{
    internal const string Code = "0101210000";
    internal static readonly DateOnly Date = new(2026, 11, 5);

    internal static JsonObject Ref(string type, string id) => new() { ["type"] = type, ["id"] = id };
    internal static JsonObject Many(string type, params string[] ids) => new()
        { ["data"] = new JsonArray(ids.Select(id => (JsonNode)Ref(type, id)).ToArray()) };

    internal static JsonObject Measure(string id = "m1", string area = "US") => new()
    {
        ["type"] = "measure", ["id"] = id,
        ["attributes"] = new JsonObject { ["import"] = true, ["export"] = false,
            ["effective_start_date"] = "2021-01-01T00:00:00.000Z", ["effective_end_date"] = null },
        ["relationships"] = new JsonObject
        {
            ["geographical_area"] = new JsonObject { ["data"] = Ref("geographical_area", area) },
            ["excluded_countries"] = Many("geographical_area"),
            ["measure_conditions"] = Many("measure_condition"),
            ["order_number"] = new JsonObject { ["data"] = null }
        }
    };

    internal static JsonObject Commodity(params JsonObject[] measures) => new()
    {
        ["data"] = new JsonObject
        {
            ["type"] = "commodity", ["id"] = "93796",
            ["attributes"] = new JsonObject { ["goods_nomenclature_item_id"] = Code },
            ["relationships"] = new JsonObject
                { ["import_measures"] = Many("measure", measures.Select(m => m["id"]!.GetValue<string>()).ToArray()) }
        },
        ["included"] = new JsonArray(measures.Select(m => (JsonNode)m).ToArray())
    };

    internal static JsonObject Area(string id, params string[] children) => new()
    {
        ["type"] = "geographical_area", ["id"] = id,
        ["relationships"] = new JsonObject { ["children_geographical_areas"] = Many("geographical_area", children) }
    };

    internal static JsonObject AreaDocument(string id = "1011", params string[] children) => new()
    {
        ["data"] = Area(id, children),
        ["included"] = new JsonArray(children.Select(child => (JsonNode)Area(child)).ToArray())
    };

    internal static UkReferenceResult Dated(JsonNode document, DateOnly? date = null)
    {
        using var json = JsonDocument.Parse(document.ToJsonString());
        return new("reference_only", json.RootElement.Clone(), "https://www.trade-tariff.service.gov.uk/uk/api/v2/reference?as_of=" +
            (date ?? Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "Reference only", date ?? Date);
    }

    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "UkTariff", name));
}
