using System.Text.Json;
using System.Security.Cryptography;
using GlobalTradeTax.Api;

internal static class UkCompletionTestData
{
    internal static readonly DateOnly Date = new(2026, 11, 5);
    internal const string Code = "0101299000";
    internal static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "UkCompletion", name));
    internal static UkReferenceResult Fixture(string name, string territory = "GB")
    {
        var bytes = Bytes(name);
        using var json = JsonDocument.Parse(bytes);
        return new("reference_only", json.RootElement.Clone(), "https://www.trade-tariff.service.gov.uk/" +
            (territory == "GB" ? "uk" : "xi") + "/api/v2/fixture?as_of=2026-11-05", "Reference only", Date)
            { DestinationTerritory = territory, ResponseSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
    }

    internal static UkRuleComponent Percent(decimal rate, string id = "p") => new(id, "01", rate, null, null, null, true);
    internal static UkMeasureRule Rule(string id, decimal rate, bool vat = false) =>
        new(id, vat ? "305" : "103", vat ? "VAT" : "Third country duty", true, vat, false, rate + "%",
            [Percent(rate, id + "-01")], [], [], [], [], vat ? null : "100", [], [], true, []);

    internal static UkAssessmentReport Assessment(string code = Code, string origin = "US", string territory = "GB", decimal duty = 10m, decimal vat = 20m)
    {
        UkMeasureDecision Decision(string id, decimal rate, bool isVat) => new(id, "requires_review", rate + "%", [origin], [])
        {
            Applicability = new("candidate_for_review", "import", "within_window", "direct_country_reference", "empty_exclusion_list", [], false),
            Rule = Rule(id, rate, isVat)
        };
        return new("requires_review", code, origin, Date, [Decision("duty", duty, false), Decision("vat", vat, true)], [],
            "https://www.trade-tariff.service.gov.uk/uk/api/v2/commodities/" + code, null, null, [], false)
            { DestinationTerritory = territory, ResponseSha256 = new string('a', 64), ReferenceGraphComplete = true };
    }

    internal static UkShipmentReviewRequest Request() => new("GB", Date, "US", "free_circulation", "GBP",
        [new("a", Code, "US", 1000m, 100m, 1m, "duty", "vat", [])], [], IntrinsicValueGbp: 1000m, TransactionKind: "commercial");

    internal static Dictionary<(string, string), UkAssessmentReport> Sources(params UkAssessmentReport[] reports) =>
        reports.ToDictionary(x => (x.CommodityCode, x.Origin));
}
