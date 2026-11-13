namespace GlobalTradeTax.Api;

/// <summary>National commercial-import references, without eligibility or rate determinations.</summary>
public static class PkBdLkDestinationReference
{
    private static NationalReferenceSource Source(string authority, string purpose, string url) => new(authority, purpose, url, "2026-11-12");
    private static readonly Dictionary<string, (string Name, NationalReferenceSource[] Sources)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PK"] = ("Pakistan", [
            Source("Federal Board of Revenue", "customs tariff index", "https://www.fbr.gov.pk/categ/laws/144"),
            Source("Federal Board of Revenue", "import sales tax context", "https://www.fbr.gov.pk/sales-tax/51148/101149"),
            Source("Federal Board of Revenue", "import procedures and restrictions; historical policy context", "https://www.fbr.gov.pk/categ/imports-exports/51149/70850/131173")]),
        ["BD"] = ("Bangladesh", [
            Source("Bangladesh Customs, National Board of Revenue", "commercial import registration and documents", "https://hub.bangladeshcustoms.gov.bd/hs-code/details/description/general-documents"),
            Source("Bangladesh Customs, National Board of Revenue", "tariff, tax and regulatory information hub", "https://hub.bangladeshcustoms.gov.bd/faq"),
            Source("Bangladesh Customs, National Board of Revenue", "customs act prohibited goods excerpt", "https://hub.bangladeshcustoms.gov.bd/storage/resource/1748855078_Import%20%26%20Export%20Prohibited%20Goods%20-%20Customs%20Act%2C%202023%20%28English%29.pdf")]),
        ["LK"] = ("Sri Lanka", [
            Source("Sri Lanka Customs", "import tariff and control list index", "https://www.customs.gov.lk/customs-tariff/import-tariff/"),
            Source("Sri Lanka Customs", "commercial import declaration and charges", "https://www.customs.gov.lk/services/importing-goods/"),
            Source("Sri Lanka Customs", "importer registration and product controls", "https://www.customs.gov.lk/about-us/directorates-and-divisions/declaration-directorate/")]),
    };

    /// <summary>Accepts exactly two ASCII letters; unsupported codes return null.</summary>
    public static NationalDestinationReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only", [.. item.Sources],
            ["Verify product classification and actual goods origin; shipping country does not establish origin",
             "Verify preferential eligibility and proof independently; country code and agreements do not establish preference",
             "Confirm customs valuation, import regime, applicable duties, taxes and fees",
             "Confirm importer registration, declarant responsibilities, documents and broker arrangements",
             "Review product permits, prohibitions and competent authorities for the actual goods",
             "Verify postal, courier and low-value treatment for the actual shipment",
             "Confirm exact territory, subnational and special or bonded zone treatment for this movement"]);
    }
}
