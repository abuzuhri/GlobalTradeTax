namespace GlobalTradeTax.Api;

public sealed record AuNzSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record AuNzReference(string Country, string Name, string Status, AuNzSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool ShipmentReleaseApproved => false;
    public bool PaymentApproved => false;
}

/// <summary>Provides AU and NZ source pointers for independent review, not applicable rates or clearance.</summary>
public static class AuNzDestinationReference
{
    private const string ReviewDate = "2026-11-09";
    private static AuNzSource Source(string authority, string purpose, string url) => new(authority, purpose, url, ReviewDate);
    private static readonly Dictionary<string, AuNzReference> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AU"] = new("AU", "Australia", "reference_only",
        [Source("Australian Border Force", "working customs tariff", "https://www.abf.gov.au/importing-exporting-and-manufacturing/tariff-classification/current-tariff"),
         Source("Australian Border Force", "import requirements and valuation", "https://www.abf.gov.au/imports/Pages/How-to-import/Requirements.aspx"),
         Source("Australian Taxation Office", "GST and imported goods", "https://www.ato.gov.au/api/public/content/0-5cfa3e60-d95b-41aa-9be1-cea62009de33"),
         Source("Department of Agriculture, Fisheries and Forestry", "biosecurity and imported food", "https://www.agriculture.gov.au/biosecurity-trade/import/online-services/bicon"),
         Source("Australian Border Force", "external territory customs scope", "https://www.abf.gov.au/help-and-support-subsite/CustomsNotices/2026-11.pdf")],
        ["Verify commodity classification and actual goods origin; dispatch country does not establish origin", "Review free trade agreement eligibility, applicable origin rules and proof independently", "Confirm customs valuation, import procedures, fees and importer and broker responsibilities", "Review GST registration, collection responsibility and transaction treatment", "Check Australian biosecurity, food and other product controls and permits", "Verify exact destination and external territory customs, tax and biosecurity scope independently"]),
        ["NZ"] = new("NZ", "New Zealand", "reference_only",
        [Source("New Zealand Customs Service", "customs tariff and classification", "https://www.customs.govt.nz/business/tariffs/tariff-classifications-and-rates"),
         Source("New Zealand Customs Service", "import duty, valuation and fees", "https://www.customs.govt.nz/sending-and-receiving/duty-and-gst/duty-and-allowances"),
         Source("Inland Revenue", "GST registration for overseas businesses", "https://www.ird.govt.nz/gst/gst-for-overseas-businesses/supplying-goods-or-services-in-new-zealand"),
         Source("Ministry for Primary Industries", "import procedures and biosecurity", "https://www.mpi.govt.nz/import/importing-into-nz-how-it-works/general-importing-requirements"),
         Source("Ministry of Foreign Affairs and Trade", "treaty territorial scope", "https://www.mfat.govt.nz/assets/About-us-Corporate/Treaties-Model-instruments/International-Treaty-Making-Guide-2021.pdf")],
        ["Verify commodity classification and actual goods origin; dispatch country does not establish origin", "Review free trade agreement eligibility, applicable origin rules and proof independently", "Confirm customs valuation, import procedures, fees and importer and broker responsibilities", "Review GST registration, collection responsibility and transaction treatment", "Check New Zealand import health standards, food and other product controls and permits", "Verify exact destination and separately review Cook Islands, Niue and Tokelau jurisdiction and treaty scope"])
    };

    /// <summary>Accepts two ASCII letters in either case; all other codes return null.</summary>
    public static AuNzReference? Find(string? country) => country is not null && country.Length == 2 && country.All(char.IsAsciiLetter) && Countries.TryGetValue(country, out var result) ? result : null;
}
