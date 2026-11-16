namespace GlobalTradeTax.Api;

public sealed record BwZmRwReferenceSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record BwZmRwReference(string Country, string Name, string Status, BwZmRwReferenceSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool PaymentApproved => false;
    public bool ShipmentReleaseApproved => false;
}

/// <summary>National source discovery only; no shipment applicability is inferred.</summary>
public static class BwZmRwDestinationReference
{
    private static BwZmRwReferenceSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-16");
    private static readonly Dictionary<string, (string Name, BwZmRwReferenceSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BW"] = ("Botswana", [
            Source("Botswana Unified Revenue Service", "commercial import declaration guidance, published 2016; verify current procedure", "https://ecustoms.burs.org.bw/TFBSEW/docs/IMPORTERS%20%20EXPORTERS.pdf"),
            Source("Botswana Unified Revenue Service", "tariff and procedure code download index", "https://www.burs.org.bw/index.php/customsexcisemain/customs-and-exercise-downloads"),
            Source("Botswana Unified Revenue Service", "customs valuation guidance; verify current law", "https://www.burs.org.bw/index.php/about-us/faq/customs-a-excise-faq/cae-faq1"),
            Source("Botswana Unified Revenue Service", "import duty and VAT assessment guidance; historical rates require verification", "https://www.burs.org.bw/index.php/customsexcisemain/payments-of-customs-duty-and-import-vat/assessment-and-payment"),
            Source("Government of Botswana", "plant import permit service", "https://www.gov.bw/crop-production/issuance-plant-plant-products-import-permitphytosanitary-certificate-2?page=3")],
            "Confirm Botswana customs procedure and consult plant protection authorities where relevant; verify exact destination and special zones."),
        ["ZM"] = ("Zambia", [
            Source("Zambia Revenue Authority", "commercial import declaration, classification and valuation guidance; historical document", "https://www.zra.org.zm/wp-content/uploads/2019/12/Import-Declaration.pdf"),
            Source("Zambia Revenue Authority", "classification rules and advance tariff rulings", "https://www.zra.org.zm/rules-for-classification-of-goods/"),
            Source("Zambia Revenue Authority", "customs valuation and selected product specification codes", "https://www.zra.org.zm/valuation-of-goods/"),
            Source("Zambia Revenue Authority", "importation, import taxes and postal procedures; verify current requirements", "https://www.zra.org.zm/importation-of-goods/"),
            Source("Zambia Medicines Regulatory Authority", "medicine import and export permit guidance", "https://www.zamra.co.zm/services/import-and-export/")],
            "Check Zambia tariff specification codes where applicable and consult ZAMRA for regulated medicines; verify exact destination and special zones."),
        ["RW"] = ("Rwanda", [
            Source("Rwanda Revenue Authority", "2024 commercial import clearance brochure; verify current requirements", "https://www.rra.gov.rw/fileadmin/user_upload/Importation_and_Goods__Clearance_2024.pdf"),
            Source("Rwanda Revenue Authority", "2025 handbook including customs duties and tariff discovery; verify national applicability and amendments", "https://www.rra.gov.rw/fileadmin/Folder_for_2025/RRA_Tax_Handbook_2025.pdf"),
            Source("Rwanda Revenue Authority", "customs valuation guidance", "https://www.rra.gov.rw/en/customs-services/valuation-gateway"),
            Source("Rwanda Food and Drugs Authority", "2026 pharmaceutical import and export guidelines", "https://rwandafda.gov.rw/monitoring-tool/documents-management/uploads/1/Guidelines/1776078758_Guidelines%20for%20Importation%20and%20Exportation%20of%20Pharmaceutical%20Products.pdf")],
            "Confirm Rwanda declaration and applicable national tariff measures; consult Rwanda FDA for regulated pharmaceuticals and verify exact destination and special zones.")
    };

    public static BwZmRwReference? Find(string? country)
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
