using GlobalTradeTax.Api;
using Xunit;

public sealed class NoChIsDestinationReferenceTests
{
    [Theory]
    [InlineData("NO", "toll.no")][InlineData("CH", "bazg.admin.ch")][InlineData("IS", "skatturinn.is")]
    public void Country_guidance_requires_shipment_review(string country, string domain)
    {
        var result = NoChIsDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal("reference_only", result.Status);
        Assert.NotEmpty(result.Sources);
        Assert.All(result.Sources, source => { Assert.Contains(domain, new Uri(source.Url).Host); Assert.Equal("2026-11-12", source.ReviewedOn); });
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("actual goods origin"));
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("preference eligibility"));
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        Assert.Equal("OTHER", DestinationPolicies.Resolve(country).Region);
        Assert.Equal("provider_required", DestinationPolicies.Resolve(country).Status);
        Assert.Equal(result.Sources.Select(s => s.Url), DestinationPolicies.Resolve(country).References);
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("postal"));
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("territory"));
        Assert.All(result.Sources, source => Assert.False(string.IsNullOrWhiteSpace(source.Purpose)));
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision); Assert.False(compliance.ShipmentReleaseApproved);
        Assert.All(result.UnresolvedChecks, check => Assert.Contains(check, compliance.RequiredChecks));
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
        result.Sources[0] = null!;
        Assert.NotNull(NoChIsDestinationReference.Find(country)!.Sources[0]);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("NOR")][InlineData("LI")][InlineData("ZZ")][InlineData("N0")][InlineData(" NO")][InlineData("éS")]
    public void Rejects_invalid_and_other_destinations(string? country) => Assert.Null(NoChIsDestinationReference.Find(country));
}
