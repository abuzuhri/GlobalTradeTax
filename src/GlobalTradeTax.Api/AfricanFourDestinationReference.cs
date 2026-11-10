namespace GlobalTradeTax.Api;

public sealed record AfricanFourSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record AfricanFourReference(string Country, string Name, string Status, AfricanFourSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

public static class AfricanFourDestinationReference
{
    private static AfricanFourSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-10");
    private static readonly Dictionary<string, (string Name, AfricanFourSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ZA"] = ("South Africa", [
            Source("South African Revenue Service", "commercial import procedures and permits", "https://www.sars.gov.za/customs-and-excise/import-export-and-transit/imports/"),
            Source("South African Revenue Service", "import duties and VAT context", "https://www.sars.gov.za/customs-and-excise/duties-and-taxes/duties-and-taxes-for-importers/"),
            Source("South African Revenue Service", "prohibited and restricted goods", "https://www.sars.gov.za/customs-and-excise/prohibited-restricted-and-counterfeit-goods/")],
            "Review SACU movement, origin, national VAT and any special-zone treatment for the shipment."),
        ["KE"] = ("Kenya", [
            Source("Kenya Revenue Authority", "commercial import procedures, taxes and documents", "https://www.kra.go.ke/business/companies-partnerships/companies-partnerships-pin-taxes/company-partnership-imports-exemptions"),
            Source("Kenya Revenue Authority", "prohibited and restricted imports schedules", "https://www.kra.go.ke/images/publications/Prohibited%20and%20Restricted%20Goods%20Under%20the%20Second%20and%20Third%20Schedule%20of%20the%20Eac%20Customs%20Management%20Act%2C%202004.pdf"),
            Source("Kenya Revenue Authority", "certificate of origin notice", "https://www.kra.go.ke/news-center/public-notices/2303-requirement-for-mandatory-certificate-of-origin-on-imports-into-kenya")],
            "Review EAC tariff, national taxes and levies, origin documentation and special-zone treatment for the shipment."),
        ["NG"] = ("Nigeria", [
            Source("Nigeria Customs Service", "customs law including import restrictions and zones", "https://customs.gov.ng/wp-content/uploads/2023/08/NCS-ACT-2023-1.pdf")],
            "Review ECOWAS tariff, Nigerian taxes and import controls, and any free-zone movement for the shipment."),
        ["EG"] = ("Egypt", [
            Source("Egyptian Customs Authority", "customs law, import tax and controls", "https://customs.gov.eg/Upload/ECAAdminace/62f62698-1dd6-4320-a68b-75433f639fb4.pdf"),
            Source("Egyptian Customs Authority", "advance customs procedures", "https://www.customs.gov.eg/Legislations/SpecialSystem?SpecialSystemsTypeId=9"),
            Source("Egyptian Customs Authority", "customs procedure and import control notices index", "https://customs.gov.eg/Legislations/Documents")],
            "Review advance registration, national taxes and the exact customs territory and free-zone movement for the shipment."),
    };
    public static AfricanFourReference? Find(string? country)
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
