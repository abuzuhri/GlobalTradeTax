namespace GlobalTradeTax.Api;

public sealed record RsAlMkReferenceSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record RsAlMkReference(string Country, string Name, string Status, RsAlMkReferenceSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

/// <summary>National source discovery only; no shipment applicability is inferred.</summary>
public static class RsAlMkDestinationReference
{
    private static RsAlMkReferenceSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-13");
    private static readonly Dictionary<string, (string Name, RsAlMkReferenceSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RS"] = ("Serbia", [
            Source("Serbian Customs Administration", "tariff database and import controls; import data informational", "https://carina.rs/sr/privreda/tarifski-poslovi/taris.html"),
            Source("Serbian Tax Administration", "historical VAT law including imports through 2024 amendments", "https://purs.gov.rs/upload/media/2025/2/4/417338/Zakonoporezunadodatuvrednost.pdf"),
            Source("Serbian Customs Administration", "historical draft customs law: valuation and import procedure", "https://www.carina.rs/upload/media/2020/10/21/47472/Customs_Law_ENGLISH_final.pdf"),
            Source("Serbian Customs Administration", "simplified commercial import procedure pointer", "https://www.carina.rs/en/business-community/Simplified-customs-procedures.html")],
            "Review current import tax and controls with competent authorities; the historical draft is not current legal proof."),
        ["AL"] = ("Albania", [
            Source("Albanian General Directorate of Customs", "tariff classification and trade measures", "https://www.dogana.gov.al/english/d/182/248/0/231/combined-nomenclature-of-goods"),
            Source("Albanian General Directorate of Customs", "import procedures and regimes", "https://dogana.gov.al/english/d/181/294/0/279/import-of-goods"),
            Source("Albanian General Directorate of Customs", "import VAT context", "https://www.dogana.gov.al/english/d/182/248/0/245/vat"),
            Source("Albanian General Directorate of Customs", "selected prohibitions and restrictions", "https://dogana.gov.al/english/c/177/prohibitions-and-restrictions")],
            "Review the current applicable tax law and product-specific controls; the restrictions index is not exhaustive."),
        ["MK"] = ("North Macedonia", [
            Source("North Macedonia Customs Administration", "tariff, valuation and origin", "https://customs.gov.mk/en-GB/tarifa-vrednost-i-poteklo.nspx"),
            Source("North Macedonia Customs Administration", "tariff system and national VAT context", "https://www.customs.gov.mk/en-GB/pocetna/biznis-zaednica/akcizno-postapuvanje/akcizni-marki/ito.nspx"),
            Source("North Macedonia Customs Administration", "prohibitions and restrictions", "https://www.customs.gov.mk/en-GB/pocetna/biznis-zaednica/carinski-postapki/postapki-so-ekonomski-efekt/zabrani-i-ogranicuvanja.nspx"),
            Source("North Macedonia Customs Administration", "import licences and competent authorities", "https://customs.gov.mk/en-GB/exim.nspx")],
            "Review current national import tax and controls with the competent authorities.")
    };

    public static RsAlMkReference? Find(string? country)
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
