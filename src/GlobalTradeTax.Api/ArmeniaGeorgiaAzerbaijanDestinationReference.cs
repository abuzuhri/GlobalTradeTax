namespace GlobalTradeTax.Api;

public sealed record ArmeniaGeorgiaAzerbaijanSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record ArmeniaGeorgiaAzerbaijanReference(string Country, string Name, string Status, ArmeniaGeorgiaAzerbaijanSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

public static class ArmeniaGeorgiaAzerbaijanDestinationReference
{
    private static ArmeniaGeorgiaAzerbaijanSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-16");
    private static readonly Dictionary<string, (string Name, ArmeniaGeorgiaAzerbaijanSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AM"] = ("Armenia", [
            Source("Armenia State Revenue Committee", "commercial import, tariff and permit discovery; informational platform", "https://exim.src.am/en/about"),
            Source("Armenia State Revenue Committee", "business import payment and valuation context", "https://exim.src.am/hy/business"),
            Source("Armenia State Revenue Committee", "example food safety permit and competent authority", "https://exim.src.am/en/passport/4559")],
            "Review Armenia competent authorities and exact customs territory and special-zone treatment."),
        ["GE"] = ("Georgia", [
            Source("Georgia Revenue Service", "commodity code, import taxes and documents discovery", "https://www.rs.ge/CommodityCodes-en?cat=1&tab=1"),
            Source("Georgia Revenue Service", "commercial import procedures and tax context; historical brochure", "https://www.rs.ge/Media/Default/Images/CustomsBrochures/exsport%20import.pdf"),
            Source("Georgia Revenue Service", "veterinary and phytosanitary import permit authority", "https://voice.rs.ge/Default.aspx?id=4414")],
            "Review Georgia competent authorities and exact customs territory and special-zone treatment."),
        ["AZ"] = ("Azerbaijan", [
            Source("Azerbaijan State Customs Committee", "customs valuation, payments and import tax context", "https://customs.gov.az/en/sahibkarlar-ucun/gomruk-odenisleri"),
            Source("Azerbaijan State Customs Committee", "historical tariff nomenclature context", "https://customs.gov.az/en/faydali/gomruk-statistikasi/tariff-statistics/"),
            Source("Azerbaijan State Customs Committee", "commercial customs declaration services", "https://customs.gov.az/en/e-xidmetler"),
            Source("Azerbaijan Food Safety Agency", "food and feed import controls; 2023 guidance", "https://afsa.gov.az/az/idxal-ve-ixrac/xeberler/idxal-olunan-qida-ve-yem-mehsullarina-dair-tenzimleme-nece-heyata-kecirilir")],
            "Review Azerbaijan competent product-control authorities and exact customs territory and special-zone treatment."),
    };
    public static ArmeniaGeorgiaAzerbaijanReference? Find(string? country)
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
