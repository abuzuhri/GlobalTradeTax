namespace GlobalTradeTax.Api;

public sealed record TrMaTnSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record TrMaTnReference(string Country, string Name, string Status, TrMaTnSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

public static class TrMaTnDestinationReference
{
    private static TrMaTnSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-12");
    private static readonly Dictionary<string, (string Name, TrMaTnSource[] Sources)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TR"] = ("Türkiye", [
            Source("Ministry of Trade", "commercial import procedures, tariff classification, taxes and controls", "https://ticaret.gov.tr/gumruk-islemleri/sikca-sorulan-sorular/ticari/ithalat"),
            Source("Ministry of Trade", "import regime, duties and permits", "https://ticaret.gov.tr/ithalat/ithalat-mevzuati/ithalat-rejimi-karari-igv-karari-ve-ithalat-tebligleri/1-ithalat-rejimi-kararikarar-sayisi3350karar-metni-ve-tablolar-konsolide-edilmis-olup-gunceldir")]),
        ["MA"] = ("Morocco", [
            Source("Ministry of Industry and Commerce", "historical importer guide: customs declarations, duties, taxes and documents", "https://www.mcinet.gov.ma/sites/default/files/importateur/guide_fr.pdf"),
            Source("Ministry of Industry and Commerce", "industrial product import controls; not all goods", "https://www.mcinet.gov.ma/en/content/quality-and-market-surveillance/market-surveillance")]),
        ["TN"] = ("Tunisia", [
            Source("Tunisian Customs", "historical commercial import procedures and controls", "https://www.douane.gov.tn/fileadmin/guides_formulaires/guides/2018-05-18_manuel_procedures_import_v2.pdf"),
            Source("Ministry of Finance", "general import VAT context; verify current law", "https://www.finances.gov.tn/fr/apercu-general-sur-la-fiscalite")]),
    };
    public static TrMaTnReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only", [.. item.Sources],
            ["Verify commodity classification and actual goods origin; shipping country does not establish origin",
             "Verify preference eligibility and origin evidence independently; agreements do not establish eligibility",
             "Confirm customs valuation, import regime, applicable duties, taxes and fees with current authoritative sources",
             "Confirm importer registration, declarant responsibilities and broker arrangements",
             "Review permits, prohibitions and product controls with competent authorities",
             "Verify postal, courier and low-value applicability for this shipment",
             "Verify precise destination territory, special-zone and subnational treatment for this shipment"]);
    }
}
