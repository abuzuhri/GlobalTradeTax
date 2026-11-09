namespace GlobalTradeTax.Api;

public sealed record NationalReferenceSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record NationalDestinationReference(string Country, string Name, string Status, NationalReferenceSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool ShipmentReleaseApproved => false;
    public bool PaymentApproved => false;
}

/// <summary>Static official source pointers for three destinations; no shipment eligibility or rates are inferred.</summary>
public static class JpKrSgDestinationReference
{
    private const string Review = "2026-11-09";
    private static NationalReferenceSource Source(string authority, string purpose, string url) => new(authority, purpose, url, Review);
    private static readonly Dictionary<string, (string Name, NationalReferenceSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JP"] = ("Japan", [
            Source("Japan Customs", "tariff schedule", "https://www.customs.go.jp/english/tariff/"),
            Source("Japan Customs", "consumption and other import taxes", "https://www.customs.go.jp/english/c-answer_e/imtsukan/1701_e.htm"),
            Source("Japan Customs", "import permit, importer and bonded area", "https://www.customs.go.jp/english/procedures/m49040/fg.htm"),
            Source("Japan Customs", "prohibited and restricted goods", "https://www.customs.go.jp/english/summary/prohibit.htm")],
            "Verify the bonded area, destination and movement, and national and local consumption tax treatment."),
        ["KR"] = ("South Korea", [
            Source("Korea Customs Service", "tariff classification database", "https://www.customs.go.kr/english/ad/ct/CustomsTariffList.do"),
            Source("Korea Customs Service", "import duty and internal tax declaration", "https://www2.customs.go.kr/english/cm/cntnts/cntntsView.do?cntntsId=2736&mi=8060"),
            Source("Korea Customs Service", "import clearance, controls and bonded areas", "https://customs.go.kr/english/cm/cntnts/cntntsView.do?cntntsId=2731&mi=8055"),
            Source("Korea Customs Service", "VAT and controlled express goods", "https://www.customs.go.kr/english/cm/cntnts/cntntsView.do?cntntsId=2733")],
            "Verify the destination, bonded-area movement and applicable import VAT and other internal taxes."),
        ["SG"] = ("Singapore", [
            Source("Singapore Customs", "classification, importer declaration and controls", "https://www.customs.gov.sg/doing-business/import-operations/import-operations-overview/"),
            Source("Singapore Customs", "import GST", "https://www.customs.gov.sg/doing-business/valuation-duties-and-fees/goods-and-services-tax-gst/"),
            Source("Singapore Customs", "competent authority controls and permits", "https://www.customs.gov.sg/doing-business/quick-links-for-traders/tradenet/competent-authorities-requirements-for-controlled-items/competent-authorities-requirements-for-controlled-items-overview/"),
            Source("Singapore Customs", "free trade zone movements", "https://www.customs.gov.sg/doing-business/import-operations/import-procedures/depositing-goods-in-free-trade-zones/")],
            "Verify the exact destination, free trade zone and movement into customs territory or consumption."),
    };

    /// <summary>Accepts two ASCII letters case-insensitively; malformed and unsupported codes return null.</summary>
    public static NationalDestinationReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only", [.. item.Sources],
            ["Verify product classification and actual goods origin; shipping country does not establish origin",
             "Verify preferential eligibility and proof independently; agreement membership and country code do not establish preference",
             "Confirm customs valuation, import procedure, duties, consumption taxes and other fees",
             "Confirm importer and declarant responsibilities, documents and broker arrangements",
             "Review product controls, prohibitions and required permits with competent authorities",
             "Confirm exact destination, territorial scope and special or bonded zone treatment for this movement", item.Detail]);
    }
}
