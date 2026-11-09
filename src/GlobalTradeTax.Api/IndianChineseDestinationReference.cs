namespace GlobalTradeTax.Api;

public sealed record NationalSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record NationalReference(string Country, string Name, string Status, string TerritoryStatus, NationalSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

public static class IndianChineseDestinationReference
{
    public static NationalReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter)) return null;
        var code = country.ToUpperInvariant();
        var sources = code switch
        {
            "IN" => new NationalSource[] {
                new("Central Board of Indirect Taxes and Customs", "Customs Act: entry of imported goods", "https://taxinformation.cbic.gov.in/content-page/explore-act/1000086/1000002", "2026-11-09"),
                new("Central Board of Indirect Taxes and Customs", "Integrated tax on imported goods", "https://taxinformation.cbic.gov.in/content-page/explore-act/1000613/1000001", "2026-11-09"),
                new("Directorate General of Foreign Trade", "Import policy, classification, registration and controls", "https://content.dgft.gov.in/Website/dgftprod/9158887c-cdfb-4312-92f1-15eeb8e8aa70/%5BUPDATED%5D%20CHAPTER%202%20OF%20FTP.pdf", "2026-11-09") },
            "CN" => new NationalSource[] {
                new("State Taxation Administration", "Tariff law and customs duties", "https://fgk.chinatax.gov.cn/zcfgk/c100009/c5234556/content.html", "2026-11-09"),
                new("State Council", "Commercial customs declaration and clearance guide", "https://english.www.gov.cn/AssetsZi/Foreign_Investment_Guide_of_the_People%27s_Republic_of_China_2024.pdf", "2026-11-09"),
                new("State Taxation Administration", "VAT law including imported goods", "https://tianjin.chinatax.gov.cn/11200000000/0300/030004/03000418/p20241226144544917.shtml", "2026-11-09"),
                new("State Council", "Import and export control and licensing enforcement", "https://english.www.gov.cn/services/doingbusiness/202102/24/content_WS6035f3c3c6d0719374af97b8.html", "2026-11-09") },
            _ => null
        };
        if (sources is null) return null;
        return new(code, code == "IN" ? "India" : "China", "reference_only", "unresolved", sources,
            ["Verify commodity classification, actual goods origin and preference evidence",
             "Confirm importer registration, importer of record and broker responsibilities",
             "Verify customs valuation, import regime and applicable duties, taxes and fees",
             "Review product controls, permits and prohibitions for the goods",
             "Confirm precise territorial scope and special-zone treatment for this movement",
             "Verify whether postal, courier or low-value procedures apply",
             code == "IN" ? "Review Indian IEC and applicable import policy and tax treatment" : "Confirm China customs territory and applicable import controls; CN alone does not establish applicability in a distinct customs territory"]);
    }
}
