using GlobalTradeTax.Api;
using Xunit;

public sealed class AuNzDestinationReferenceTests
{
    [Theory]
    [InlineData("AU", "abf.gov.au", "ato.gov.au", "agriculture.gov.au")]
    [InlineData("NZ", "customs.govt.nz", "ird.govt.nz", "mpi.govt.nz")]
    public void Country_guidance_has_official_sources_and_closed_gates(string country, string customs, string tax, string biosecurity)
    {
        var result = AuNzDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country); Assert.Equal("reference_only", result.Status);
        Assert.Contains(result.Sources, s => new Uri(s.Url).Host.EndsWith(customs));
        Assert.Contains(result.Sources, s => new Uri(s.Url).Host.EndsWith(tax));
        Assert.Contains(result.Sources, s => new Uri(s.Url).Host.EndsWith(biosecurity));
        Assert.All(result.Sources, s => Assert.Equal("2026-11-09", s.ReviewedOn));
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("territor") || s.Contains("jurisdiction"));
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("origin"));
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        Assert.Equal(result.Sources.Select(s => s.Url), DestinationPolicies.Resolve(country).References);
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision); Assert.False(compliance.ShipmentReleaseApproved);
        Assert.All(result.UnresolvedChecks, check => Assert.Contains(check, compliance.RequiredChecks));
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("US")][InlineData("CK")][InlineData("NU")][InlineData("TK")][InlineData("NF")][InlineData("AUS")][InlineData(" AU")][InlineData("ÅU")]
    public void Other_and_malformed_codes_are_rejected(string? country) => Assert.Null(AuNzDestinationReference.Find(country));
}
