namespace GlobalTradeTax.Api;

public sealed record ComplianceResult(string Route, string Decision, string[] RequiredChecks, string[] Warnings);
public static class ShipmentCompliance
{
    public static ComplianceResult Evaluate(QuoteRequest request)
    {
        var checks = new List<string> { "Confirm HS classification and actual country of origin", "Confirm importer of record and destination import restrictions", "Check denied-party, sanctions and export-control requirements" };
        var warnings = new List<string>();
        if (request.ShipFrom.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            checks.Add("Validate Schedule B and EEI/AES applicability by classification and USD value");
            checks.Add("Check ECCN/EAR and ITAR before shipping controlled goods");
            if (request.ItarControlled || request.RequiresExportLicense)
                warnings.Add("Export-control flag set: hold for specialist review; do not automate clearance.");
        }
        if (request.ShipTo.Equals("US", StringComparison.OrdinalIgnoreCase))
            checks.Add("Check HTSUS, additional duties, CBP entry, origin and agency admissibility");
        if (request.ShipTo is "DE" or "FR" or "SE" or "NL" or "ES" or "IT")
            checks.Add("Assess EU VAT/IOSS eligibility separately from customs duties and clearance fees");
        return new(request.ShipFrom.ToUpperInvariant()+"->"+request.ShipTo.ToUpperInvariant(),
            warnings.Count > 0 ? "manual_review_required" : "checks_required", checks.ToArray(), warnings.ToArray());
    }
}
