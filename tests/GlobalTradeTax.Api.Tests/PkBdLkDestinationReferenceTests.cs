using GlobalTradeTax.Api;
using Xunit;

public sealed class PkBdLkDestinationReferenceTests
{
    [Theory]
    [InlineData("PK", "Pakistan")][InlineData("BD", "Bangladesh")][InlineData("LK", "Sri Lanka")]
    public void Guidance_is_unresolved_and_integrated(string country, string name)
    {
        var result = PkBdLkDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal(name, result.Name); Assert.Equal("reference_only", result.Status);
        Assert.Contains(result.Sources, s => s.Purpose.Contains("tariff") || s.Purpose.Contains("tax"));
        Assert.Contains(result.Sources, s => s.Purpose.Contains("control") || s.Purpose.Contains("restrict") || s.Purpose.Contains("prohibited"));
        Assert.All(result.Sources, s => { Assert.Equal("https", new Uri(s.Url).Scheme); Assert.Equal("2026-11-12", s.ReviewedOn); });
        foreach (var term in new[] { "classification", "origin", "preferential", "valuation", "registration", "regime", "duties", "permits", "courier", "territory" })
            Assert.Contains(result.UnresolvedChecks, s => s.Contains(term));
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        var policy = DestinationPolicies.Resolve(country); Assert.Equal("OTHER", policy.Region); Assert.Equal("provider_required", policy.Status);
        Assert.Equal(result.Sources.Select(s => s.Url), policy.References);
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision); Assert.False(compliance.ShipmentReleaseApproved);
        Assert.All(result.UnresolvedChecks, check => Assert.Contains(check, compliance.RequiredChecks));
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
        result.Sources[0] = result.Sources[1];
        Assert.NotEqual(result.Sources[0], PkBdLkDestinationReference.Find(country)!.Sources[0]);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("PKK")][InlineData("IN")][InlineData(" PK")][InlineData("P1")][InlineData("ＰＫ")]
    public void Rejects_malformed_and_unsupported(string? country) => Assert.Null(PkBdLkDestinationReference.Find(country));
}
