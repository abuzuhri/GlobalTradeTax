using GlobalTradeTax.Api;
using Xunit;

public sealed class BaMeMdDestinationReferenceTests
{
    [Theory]
    [InlineData("BA", "uino.gov.ba")][InlineData("ME", "wapi.gov.me")][InlineData("MD", "customs.gov.md")]
    public void Country_guidance_requires_shipment_review(string country, string domain)
    {
        var result = BaMeMdDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal("reference_only", result.Status);
        Assert.NotEmpty(result.Sources);
        Assert.Contains(result.Sources, source => new Uri(source.Url).Host.Contains(domain));
        Assert.All(result.Sources, source => Assert.Equal("2026-11-13", source.ReviewedOn));
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
        Assert.NotNull(BaMeMdDestinationReference.Find(country)!.Sources[0]);
    }
    [Fact]
    public void Sources_identify_country_scope_and_historical_material()
    {
        var ba = BaMeMdDestinationReference.Find("BA")!;
        var me = BaMeMdDestinationReference.Find("ME")!;
        var md = BaMeMdDestinationReference.Find("MD")!;
        Assert.Equal("Bosnia and Herzegovina", ba.Name);
        Assert.Equal("Montenegro", me.Name);
        Assert.Equal("Moldova", md.Name);
        Assert.Contains(ba.Sources, x => x.Purpose.Contains("historical import VAT"));
        Assert.Contains(me.Sources, x => x.Purpose.Contains("2026 tariff"));
        Assert.Contains(me.Sources, x => x.Purpose.Contains("historical veterinary"));
        Assert.Contains(md.Sources, x => x.Purpose.Contains("declaration"));
        Assert.All(new[] { ba, me, md }, r => Assert.Contains(r.UnresolvedChecks, x => x.Contains("special")));
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("BAM")][InlineData("LI")][InlineData("ZZ")][InlineData("N0")][InlineData(" BA")][InlineData("éS")]
    public void Rejects_invalid_and_other_destinations(string? country) => Assert.Null(BaMeMdDestinationReference.Find(country));
}
