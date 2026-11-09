namespace GlobalTradeTax.Api;

public sealed record FiveCountryImportSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record FiveCountryImportGuidance(string Country, string Name, string Status, FiveCountryImportSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

/// <summary>Static commercial import source pointers, not applicable rates or clearance decisions.</summary>
public static class FiveCountryImportReference
{
    private static readonly Dictionary<string, (string Name, string Authority, string Purpose, string Url, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MY"] = ("Malaysia", "Royal Malaysian Customs Department", "Import declaration and customs duties legislation", "https://www.customs.gov.my/en/pg/Customs%20Act/CUSTOMS%20ACT%201967-1.pdf", "Review Malaysia sales tax and designated or special area treatment for the movement"),
        ["ID"] = ("Indonesia", "Directorate General of Customs and Excise", "Import for consumption, declaration, duties and restricted goods", "https://www.beacukai.go.id/impor-untuk-dipakai", "Review Indonesia import-for-consumption requirements and relevant agency restrictions"),
        ["TH"] = ("Thailand", "Laem Chabang Port Customs Office", "Commercial import declarations, duties and permits", "https://lcpcb.customs.go.th/th/import-procedures", "Review Thailand import declaration and permits with the responsible agencies"),
        ["VN"] = ("Vietnam", "Vietnam Customs", "Administrative procedure index including commercial imports and customs tax", "https://pus.customs.gov.vn/faces/Procedure", "Review Vietnam commercial import procedure and current goods-management requirements"),
        ["PH"] = ("Philippines", "Bureau of Customs", "Customs Modernization and Tariff Act import clearance and duties", "https://customs.gov.ph/wp-content/uploads/2023/01/CMTA-RA-10863-2.pdf", "Review Philippines importer accreditation and regulated-goods clearances")
    };

    public static FiveCountryImportGuidance? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only",
            [new(item.Authority, item.Purpose, item.Url, "2026-11-09")],
            ["Verify commodity classification, actual goods origin and preference evidence",
             "Verify customs valuation, importer registration and broker responsibility",
             "Confirm the applicable import regime, duties, taxes and fees",
             "Check prohibitions, restrictions and permits with responsible authorities",
             "Verify precise customs territory and special-zone or subnational treatment",
             "Assess postal, courier and low-value rules for this particular shipment", item.Detail]);
    }
}
