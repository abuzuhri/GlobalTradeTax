namespace GlobalTradeTax.Api;

public sealed record GhanaTanzaniaUgandaSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record GhanaTanzaniaUgandaReference(string Country, string Name, string Status, GhanaTanzaniaUgandaSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

public static class GhanaTanzaniaUgandaDestinationReference
{
    private static GhanaTanzaniaUgandaSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-13");
    private static readonly Dictionary<string, (string Name, GhanaTanzaniaUgandaSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GH"] = ("Ghana", [
            Source("Ghana Revenue Authority", "commercial import procedures, valuation and permits", "https://gra.gov.gh/customs/import-procedures/"),
            Source("Ghana Revenue Authority", "tariff and levy context", "https://gra.gov.gh/customs/customs-tariffs-and-levies/")],
            "Review Ghana import controls and exact destination and special-zone treatment for the shipment."),
        ["TZ"] = ("Tanzania", [
            Source("Tanzania Bureau of Standards via Tanzania Trade Portal", "product conformity guidance", "https://trade.tanzania.go.tz/media/PVoC_HARMONISED_PR0CEDURE_%28SUMMARISED%29_1.pdf"),
            Source("Tanzania Trade Development Authority", "historical regional customs regulations; current applicability unverified", "https://trade.tanzania.go.tz/media/Customs%20Regulations_1.pdf")],
            "Review exact mainland or Zanzibar destination, applicable customs and tax treatment, product controls and special zones for the shipment."),
        ["UG"] = ("Uganda", [
            Source("Uganda Revenue Authority", "customs valuation, import tax and tariff discovery context", "https://ura.go.ug/en/import-export-faqs/"),
            Source("Uganda Revenue Authority", "commercial documents and restricted-goods permits", "https://ura.go.ug/en/documents-at-the-point-of-entry/")],
            "Review Uganda import controls and exact destination and special-zone treatment for the shipment."),
    };
    public static GhanaTanzaniaUgandaReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only", [.. item.Sources], [
            "Verify commodity classification and actual goods origin; shipping country does not establish origin",
            "Verify preference eligibility and proof independently; regional membership does not establish preference",
            "Confirm customs valuation, import regime and applicable duties, taxes and fees",
            "Confirm importer registration, declarant documents and broker responsibility",
            "Review permits, prohibitions and responsible agencies for the actual goods",
            "Verify postal, courier or low-value procedure applicability for this shipment",
            "Confirm precise customs territory and special-zone or subnational treatment", item.Detail]);
    }
}
