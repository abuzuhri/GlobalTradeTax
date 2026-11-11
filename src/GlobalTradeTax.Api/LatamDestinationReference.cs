namespace GlobalTradeTax.Api;

public sealed record LatamSource(string Authority, string Purpose, string Url, string ReviewedOn);
public sealed record LatamReference(string Country, string Name, string Status, LatamSource[] Sources, string[] UnresolvedChecks)
{
    public decimal? ImportDuty => null;
    public decimal? ImportTax => null;
    public decimal? LandedTotal => null;
    public bool VerifiedForCalculation => false;
    public bool ShipmentReleaseApproved => false;
    public bool PaymentApproved => false;
}

/// <summary>Provides national source pointers, not rates or eligibility determinations.</summary>
public static class LatamDestinationReference
{
    private static readonly Dictionary<string, (string Name, LatamSource[] Sources, string Detail)> Countries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AR"] = ("Argentina", [new("ARCA", "customs code and importer context", "https://biblioteca.arca.gob.ar/dcp/LEY_C_022415_1981_03_02", "2026-11-09"), new("ARCA", "general import procedure for international shipments", "https://www.arca.gob.ar/envios-internacionales/regimen-general/", "2026-11-09")], "Review exact customs territory, including special customs areas such as Tierra del Fuego, and applicable national and provincial treatment."),
        ["UY"] = ("Uruguay", [new("Dirección Nacional de Aduanas", "import tax and tariff context", "https://www.aduanas.gub.uy/innovaportal/v/2755/8/innova.front/regimen-impositivo-a-la-importacion-.html", "2026-11-09"), new("Dirección Nacional de Aduanas", "digital import declaration procedure", "https://www.aduanas.gub.uy/innovaportal/file/10163/1/procedimiento_dua_digital_-_importacion.pdf", "2026-11-09"), new("Dirección Nacional de Aduanas", "special border trade restrictions", "https://www.aduanas.gub.uy/innovaportal/v/28040/1/innova.front/", "2026-11-09")], "Review exact destination, free zones and any special border trade regime independently."),
        ["PY"] = ("Paraguay", [new("DNIT", "importer and broker registration guidance index", "https://ekuatia.set.gov.py/es/web/portal-institucional/personas-vinculadas-a-la-actividad-aduanera", "2026-11-09"), new("DNIT", "import permits and VUI guidance index", "https://www.dnit.gov.py/en/web/portal-institucional/vui", "2026-11-09"), new("Ministerio de Industria y Comercio", "selected product import procedures", "https://www.mic.gov.py/tramites-para-importaciones/", "2026-11-09")], "Review exact destination, free zones and applicable national and local treatment independently."),
        ["BR"] = ("Brazil", [new("Receita Federal", "import tax and administrative treatment", "https://www.gov.br/pt-br/servicos/simular-o-tratamento-tributario-e-administrativo-de-importacoes", "2026-11-09"), new("Siscomex", "tariff classification and federal and state taxes", "https://www.gov.br/siscomex/pt-br/informacoes/perguntas-frequentes/acordos-comerciais/7-aspectos-tarifarios", "2026-11-09"), new("Siscomex", "import controls and licensing", "https://www.gov.br/siscomex/pt-br/informacoes/tratamento-administrativos/tratamento-administrativo-na-importacao", "2026-11-09")], "Review federal taxes, state ICMS, destination state and special programs independently."),
        ["CL"] = ("Chile", [new("Servicio Nacional de Aduanas", "tariff classification rules", "https://www.aduana.cl/aduana/site/docs/20070227/20070227234303/02_reglas_generales.pdf", "2026-11-09"), new("Servicio de Impuestos Internos", "indirect taxes", "https://www.sii.cl/aprenda_sobre_impuestos/impuestos/impuestos_indirectos.htm", "2026-11-09"), new("Servicio Nacional de Pesca y Acuicultura", "example product control", "https://www.chileatiende.gob.cl/fichas/4648-autorizacion-de-ingreso-para-productos-pesqueros-y-alimentos-destinados-a-especies-hidrobiologicas", "2026-11-09")], "Review destination free zones and special territorial treatment independently."),
        ["CO"] = ("Colombia", [new("DIAN", "customs tariff consultation", "https://importacionescarga.dian.gov.co/WebArancel/DefMenuConsultas.faces", "2026-11-09"), new("DIAN", "customs taxes and import requirements", "https://www.dian.gov.co/tramitesservicios/tramites-y-servicios/Paginas/tramites-aduaneros.aspx", "2026-11-09")], "Review special customs territories including Leticia and local treatment independently."),
        ["PE"] = ("Peru", [new("SUNAT", "tariff and import taxes", "https://www.sunat.gob.pe/orientacionaduanera/pagosgarantias/", "2026-11-09"), new("SUNAT", "import procedure", "https://www.sunat.gob.pe/orientacionaduanera/importacion/", "2026-11-09"), new("SUNAT", "restricted goods and competent authorities", "https://www.sunat.gob.pe/orientacionaduanera/mercanciasrestringidas/", "2026-11-09")], "Review special zones and exact destination territory independently.")
    };

    /// <summary>Accepts two ASCII country letters; unsupported or malformed inputs return null.</summary>
    public static LatamReference? Find(string? country)
    {
        if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetter) || !Countries.TryGetValue(country, out var item)) return null;
        return new(country.ToUpperInvariant(), item.Name, "reference_only", item.Sources,
            ["Verify commodity classification and actual goods origin, including preference evidence and agreement eligibility", "Confirm customs valuation, applicable taxes, duties and fees", "Review importer registration, broker responsibilities and import procedure", "Review product controls, permits and prohibited goods with competent authorities", "Verify postal, courier or low-value regime and collection responsibilities for this shipment", "Confirm exact destination territory and any subnational or special-zone treatment", item.Detail]);
    }
}
