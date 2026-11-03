namespace GlobalTradeTax.Api;

// Provenance registry: documentation/reference entries are NOT live rate providers.
public sealed record TradeDataSource(string Id, string Name, string Url, string Coverage,
    string DataType, string IntegrationStatus, string Warning,
    string? RepositoryUrl = null, string? DocumentationUrl = null,
    string? License = null, string? Attribution = null, string? Usage = null);

public static class TradeDataCatalog
{
    public static readonly TradeDataSource[] Sources =
    [
        new("uk-trade-tariff", "UK Trade Tariff Backend", "https://github.com/trade-tariff/trade-tariff-backend",
            "GB / Northern Ireland", "commodity codes, measures, quotas, origin rules and tariff API architecture",
            "verified-open-source-reference", "UK/NI scope only. MIT code license does not grant rights to every upstream dataset; no live rates are imported.",
            "https://github.com/trade-tariff/trade-tariff-backend",
            "https://docs.trade-tariff.service.gov.uk/", "MIT",
            "UK Trade Tariff Backend contributors (https://github.com/trade-tariff/trade-tariff-backend)",
            "Architecture and API integration research; review dataset licensing and API terms before any commercial ingestion."),
        new("eu-taric", "EU TARIC", "https://taxation-customs.ec.europa.eu/customs-4/calculation-customs-duties/customs-tariff/eu-customs-tariff-taric_en",
            "EU", "official tariff reference", "reference-only", "TARIC alone does not cover all national VAT, excise or clearance fees."),
        new("us-hts", "USITC HTS", "https://hts.usitc.gov/", "US", "official import tariff reference",
            "reference-only", "Duty depends on origin, measures and current entry context."),
        new("wits", "World Bank WITS", "https://wits.worldbank.org/", "Global", "trade tariff research",
            "reference-only", "Not a real-time checkout duty guarantee; check licensing and update frequency.")
    ];

    public static TradeDataSource? Find(string id) =>
        Sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
}
