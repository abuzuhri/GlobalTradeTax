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
        new("br-import-reference", "Brazil import reference", "https://www.gov.br/pt-br/servicos/simular-o-tratamento-tributario-e-administrativo-de-importacoes", "BR", "national customs and tax guidance", "reference-only", "Federal and state treatment and applicability require independent review; no live integration."),
        new("cl-import-reference", "Chile import reference", "https://www.sii.cl/aprenda_sobre_impuestos/impuestos/impuestos_indirectos.htm", "CL", "national tax guidance", "reference-only", "Customs, territory and applicability require independent review; no live integration."),
        new("co-import-reference", "Colombia import reference", "https://importacionescarga.dian.gov.co/WebArancel/DefMenuConsultas.faces", "CO", "national tariff guidance", "reference-only", "Taxes, territory and applicability require independent review; no live integration."),
        new("pe-import-reference", "Peru import reference", "https://www.sunat.gob.pe/orientacionaduanera/pagosgarantias/", "PE", "national import tax guidance", "reference-only", "Territory and applicability require independent review; no live integration."),
        new("au-customs-tariff", "Australian Border Force working tariff", "https://www.abf.gov.au/importing-exporting-and-manufacturing/tariff-classification/current-tariff", "Australia; external territories require separate review", "official customs reference", "reference-only", "No rates are imported; origin, territory, tax and data rights require review."),
        new("nz-customs-tariff", "New Zealand Customs tariff", "https://www.customs.govt.nz/business/tariffs/tariff-classifications-and-rates", "New Zealand; associated jurisdictions require separate review", "official customs reference", "reference-only", "No rates are imported; origin, territory, tax and data rights require review."),
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
