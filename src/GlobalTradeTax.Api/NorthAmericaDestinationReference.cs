namespace GlobalTradeTax.Api;

public sealed record NorthAmericaSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record NorthAmericaReference(string Country, string Name, string Status, NorthAmericaSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool ShipmentReleaseApproved => false;
    public bool PaymentApproved => false;
}

/// <summary>Returns official review pointers for US, Canada and Mexico, not applicable rates or approvals.</summary>
public static class NorthAmericaDestinationReference
{
    private static readonly Dictionary<string, (string Name, NorthAmericaSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["US"] = ("United States", [new("US International Trade Commission", "Harmonized Tariff Schedule", "https://hts.usitc.gov/", "2026-11-09"), new("US Customs and Border Protection", "classification, origin and entry", "https://www.help.cbp.gov/s/article/Article-1397", "2026-11-09"), new("Internal Revenue Service", "federal, state and local tax context", "https://apps.irs.gov/app/understandingTaxes/whys/thm04/les01/media/is1_thm04_les01.pdf", "2026-11-09")], "Review exact state, local and territorial sales or use tax scope with the relevant jurisdiction; federal customs entry does not resolve it."),
        ["CA"] = ("Canada", [new("Canada Border Services Agency", "customs tariff", "https://www.cbsa-asfc.gc.ca/trade-commerce/tariff-tarif/2026/menu-eng.html", "2026-11-09"), new("Canada Border Services Agency", "origin and preference evidence", "https://www.cbsa-asfc.gc.ca/import/origin-origine-eng.html", "2026-11-09"), new("Canada Revenue Agency", "GST/HST place of supply", "https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/gst-hst-businesses/charge-collect-place-supply.html", "2026-11-09")], "Review exact province or territory, import GST/HST and any provincial or Quebec sales tax separately."),
        ["MX"] = ("Mexico", [new("Servicio de Administración Tributaria", "tariff classification and NICO", "https://wwwmat.sat.gob.mx/tramites/71719/presenta-tu-consulta-de-clasificacion-arancelaria", "2026-11-09"), new("Agencia Nacional de Aduanas de México", "customs procedures", "https://aduanafacil.anam.gob.mx/", "2026-11-09"), new("Servicio de Administración Tributaria", "import VAT valuation", "https://wwwmatnp.sat.gob.mx/articulo/43379/articulo-27", "2026-11-09")], "Review the exact destination, border or special region, customs procedure and applicable national and subnational tax scope.")
    };

    /// <summary>Accepts only ASCII alpha-2 codes US, CA and MX, in either case.</summary>
    public static NorthAmericaReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only", item.Sources.ToArray(),
            ["Confirm importer of record, broker responsibilities and exact destination territory", "Verify classification, actual goods origin, customs valuation and current applicable duties and fees", "Review import procedure, product controls, permits and agency requirements", "Verify postal, courier or low-value treatment using shipment-specific facts", "Verify USMCA/CUSMA/T-MEC origin rules, proof and other conditions; shipping country or membership does not establish preference", "Review current consumption and other taxes for the transaction and exact destination", item.Detail]);
    }
}
