using GlobalTradeTax.Api;
using Xunit;

public sealed class GccDestinationReferenceTests
{
    [Theory]
    [InlineData("BH")][InlineData("KW")][InlineData("OM")][InlineData("QA")][InlineData("SA")][InlineData("AE")]
    public void Each_country_has_national_sources_and_closed_gates(string country)
    {
        var result = GccDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal("reference_only", result.Status);
        Assert.Equal(2, result.Sources.Length); Assert.All(result.Sources, s => { Assert.StartsWith("https://", s.Url); Assert.Equal("2026-11-08", s.ReviewedOn); });
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        Assert.Equal(result.Sources.Select(s => s.Url), DestinationPolicies.Resolve(country.ToLowerInvariant()).References);
        var request = new QuoteRequest("US", country.ToLowerInvariant(), "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision); Assert.False(compliance.ShipmentReleaseApproved);
        Assert.Contains(result.UnresolvedChecks[0], compliance.RequiredChecks);
        var quote = new QuoteService().Calculate(request); Assert.Null(quote.LandedTotal);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("USA")][InlineData("US")][InlineData("ÅE")][InlineData(" AE")]
    public void Invalid_and_nonmember_codes_are_rejected(string? country) => Assert.Null(GccDestinationReference.Find(country));
}
