namespace GlobalTradeTax.Api;

public sealed record GccSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record GccReference(string Country, string Name, string Status, GccSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool ShipmentReleaseApproved => false;
    public bool PaymentApproved => false;
}

/// <summary>Returns static national source pointers for review, never a legally applicable rate.</summary>
public static class GccDestinationReference
{
    private static readonly Dictionary<string, (string Name, string Customs, string Tax, string CustomsAuthority, string TaxAuthority, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BH"] = ("Bahrain", "https://www.bahrain.bh/wps/wcm/connect/c3e341be-a345-48ab-bcf2-bc25b5aa836b/Unified%2BCustoms%2BRegulation%2BLaw%2Bof%2Bthe%2BGCC%2BStates_en.pdf?CVID=m0H3E2q&MOD=AJPERES", "https://www.bahrain.bh/new/en/business-vat_en.html", "Bahrain government", "National Bureau for Revenue", "Review Bahrain VAT treatment and the status of the destination zone."),
        ["KW"] = ("Kuwait", "https://customs.gov.kw/KGO/HsCode", "https://mof.gov.kw/TCRS_Public/en", "Kuwait Customs", "Kuwait Ministry of Finance", "Confirm current Kuwait tax law and any applicable indirect taxes; no VAT conclusion is inferred."),
        ["OM"] = ("Oman", "https://www.customs.gov.om/en/user-guides/", "https://tms.taxoman.gov.om/portal/web/taxportal/vat-faqs", "Oman Customs", "Oman Tax Authority", "Review Oman VAT treatment and relevant special-zone conditions."),
        ["QA"] = ("Qatar", "https://customs.gov.qa/English/Legislations/Laws/Pages/default.aspx", "https://gta.gov.qa/en/taxes-info", "Qatar General Authority of Customs", "Qatar General Tax Authority", "Confirm current Qatar indirect tax law, including excise and any VAT developments."),
        ["SA"] = ("Saudi Arabia", "https://zatca.gov.sa/en/RulesRegulations/Taxes/Pages/Integrated-Tarrifs.aspx", "https://www.zatca.gov.sa/en/RulesRegulations/VAT/Pages/About-Vat.aspx", "Zakat, Tax and Customs Authority", "Zakat, Tax and Customs Authority", "Review Saudi VAT treatment and special-zone conditions."),
        ["AE"] = ("United Arab Emirates", "https://icp.gov.ae/en/customs-union-for-gcc/", "https://tax.gov.ae/-/media/Files/FTA/links/vat-designated-zone/Designated-Zones-VAT-Guide.pdf", "Federal Authority for Identity, Citizenship, Customs and Port Security", "Federal Tax Authority", "Review UAE designated-zone conditions and the particular movement of goods.")
    };

    /// <summary>Accepts ASCII ISO codes in either case; unknown or malformed codes return null.</summary>
    public static GccReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only",
            [new(item.CustomsAuthority, "customs and tariff reference", item.Customs, "2026-11-08"), new(item.TaxAuthority, "national tax and territorial review", item.Tax, "2026-11-08")],
            ["Verify classification and actual origin, including preference evidence", "Confirm import procedure, customs valuation and current applicable tariff", "Review product controls, permits and importer and broker responsibilities", "Verify free-zone and territorial treatment for this movement", "Verify national VAT and other indirect taxes for the transaction", item.Detail]);
    }
}
