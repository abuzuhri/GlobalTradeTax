using GlobalTradeTax.Api;
using Xunit;

public sealed class NorthAmericaDestinationReferenceTests
{
    [Theory]
    [InlineData("US", "US Customs and Border Protection")]
    [InlineData("CA", "Canada Revenue Agency")]
    [InlineData("MX", "Agencia Nacional de Aduanas de México")]
    public void Supported_countries_have_attributed_sources_and_closed_gates(string country, string authority)
    {
        var result = NorthAmericaDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country);
        Assert.Equal("reference_only", result.Status);
        Assert.Contains(result.Sources, s => s.Authority == authority);
        Assert.All(result.Sources, s => { Assert.StartsWith("https://", s.Url); Assert.Equal("2026-11-09", s.ReviewedOn); });
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("USMCA/CUSMA/T-MEC"));
        Assert.Contains(result.UnresolvedChecks, s => s.Contains("exact destination"));
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        Assert.Equal(result.Sources.Select(s => s.Url), DestinationPolicies.Resolve(country).References);
        var request = new QuoteRequest("GB", country, "USD", "DDP", [new("item", "0101299000", "GB", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision);
        Assert.All(result.UnresolvedChecks, check => Assert.Contains(check, compliance.RequiredChecks));
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("USA")][InlineData("PR")][InlineData("GL")][InlineData("GB")][InlineData(" CA")][InlineData("ÅA")]
    public void Other_or_malformed_codes_are_rejected(string? country) => Assert.Null(NorthAmericaDestinationReference.Find(country));
}
