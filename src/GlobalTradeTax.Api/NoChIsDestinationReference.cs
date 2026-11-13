namespace GlobalTradeTax.Api;

public sealed record NoChIsReferenceSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record NoChIsReference(string Country, string Name, string Status, NoChIsReferenceSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

/// <summary>National source discovery only; no shipment applicability is inferred.</summary>
public static class NoChIsDestinationReference
{
    private static NoChIsReferenceSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-12");
    private static readonly Dictionary<string, (string Name, NoChIsReferenceSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NO"] = ("Norway", [
            Source("Norwegian Customs", "commercial import procedure, duties, VAT and controls", "https://www.toll.no/en/corporate/import/import-guide-for-beginners")],
            "Verify exact Norwegian customs territory, including Svalbard, Jan Mayen and dependencies separately."),
        ["CH"] = ("Switzerland", [
            Source("Swiss Federal Office for Customs and Border Security", "commercial import declaration and origin", "https://www.bazg.admin.ch/en/importation-checklist-companies"),
            Source("Swiss Federal Office for Customs and Border Security", "import tax context", "https://www.bazg.admin.ch/en/value-added-tax-and-import-tax-companies"),
            Source("Swiss Federal Office for Customs and Border Security", "goods traffic restrictions and permits", "https://www.bazg.admin.ch/en/bans-restrictions-conditions-goods-traffic"),
            Source("Swiss Federal Office for Customs and Border Security", "customs territory context for trade statistics", "https://www.bazg.admin.ch/en/swiss-foreign-trade-statistics-methods-survey")],
            "Verify exact customs and tax territory and special zones; separately coded Liechtenstein is outside this endpoint."),
        ["IS"] = ("Iceland", [
            Source("Iceland Revenue and Customs", "commercial import procedures and import charges", "https://www.skatturinn.is/english/companies/customs-matters/importing-to-iceland/"),
            Source("Iceland Revenue and Customs", "commercial import documents and licences", "https://www.skatturinn.is/english/companies/customs-matters/importing-to-iceland/import-documents/")],
            "Verify exact destination and movement through any bonded warehouse or free zone.")
    };

    public static NoChIsReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only", [.. item.Sources],
            ["Verify classification and actual goods origin; shipping country does not establish origin",
             "Verify preference eligibility and proof independently",
             "Confirm customs valuation, import regime, duties, taxes and fees",
             "Confirm importer registration, declarant and broker responsibilities",
             "Review permits, prohibitions and product controls with the competent authority",
             "Check postal, courier and low-value procedure applicability to this shipment",
             "Confirm exact destination territory and special-zone or subnational treatment", item.Detail]);
    }
}
