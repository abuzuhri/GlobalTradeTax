using GlobalTradeTax.Api;
using Xunit;

public sealed class AfricanFourDestinationReferenceTests
{
    [Theory]
    [InlineData("ZA", "South Africa", "SACU")]
    [InlineData("KE", "Kenya", "EAC")]
    [InlineData("NG", "Nigeria", "ECOWAS")]
    [InlineData("EG", "Egypt", "advance")]
    public void Guidance_is_country_specific_and_financially_closed(string country, string name, string detail)
    {
        var result = AfricanFourDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal(name, result.Name); Assert.Equal("reference_only", result.Status);
        Assert.All(result.Sources, source => { Assert.Equal("2026-11-10", source.ReviewedOn); Assert.False(string.IsNullOrWhiteSpace(source.Purpose)); Assert.Equal("https", new Uri(source.Url).Scheme); });
        Assert.Contains(result.UnresolvedChecks, x => x.Contains(detail, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.UnresolvedChecks, x => x.Contains("actual goods origin"));
        Assert.Contains(result.UnresolvedChecks, x => x.Contains("postal"));
        Assert.Contains(result.UnresolvedChecks, x => x.Contains("responsible agencies"));
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        var policy = DestinationPolicies.Resolve(country);
        Assert.Equal("OTHER", policy.Region); Assert.Equal("provider_required", policy.Status);
        Assert.Equal(result.Sources.Select(x => x.Url), policy.References);
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision); Assert.False(compliance.ShipmentReleaseApproved);
        Assert.All(result.UnresolvedChecks, check => Assert.Contains(check, compliance.RequiredChecks));
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
        result.Sources[0] = new("changed", "changed", "https://example.com", "changed");
        Assert.NotEqual(result.Sources[0], AfricanFourDestinationReference.Find(country)!.Sources[0]);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData(" Z")][InlineData("ZA ")][InlineData("ZAF")][InlineData("GH")][InlineData("éG")]
    public void Rejects_invalid_or_unsupported_codes(string? country) => Assert.Null(AfricanFourDestinationReference.Find(country));
}
