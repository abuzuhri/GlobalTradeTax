namespace GlobalTradeTax.Api;

public record QuoteLine(string Sku, string HsCode, string CountryOfOrigin, int Quantity, decimal UnitPrice);
public record QuoteRequest(string ShipFrom, string ShipTo, string Currency, string Incoterm,
    List<QuoteLine> Lines, decimal ShippingCost = 0, decimal InsuranceCost = 0,
    bool RequiresExportLicense = false, bool ItarControlled = false);
public record QuoteResponse(string QuoteId, string Status, decimal MerchandiseValue, decimal Freight,
    decimal Insurance, decimal? ImportDuty, decimal? ImportTax, decimal? LandedTotal,
    bool? EeiMayBeRequired, string RuleVersion, string[] Warnings, string[] RequiredActions);

public sealed class QuoteService
{
    public static Dictionary<string, string[]> Validate(QuoteRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.ShipFrom) || r.ShipFrom.Length != 2) e["shipFrom"] = ["Use ISO 3166-1 alpha-2 code."];
        if (string.IsNullOrWhiteSpace(r.ShipTo) || r.ShipTo.Length != 2) e["shipTo"] = ["Use ISO 3166-1 alpha-2 code."];
        if (string.Equals(r.ShipFrom, r.ShipTo, StringComparison.OrdinalIgnoreCase)) e["route"] = ["International route required."];
        if (string.IsNullOrWhiteSpace(r.Currency) || r.Currency.Length != 3) e["currency"] = ["Use ISO 4217 currency code."];
        if (r.Lines is null || r.Lines.Count == 0) e["lines"] = ["At least one line required."];
        else if (r.Lines.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || string.IsNullOrWhiteSpace(x.Sku) ||
            string.IsNullOrWhiteSpace(x.HsCode) || x.CountryOfOrigin?.Length != 2)) e["lines"] = ["Each line needs SKU, HS code, origin, positive quantity, nonnegative price."];
        if (r.ShippingCost < 0 || r.InsuranceCost < 0) e["costs"] = ["Costs cannot be negative."];
        if (r.Incoterm is not ("DDP" or "DAP")) e["incoterm"] = ["MVP supports DDP or DAP."];
        return e;
    }
    public QuoteResponse Calculate(QuoteRequest r)
    {
        decimal merchandise = r.Lines.Sum(x => x.UnitPrice * x.Quantity);
        var warnings = new List<string> { "No authoritative destination tariff/tax provider configured; duty, tax and landed total are intentionally null.",
            "HS classification, origin, customs value, FX, and product restrictions require independent validation." };
        var actions = new List<string> { "Connect a licensed tariff and tax data provider", "Confirm importer of record and customs broker", "Check export controls and destination restrictions" };
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
        return new(Guid.NewGuid().ToString("N"), "requires_provider_quote", merchandise, r.ShippingCost,
            r.InsuranceCost, null, null, null, eei, "unconfigured", warnings.ToArray(), actions.ToArray());
    }
}
