namespace GlobalTradeTax.Api;

/// <summary>Non-payable source guidance; country alone does not establish territorial treatment.</summary>
public sealed record EuDestinationReference(string Country, string CountryName, string Status,
    string TerritoryStatus, bool VerifiedForCalculation, string ReferenceReviewedOn, string[] RequiredChecks, string[] Sources);

public static class EuDestinationReferences
{
    // EU membership snapshot, not a determination of customs/VAT territorial applicability.
    // Reviewed against the European Commission territorial scope page on 2026-11-08.
    private static readonly Dictionary<string, string> Countries = new(StringComparer.Ordinal)
    {
        ["AT"]="Austria", ["BE"]="Belgium", ["BG"]="Bulgaria", ["HR"]="Croatia",
        ["CY"]="Cyprus", ["CZ"]="Czechia", ["DK"]="Denmark", ["EE"]="Estonia",
        ["FI"]="Finland", ["FR"]="France", ["DE"]="Germany", ["GR"]="Greece",
        ["HU"]="Hungary", ["IE"]="Ireland", ["IT"]="Italy", ["LV"]="Latvia",
        ["LT"]="Lithuania", ["LU"]="Luxembourg", ["MT"]="Malta", ["NL"]="Netherlands",
        ["PL"]="Poland", ["PT"]="Portugal", ["RO"]="Romania", ["SK"]="Slovakia",
        ["SI"]="Slovenia", ["ES"]="Spain", ["SE"]="Sweden"
    };

    /// <summary>Returns guidance only for an exact two-letter ASCII member-state code.</summary>
    public static EuDestinationReference? Find(string? country)
    {
        if (country is not { Length: 2 } || !country.All(char.IsAsciiLetter)) return null;
        var code = country.ToUpperInvariant();
        if (!Countries.TryGetValue(code, out var name)) return null;
        return new(code, name, "reference_only", "unresolved", false, "2026-11-08",
            ["Determine the precise destination territory and customs and VAT scope",
             "Verify dated TARIC classification, origin, measures and conditions",
             "Verify the destination's current national VAT, excise and import procedures",
             "Review importer, valuation, preference, restrictions and applicable relief separately"],
            ["https://taxation-customs.ec.europa.eu/taxation/vat/vat-directive/how-does-vat-work/territorial-scope_en",
             "https://taxation-customs.ec.europa.eu/online-services/online-services-and-databases-customs/eu-customs-tariff-taric_en",
             "https://taxation-customs.ec.europa.eu/taxation/vat/vat-directive/vat-rates/country-specific-information-vat_en"]);
    }
}
