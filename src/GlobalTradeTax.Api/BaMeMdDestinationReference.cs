namespace GlobalTradeTax.Api;

public sealed record BaMeMdReferenceSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record BaMeMdReference(string Country, string Name, string Status, BaMeMdReferenceSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

/// <summary>National source discovery only; no shipment applicability is inferred.</summary>
public static class BaMeMdDestinationReference
{
    private static BaMeMdReferenceSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-13");
    private static readonly Dictionary<string, (string Name, BaMeMdReferenceSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BA"] = ("Bosnia and Herzegovina", [
            Source("Indirect Taxation Authority", "customs regulations and declaration instructions index", "https://www.uino.gov.ba/portal/bs/propisi/carina/"),
            Source("Indirect Taxation Authority", "import declaration instructions", "https://www.uino.gov.ba/portal/wp-content/uploads/PROPISI/3_Carina/3_Uputstva/5_Carinska_prijava/B/B-1-U-o-popunjavanju-car-prijave-i-deklaracije-za-priv-smjestajslist-9-23-10-02-23.pdf"),
            Source("Indirect Taxation Authority", "historical import VAT law context", "https://www.uino.gov.ba/portal/wp-content/uploads/PROPISI/2_Porezi/1_PDV/1_Zakon_o_PDV/B/B-1-Zakon_o_PDV-b.pdf"),
            Source("Indirect Taxation Authority", "customs valuation and tariff context", "https://www.uino.gov.ba/portal/en/customs/customs-valuation/"),
            Source("Veterinary Office", "border veterinary inspection responsibilities", "https://www.vet.gov.ba/bs/category/c255")],
            "Verify exact Bosnia and Herzegovina destination, customs and tax territory and any special-zone treatment."),
        ["ME"] = ("Montenegro", [
            Source("Customs Administration", "customs law: declaration, valuation and tariff", "https://wapi.gov.me/download/089be859-51ae-4c3a-8965-fe5bf0bda222?version=1.0"),
            Source("Ministry of Finance", "2026 tariff regulation and quota context", "https://wapi.gov.me/download/c9ad2bc2-dfc4-4107-9fea-585c792b81af?version=1.0"),
            Source("Tax Administration", "historical tax practice including import VAT", "https://wapi.gov.me/download/199841cc-c742-465a-a5d1-bbc563f19e11?version=1.0"),
            Source("Government of Montenegro", "historical veterinary and phytosanitary border inspection responsibilities", "https://wapi.gov.me/download/4bfcfa05-6698-474c-be58-bac9cd34a1e5?version=1.0")],
            "Verify exact Montenegro destination and customs and tax treatment including special zones."),
        ["MD"] = ("Moldova", [
            Source("Customs Service", "import and export declaration processing norms", "https://customs.gov.md/api/media/02/01/2024/562-O_29.12.2023.pdf"),
            Source("Customs Service", "customs valuation guidance", "https://customs.gov.md/ro/articles/aspecte-privind-valoarea-in-vama-potrivit-actualelor-prevederi-vamale-1887-ro"),
            Source("Customs Service", "tariff information and import tax terminology", "https://customs.gov.md/api/media/02/01/2024/528-O_29.12.2023.pdf"),
            Source("National Food Safety Agency", "border veterinary and phytosanitary inspection responsibilities", "https://www.ansa.gov.md/aparat-central/directia-inspectare-la-frontiera.html")],
            "Verify exact Moldova destination, customs and tax treatment including special zones and regional circumstances individually.")
    };

    public static BaMeMdReference? Find(string? country)
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
