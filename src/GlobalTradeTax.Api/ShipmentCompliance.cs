namespace GlobalTradeTax.Api;

public sealed record ComplianceResult(string Route, string Decision, string[] RequiredChecks, string[] Warnings)
{
    public string[] HoldReasons { get; init; } = [];
    public bool SpecialistHoldRequired => HoldReasons.Length > 0;
    public bool ShipmentReleaseApproved => false;
}
public static class ShipmentCompliance
{
    /// <summary>
    /// Collects route checks and caller-declared control holds. Neither missing flags
    /// nor a financial provider response can approve shipment release.
    /// </summary>
    public static ComplianceResult Evaluate(QuoteRequest request)
    {
        var checks = new List<string> { "Confirm HS classification and actual country of origin", "Confirm importer of record and destination import restrictions", "Check denied-party, sanctions and export-control requirements" };
        var warnings = new List<string>();
        var holds = new List<string>();
        if (request.RequiresExportLicense) holds.Add("declared_export_licence");
        if (request.ItarControlled) holds.Add("declared_itar_control");
        if (request.RequiresExportLicense || request.ItarControlled)
            warnings.Add("Export-control flag set: hold for specialist review; do not automate clearance.");
        if (request.ShipFrom.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add("Validate Schedule B and EEI/AES applicability by classification and USD value");
            checks.Add("Check ECCN/EAR and ITAR before shipping controlled goods");
        }
        if (request.ShipTo.Equals("US", StringComparison.OrdinalIgnoreCase))
            checks.Add("Check HTSUS, additional duties, CBP entry, origin and agency admissibility");
        if (EuDestinationReferences.Find(request.ShipTo) is not null)
            checks.AddRange(["Assess EU VAT/IOSS eligibility separately from customs duties and clearance fees",
                "Confirm the exact destination territory and its customs and VAT scope"]);
        if (NorthAmericaDestinationReference.Find(request.ShipTo) is { } northAmerica)
        {
            checks.AddRange(northAmerica.UnresolvedChecks);
            warnings.Add("US, Canada and Mexico guidance is reference only; tax, tariff and clearance require independent review.");
        }
        if (GccDestinationReference.Find(request.ShipTo) is { } gcc)
        {
            checks.AddRange(gcc.UnresolvedChecks);
            warnings.Add("GCC country guidance is reference only; national tax, tariff and clearance require independent review.");
        }
        if (JpKrSgDestinationReference.Find(request.ShipTo) is { } national)
        {
            checks.AddRange(national.UnresolvedChecks);
            warnings.Add("National guidance is reference only; tax, tariff and clearance require independent review.");
        }
        if (request.ShipTo.Equals("GB", StringComparison.OrdinalIgnoreCase) || request.ShipTo.Equals("XI", StringComparison.OrdinalIgnoreCase))
        {
            checks.AddRange(["Confirm the GB or Northern Ireland tariff, ten-digit import classification and actual goods origin",
                "Verify importer EORI, import procedure, customs valuation, currency basis and VAT collection regime",
                "Review dated measure conditions, certificate alternatives, sanctions, prohibitions and product controls",
                "Verify preference origin proof and quota entitlement/balance; neither is inferred from shipping country",
                "Confirm DDP/DAP responsibilities separately from statutory importer and payment obligations"]);
            warnings.Add("UK import clearance and payable tax require independently verified evidence; reference screening does not clear goods.");
            if (request.ShipTo.Equals("XI", StringComparison.OrdinalIgnoreCase))
                checks.Add("Review Northern Ireland movement, at-risk status, UKIMS authorisation and relevant Union/UK tariff and VAT rules separately");
        }
        return new(request.ShipFrom.ToUpperInvariant()+"->"+request.ShipTo.ToUpperInvariant(),
            warnings.Count > 0 ? "manual_review_required" : "checks_required", checks.ToArray(), warnings.ToArray())
            { HoldReasons = holds.ToArray() };
    }
}
