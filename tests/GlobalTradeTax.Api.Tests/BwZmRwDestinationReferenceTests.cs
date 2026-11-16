using GlobalTradeTax.Api;
using Xunit;

public sealed class BwZmRwDestinationReferenceTests
{
    [Theory]
    [InlineData("BW", "Botswana")][InlineData("ZM", "Zambia")][InlineData("RW", "Rwanda")]
    public void Country_guidance_requires_shipment_review(string country, string name)
    {
        var result = BwZmRwDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal("reference_only", result.Status);
        Assert.NotEmpty(result.Sources);
        Assert.Equal(name, result.Name);
        var hosts = country switch { "BW" => new[] { "ecustoms.burs.org.bw", "www.burs.org.bw", "www.gov.bw" }, "ZM" => new[] { "www.zra.org.zm", "www.zamra.co.zm" }, _ => new[] { "www.rra.gov.rw", "rwandafda.gov.rw" } };
        Assert.All(result.Sources, source => { var uri = new Uri(source.Url); Assert.Equal("https", uri.Scheme); Assert.Contains(uri.Host, hosts); });
        Assert.All(result.Sources, source => Assert.Equal("2026-11-16", source.ReviewedOn));
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
        var quote = new QuoteService().Calculate(request);
        Assert.Null(quote.ImportDuty); Assert.Null(quote.ImportTax); Assert.Null(quote.LandedTotal);
        result.Sources[0] = null!;
        Assert.NotNull(BwZmRwDestinationReference.Find(country)!.Sources[0]);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("BWW")][InlineData("BA")][InlineData("ZZ")][InlineData("N0")][InlineData(" BW")][InlineData("éS")]
    public void Rejects_invalid_and_other_destinations(string? country) => Assert.Null(BwZmRwDestinationReference.Find(country));
}
