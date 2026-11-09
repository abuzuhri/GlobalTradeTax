using GlobalTradeTax.Api;
using Xunit;

public sealed class IndianChineseDestinationReferenceTests
{
    [Theory]
    [InlineData("IN", "India", "dgft.gov.in")]
    [InlineData("CN", "China", "chinatax.gov.cn")]
    public void National_guidance_is_unresolved_and_integrated(string country, string name, string domain)
    {
        var guidance = IndianChineseDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(name, guidance.Name);
        Assert.Equal("reference_only", guidance.Status);
        Assert.Equal("unresolved", guidance.TerritoryStatus);
        Assert.Contains(guidance.Sources, source => new Uri(source.Url).Host.EndsWith(domain));
        Assert.All(guidance.Sources, source => { Assert.Equal("2026-11-09", source.ReviewedOn); Assert.StartsWith("https://", source.Url); });
        Assert.Contains(guidance.UnresolvedChecks, c => c.Contains("origin") && c.Contains("preference"));
        Assert.Contains(guidance.UnresolvedChecks, c => c.Contains("postal") && c.Contains("courier"));
        Assert.Null(guidance.ImportDuty); Assert.Null(guidance.ImportTax); Assert.Null(guidance.LandedTotal);
        Assert.False(guidance.VerifiedForCalculation); Assert.False(guidance.PaymentApproved); Assert.False(guidance.ShipmentReleaseApproved);
        var policy = DestinationPolicies.Resolve(country);
        Assert.Equal("provider_required", policy.Status);
        Assert.Equal(guidance.Sources.Select(s => s.Url), policy.References);
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision);
        Assert.All(guidance.UnresolvedChecks, c => Assert.Contains(c, compliance.RequiredChecks));
        Assert.False(compliance.ShipmentReleaseApproved);
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
    }
    [Fact] public void China_does_not_decide_distinct_customs_territory() =>
        Assert.Contains(IndianChineseDestinationReference.Find("CN")!.UnresolvedChecks, c => c.Contains("distinct customs territory"));
    [Theory]
    [InlineData(null)][InlineData("")][InlineData(" IN")][InlineData("IND")][InlineData("ÅN")][InlineData("HK")][InlineData("MO")][InlineData("TW")][InlineData("US")]
    public void Unsupported_or_malformed_codes_are_not_found(string? country) => Assert.Null(IndianChineseDestinationReference.Find(country));
}
