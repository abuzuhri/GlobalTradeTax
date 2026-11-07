namespace GlobalTradeTax.Api;

public record QuoteLine(string Sku, string HsCode, string CountryOfOrigin, int Quantity, decimal UnitPrice);
public record QuoteRequest(string ShipFrom, string ShipTo, string Currency, string Incoterm,
    List<QuoteLine> Lines, decimal ShippingCost = 0, decimal InsuranceCost = 0,
    bool RequiresExportLicense = false, bool ItarControlled = false);
public record QuoteResponse(string QuoteId, string Status, decimal MerchandiseValue, decimal Freight,
    decimal Insurance, decimal? ImportDuty, decimal? ImportTax, decimal? LandedTotal,
    bool? EeiMayBeRequired, string RuleVersion, string[] Warnings, string[] RequiredActions)
{
    public ComplianceResult? Compliance { get; init; }
}

public sealed class QuoteService
{
    /// <summary>
    /// Rejects malformed, unbounded or overflowing input before storage or provider use.
    /// Format checks do not establish classification, country membership or licensing.
    /// </summary>
    public static Dictionary<string, string[]> Validate(QuoteRequest? r)
    {
        var e = new Dictionary<string, string[]>();
        if (r is null) { e["request"] = ["Quote request required."]; return e; }
        if (!HasCountryShape(r.ShipFrom)) e["shipFrom"] = ["Use ISO 3166-1 alpha-2 letters."];
        if (!HasCountryShape(r.ShipTo)) e["shipTo"] = ["Use ISO 3166-1 alpha-2 letters."];
        if (string.Equals(r.ShipFrom, r.ShipTo, StringComparison.OrdinalIgnoreCase)) e["route"] = ["International route required."];
        if (r.Currency is not { Length: 3 } || !r.Currency.All(char.IsAsciiLetter)) e["currency"] = ["Use ISO 4217 currency letters."];
        if (r.Lines is null || r.Lines.Count is < 1 or > 100) e["lines"] = ["One to 100 complete lines required."];
        else if (r.Lines.Any(x => x is null || x.Quantity <= 0 || x.UnitPrice < 0 || !HasReferenceShape(x.Sku,128) ||
            !HasReferenceShape(x.HsCode,32) || !HasCountryShape(x.CountryOfOrigin))) e["lines"] = ["Each line needs bounded SKU/HS code, origin letters, positive quantity and nonnegative price."];
        if (r.ShippingCost < 0 || r.InsuranceCost < 0) e["costs"] = ["Costs cannot be negative."];
        if (r.Incoterm is not ("DDP" or "DAP")) e["incoterm"] = ["MVP supports DDP or DAP."];
        if (!e.ContainsKey("lines") && !e.ContainsKey("costs"))
        {
            try
            {
                var merchandise = r.Lines!.Sum(x => checked(x.Quantity * x.UnitPrice));
                _ = checked(merchandise + r.ShippingCost + r.InsuranceCost);
            }
            catch (OverflowException) { e["amounts"] = ["Declared amounts exceed supported decimal arithmetic."]; }
        }
        return e;
    }
    /// <summary>
    /// Creates a reference quote from a validated request, preserving declared holds and
    /// preliminary EEI checks. It never supplies destination duty, tax or a landed total.
    /// </summary>
    public QuoteResponse Calculate(QuoteRequest r)
    {
        decimal merchandise = r.Lines.Sum(x => x.UnitPrice * x.Quantity);
        var warnings = new List<string> { "No authoritative destination tariff/tax provider configured; duty, tax and landed total are intentionally null.",
            "HS classification, origin, customs value, FX, and product restrictions require independent validation." };
        var actions = new List<string> { "Connect a licensed tariff and tax data provider", "Confirm importer of record and customs broker", "Check export controls and destination restrictions" };
        var compliance = ShipmentCompliance.Evaluate(r);
        if (compliance.SpecialistHoldRequired)
        {
            warnings.AddRange(compliance.Warnings);
            actions.Add("Hold for specialist review of declared export controls; no automated release or payment completion.");
        }
        bool? eei = null;
        if (r.ShipFrom.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            if (!r.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
                warnings.Add("US EEI screening requires USD customs values and appropriate valuation adjustments.");
            else
            {
                eei = r.RequiresExportLicense || r.ItarControlled || r.Lines.GroupBy(x => x.HsCode)
                    .Any(g => g.Sum(x => x.Quantity * x.UnitPrice) > 2500m);
                if (eei == true) actions.Add("Review EEI/AES filing with authorized exporter or agent before shipment; HS codes alone do not establish Schedule B classification");
                else warnings.Add("EEI screen is preliminary: Schedule B classification (not necessarily HS), destination, controlled goods, exemptions, and aggregation rules require review.");
            }
        }
        if (r.ShipTo.Equals("US", StringComparison.OrdinalIgnoreCase))
            actions.Add("Validate HTSUS, country of origin, Section 301/232 and current US import requirements");
        if (r.ShipFrom.Equals("US", StringComparison.OrdinalIgnoreCase))
        {
            var policy = DestinationPolicies.Resolve(r.ShipTo);
            actions.AddRange(policy.ComplianceChecks);
            warnings.Add($"Destination policy: {policy.Region}; taxes and duties unavailable until a validated data provider is connected.");
        }
        return new(Guid.NewGuid().ToString("N"), compliance.SpecialistHoldRequired ? "manual_review_required" : "requires_provider_quote", merchandise, r.ShippingCost,
            r.InsuranceCost, null, null, null, eei, "unconfigured", warnings.ToArray(), actions.ToArray())
            { Compliance = compliance };
    }

    /// <summary>Checks ASCII country-code syntax without asserting country membership.</summary>
    private static bool HasCountryShape(string? value) => value is { Length: 2 } && value.All(char.IsAsciiLetter);

    /// <summary>Bounds an untrusted reference string and excludes control characters.</summary>
    private static bool HasReferenceShape(string? value,int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);
}
