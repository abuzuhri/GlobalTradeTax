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
        new("eu-taric", "EU TARIC", "https://taxation-customs.ec.europa.eu/online-services/online-services-and-databases-customs/eu-customs-tariff-taric_en",
            "EU", "official tariff reference", "reference-only", "TARIC alone does not cover all national VAT, excise or clearance fees."),
        new("us-hts", "USITC HTS", "https://hts.usitc.gov/", "US", "official import tariff reference",
            "reference-only", "Duty depends on origin, measures and current entry context."),
        new("jp-customs", "Japan Customs", "https://www.customs.go.jp/english/tariff/", "JP", "national tariff reference", "reference-only", "Shipment applicability, rights and current rates require independent review."),
        new("kr-customs", "Korea Customs Service", "https://www.customs.go.kr/english/ad/ct/CustomsTariffList.do", "KR", "national tariff reference", "reference-only", "Shipment applicability, rights and current rates require independent review."),
        new("sg-customs", "Singapore Customs", "https://www.customs.gov.sg/doing-business/import-operations/import-operations-overview/", "SG", "national import reference", "reference-only", "Shipment applicability, rights and current rates require independent review."),
        new("ca-customs-tariff", "CBSA Customs Tariff", "https://www.cbsa-asfc.gc.ca/trade-commerce/tariff-tarif/2026/menu-eng.html", "CA", "official tariff reference", "reference-only", "Classification, origin, dated measures and provincial tax require separate review."),
        new("mx-classification", "SAT tariff classification and NICO", "https://wwwmat.sat.gob.mx/tramites/71719/presenta-tu-consulta-de-clasificacion-arancelaria", "MX", "official classification reference", "reference-only", "A classification consultation is not a rate feed or clearance."),
        new("wits", "World Bank WITS", "https://wits.worldbank.org/", "Global", "trade tariff research",
            "reference-only", "Not a real-time checkout duty guarantee; check licensing and update frequency.")
    ];

    public static TradeDataSource? Find(string id) =>
        Sources.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
}
