using GlobalTradeTax.Api;
using Xunit;

public sealed class JpKrSgDestinationReferenceTests
{
    [Theory]
    [InlineData("JP", "customs.go.jp")][InlineData("KR", "customs.go.kr")][InlineData("SG", "customs.gov.sg")]
    public void Country_guidance_requires_shipment_review(string country, string domain)
    {
        var result = JpKrSgDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal("reference_only", result.Status);
        Assert.True(result.Sources.Length >= 4);
        Assert.All(result.Sources, source => { Assert.Contains(domain, new Uri(source.Url).Host); Assert.Equal("2026-11-09", source.ReviewedOn); });
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("actual goods origin"));
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("preferential eligibility"));
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        Assert.Equal(result.Sources.Select(s => s.Url), DestinationPolicies.Resolve(country).References);
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision); Assert.False(compliance.ShipmentReleaseApproved);
        Assert.All(result.UnresolvedChecks, check => Assert.Contains(check, compliance.RequiredChecks));
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
        result.Sources[0] = result.Sources[1];
        Assert.NotEqual(result.Sources[0], JpKrSgDestinationReference.Find(country)!.Sources[0]);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("JPN")][InlineData("HK")][InlineData("TW")][InlineData("KP")][InlineData(" JP")]
    public void Rejects_invalid_and_other_destinations(string? country) => Assert.Null(JpKrSgDestinationReference.Find(country));
}
