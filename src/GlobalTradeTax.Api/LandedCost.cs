namespace GlobalTradeTax.Api;

// Provider contract: a quote is complete only when all mandatory components are verified.
public sealed record RateEvidence(string Provider, string Reference, DateTimeOffset EffectiveAt,
    DateTimeOffset ExpiresAt, string Currency, string HsCode, string OriginCountry,
    string DestinationCountry);
public sealed record RateResult(decimal? Duty, decimal? Tax, decimal? ClearanceFee,
    string Status, RateEvidence? Evidence, string[] Warnings);

public interface IImportRateProvider
{
    Task<RateResult> GetRatesAsync(QuoteRequest request, CancellationToken cancellationToken);
}

// Safe default: never silently invent a tax or duty rate.
public sealed class UnconfiguredImportRateProvider : IImportRateProvider
{
    public Task<RateResult> GetRatesAsync(QuoteRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new RateResult(null, null, null, "provider_not_configured", null,
            ["No verified customs/tax data source configured for this destination."]));
}

public sealed record LandedCostBreakdown(decimal Merchandise, decimal Shipping, decimal Insurance,
    decimal? ImportDuty, decimal? ImportTax, decimal? ClearanceFee, decimal? Total,
    string Status, RateEvidence? Evidence, string[] Warnings)
{
    public ComplianceResult? Compliance { get; init; }
}

public sealed class LandedCostService(IImportRateProvider provider)
{
    /// <summary>
    /// Returns declared costs and a separately gated provider estimate. Declared control
    /// holds short-circuit provider access; financial verification never clears goods.
    /// The HTTP boundary must validate request shape and amount bounds first.
    /// </summary>
    public async Task<LandedCostBreakdown> CalculateAsync(QuoteRequest request, CancellationToken ct)
    {
        var merchandise = request.Lines.Sum(x => x.Quantity * x.UnitPrice);
        var compliance = ShipmentCompliance.Evaluate(request);
        if (compliance.SpecialistHoldRequired)
            return new(merchandise,request.ShippingCost,request.InsuranceCost,null,null,null,null,
                "manual_review_required",null,[..compliance.Warnings,
                    "Declared control hold: obtain specialist review before requesting a provider quote or completing payment."])
                { Compliance = compliance };
        var result = await provider.GetRatesAsync(request, ct);
        var verified = result.Status == "verified" && result.Evidence is not null &&
            result.Evidence.ExpiresAt > DateTimeOffset.UtcNow &&
            string.Equals(result.Evidence.Currency, request.Currency, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.Evidence.DestinationCountry, request.ShipTo, StringComparison.OrdinalIgnoreCase) &&
            result.Duty is >= 0 && result.Tax is >= 0 && result.ClearanceFee is >= 0 &&
            !string.IsNullOrWhiteSpace(result.Evidence.Provider) &&
            !string.IsNullOrWhiteSpace(result.Evidence.Reference) &&
            result.Evidence.EffectiveAt <= DateTimeOffset.UtcNow &&
            request.Lines.Count == 1 &&
            string.Equals(result.Evidence.OriginCountry, request.Lines[0].CountryOfOrigin, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(result.Evidence.HsCode, request.Lines[0].HsCode, StringComparison.OrdinalIgnoreCase);
        decimal? total = null;
        try
        {
            if (verified) total = checked(merchandise + request.ShippingCost + request.InsuranceCost +
                result.Duty!.Value + result.Tax!.Value + result.ClearanceFee!.Value);
        }
        catch (OverflowException) { verified = false; }
        return new(merchandise, request.ShippingCost, request.InsuranceCost,
            verified ? result.Duty : null, verified ? result.Tax : null,
            verified ? result.ClearanceFee : null, total,
            verified ? "verified" : "requires_provider_quote", verified ? result.Evidence : null,
            verified ? result.Warnings : [..result.Warnings, "Quote is not payable: missing or invalid verified rate evidence."])
            { Compliance = compliance };
    }
}
