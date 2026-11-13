using GlobalTradeTax.Api;
using Xunit;

public sealed class LatamDestinationReferenceTests
{
    [Theory]
    [InlineData("CR", "hacienda.go.cr")][InlineData("PA", "ana.gob.pa")][InlineData("DO", "aduanas.gob.do")][InlineData("AR", "arca.gob.ar")][InlineData("UY", "aduanas.gub.uy")][InlineData("PY", "dnit.gov.py")][InlineData("BR", "gov.br")][InlineData("CL", "aduana.cl")][InlineData("CO", "dian.gov.co")][InlineData("PE", "sunat.gob.pe")]
    public void National_guidance_is_unapproved_and_reaches_compliance(string country, string authorityDomain)
    {
        var result = LatamDestinationReference.Find(country.ToLowerInvariant())!;
        Assert.Equal(country, result.Country);
        Assert.Equal("reference_only", result.Status);
        Assert.Contains(result.Sources, s => s.Url.Contains(authorityDomain));
        Assert.All(result.Sources, s => { Assert.StartsWith("https://", s.Url); Assert.Equal(country is "CR" or "PA" or "DO" ? "2026-11-13" : "2026-11-09", s.ReviewedOn); });
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.LandedTotal);
        Assert.False(result.VerifiedForCalculation); Assert.False(result.PaymentApproved); Assert.False(result.ShipmentReleaseApproved);
        Assert.Equal("LATAM", DestinationPolicies.Resolve(country).Region);
        Assert.Equal("provider_required", DestinationPolicies.Resolve(country).Status);
        Assert.Equal(result.Sources.Select(s => s.Url), DestinationPolicies.Resolve(country).References);
        Assert.Contains(result.UnresolvedChecks, c => c.Contains("origin"));
        Assert.Contains(result.UnresolvedChecks, c => c.Contains("postal"));
        Assert.Contains(result.UnresolvedChecks, c => c.Contains("territory"));
        var request = new QuoteRequest("US", country, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision);
        Assert.All(result.UnresolvedChecks, c => Assert.Contains(c, compliance.RequiredChecks));
        Assert.False(compliance.ShipmentReleaseApproved);
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
    }
    [Theory]
    [InlineData("CR", "historical", "technical notes", "development portal")]
    [InlineData("PA", "clearance", "tariff", "ITBMS")]
    [InlineData("DO", "declaration", "competent authority", "ITBIS")]
    public void New_country_sources_retain_distinct_purposes(string country, string first, string second, string third)
    {
        var reference = LatamDestinationReference.Find(country)!;
        foreach (var purpose in new[] { first, second, third })
            Assert.Contains(reference.Sources, source => source.Purpose.Contains(purpose, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(reference.UnresolvedChecks, check => check.Contains("preference"));
        Assert.Contains(reference.UnresolvedChecks, check => check.Contains("controls"));
        Assert.Contains(reference.UnresolvedChecks, check => check.Contains("regime"));
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("BRA")][InlineData("MX")][InlineData("ZZ")][InlineData("GF")][InlineData("PR")][InlineData("CR ")][InlineData("P1")][InlineData(" BR")][InlineData("BÅ")]
    public void Invalid_or_unsupported_input_returns_null(string? country) => Assert.Null(LatamDestinationReference.Find(country));
}
