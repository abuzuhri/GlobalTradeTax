namespace GlobalTradeTax.Api;

public sealed record DestinationPolicy(string Country, string Region, string TaxScheme,
    string DutyScheme, string Status, string[] ComplianceChecks, string[] References);

// Routing metadata, not tax rates. Real quotes require licensed, effective-dated data.
public static class DestinationPolicies
{
    private static readonly HashSet<string> Gcc = new(StringComparer.OrdinalIgnoreCase)
    { "SA", "AE", "BH", "OM", "QA", "KW" };
    private static readonly HashSet<string> Latam = new(StringComparer.OrdinalIgnoreCase)
    { "BR", "MX", "CL", "CO", "AR", "PE" };

    public static DestinationPolicy Resolve(string country)
    {
        country = country.Trim().ToUpperInvariant();
        if (EuDestinationReferences.Find(country) is not null) return new(country, "EU", "Destination VAT / IOSS eligibility",
            "EU customs tariff and applicable low-value rules", "provider_required",
            ["Validate IOSS eligibility and seller registration", "Verify EU tariff classification and origin", "Check VAT rate, customs value and import fees", "Check precise destination territory, VAT scope and product restrictions"],
            ["https://taxation-customs.ec.europa.eu/ioss_en", "https://taxation-customs.ec.europa.eu/online-services/online-services-and-databases-customs/eu-customs-tariff-taric_en"]);
        if (Gcc.Contains(country)) return new(country, "GCC", "Country-specific VAT where applicable",
            "Country-specific customs tariff", "provider_required",
            ["Confirm importer of record and broker", "Check local VAT applicability and customs exemptions", "Validate product approvals and restricted goods"],
            GccDestinationReference.Find(country)!.Sources.Select(source => source.Url).ToArray());
        if (JpKrSgDestinationReference.Find(country) is { } national) return new(country, "OTHER", "Country-specific consumption tax",
            "Country-specific customs tariff", "provider_required", national.UnresolvedChecks, national.Sources.Select(source => source.Url).ToArray());
        if (NorthAmericaDestinationReference.Find(country) is { } northAmerica)
            return new(country, country == "MX" ? "LATAM" : "OTHER", "Destination-specific consumption and local taxes",
                "Country-specific customs tariff", "provider_required", northAmerica.UnresolvedChecks,
                northAmerica.Sources.Select(source => source.Url).ToArray());
        if (Latam.Contains(country)) return new(country, "LATAM", country == "BR" ? "Brazil ICMS / applicable remittance rules" : "Country-specific import tax",
            "Country-specific customs tariff", "provider_required",
            ["Confirm local importer and clearance regime", "Validate current import tax and de minimis rules", "Confirm carrier collection/remittance support"],
            country == "BR" ? ["https://www.gov.br/receitafederal/pt-br"] : []);
        return new(country, "OTHER", "Destination-specific tax", "Destination-specific customs tariff",
            "provider_required", ["Check import eligibility and restrictions", "Obtain tariff/tax quote from authoritative provider", "Confirm customs broker and importer"], []);
    }
}
