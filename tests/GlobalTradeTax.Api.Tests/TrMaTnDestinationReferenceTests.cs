using GlobalTradeTax.Api;
using Xunit;

public sealed class TrMaTnDestinationReferenceTests
{
    [Theory]
    [InlineData("TR", "ticaret.gov.tr")][InlineData("MA", "mcinet.gov.ma")][InlineData("TN", "douane.gov.tn")]
    public void Country_guidance_requires_shipment_review(string country, string domain)
    {
        var result = TrMaTnDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal("reference_only", result.Status);
        Assert.True(result.Sources.Length >= 2);
        Assert.Contains(domain, new Uri(result.Sources[0].Url).Host);
        Assert.All(result.Sources, source => { Assert.NotEmpty(source.Purpose); Assert.Equal("2026-11-12", source.ReviewedOn); });
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("actual goods origin"));
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("preference eligibility"));
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        Assert.Equal("OTHER", DestinationPolicies.Resolve(country).Region);
        Assert.Equal("provider_required", DestinationPolicies.Resolve(country).Status);
        Assert.Equal(result.Sources.Select(s => s.Url), DestinationPolicies.Resolve(country).References);
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision); Assert.False(compliance.ShipmentReleaseApproved);
        Assert.All(result.UnresolvedChecks, check => Assert.Contains(check, compliance.RequiredChecks));
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
        result.Sources[0] = result.Sources[1];
        Assert.NotEqual(result.Sources[0], TrMaTnDestinationReference.Find(country)!.Sources[0]);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("TUR")][InlineData("HK")][InlineData("TR ")][InlineData("T1")][InlineData(" TR")]
    public void Rejects_invalid_and_other_destinations(string? country) => Assert.Null(TrMaTnDestinationReference.Find(country));
}
