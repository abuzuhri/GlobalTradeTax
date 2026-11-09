using GlobalTradeTax.Api;
using Xunit;

public sealed class DestinationRegionContractTests
{
    [Theory]
    [InlineData("my", "OTHER")][InlineData("ID", "OTHER")][InlineData("TH", "OTHER")]
    [InlineData("VN", "OTHER")][InlineData("PH", "OTHER")][InlineData("IN", "OTHER")]
    [InlineData("CN", "OTHER")][InlineData("AU", "OTHER")][InlineData("NZ", "OTHER")]
    [InlineData("de", "EU")][InlineData("AE", "GCC")][InlineData("BR", "LATAM")]
    [InlineData("mx", "LATAM")][InlineData("AR", "LATAM")][InlineData("uy", "LATAM")][InlineData("PY", "LATAM")][InlineData("JP", "OTHER")]
    [InlineData("CA", "OTHER")][InlineData("ZZ", "OTHER")]
    public void Policy_and_quote_use_advertised_categories(string country, string expected)
    {
        var policy = DestinationPolicies.Resolve(country);
        Assert.Equal(expected, policy.Region);
        Assert.Equal(country.ToUpperInvariant(), policy.Country);
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var quote = new QuoteService().Calculate(request);
        Assert.Contains($"Destination policy: {expected}; taxes and duties unavailable until a validated data provider is connected.", quote.Warnings);
        Assert.All(policy.ComplianceChecks, check => Assert.Contains(check, quote.RequiredActions));
        Assert.Null(quote.ImportDuty); Assert.Null(quote.ImportTax); Assert.Null(quote.LandedTotal);
        Assert.False(quote.Compliance!.ShipmentReleaseApproved);
    }

    [Fact]
    public void Mexico_keeps_north_america_sources_and_checks_in_latam_category()
    {
        var reference = NorthAmericaDestinationReference.Find("MX")!;
        var policy = DestinationPolicies.Resolve("MX");
        Assert.Equal("LATAM", policy.Region);
        Assert.Equal(reference.Sources.Select(s => s.Url), policy.References);
        Assert.Equal(reference.UnresolvedChecks, policy.ComplianceChecks);
    }
}
