using GlobalTradeTax.Api;
using Xunit;

public sealed class EuDestinationReferenceTests
{
    [Theory]
    [InlineData("AT")][InlineData("BE")][InlineData("BG")][InlineData("HR")][InlineData("CY")]
    [InlineData("CZ")][InlineData("DK")][InlineData("EE")][InlineData("FI")][InlineData("FR")]
    [InlineData("DE")][InlineData("GR")][InlineData("HU")][InlineData("IE")][InlineData("IT")]
    [InlineData("LV")][InlineData("LT")][InlineData("LU")][InlineData("MT")][InlineData("NL")]
    [InlineData("PL")][InlineData("PT")][InlineData("RO")][InlineData("SK")][InlineData("SI")]
    [InlineData("ES")][InlineData("SE")]
    public void Member_destinations_have_reference_guidance_and_no_verified_calculation(string country)
    {
        var result = EuDestinationReferences.Find(country.ToLowerInvariant());
        Assert.NotNull(result);
        Assert.Equal(country, result.Country);
        Assert.Equal("reference_only", result.Status);
        Assert.Equal("unresolved", result.TerritoryStatus);
        Assert.False(result.VerifiedForCalculation);
        Assert.Equal("2026-11-08", result.ReferenceReviewedOn);
        Assert.Contains(result.RequiredChecks, s => s.Contains("territory", StringComparison.Ordinal));
        Assert.Contains(result.RequiredChecks, s => s.Contains("national VAT", StringComparison.Ordinal));
        Assert.Equal(3, result.Sources.Length);
        Assert.Equal("EU", DestinationPolicies.Resolve(country).Region);
        var request = new QuoteRequest("US", country, "EUR", "DDP", [new("item", "0101299000", "US", 1, 10)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Contains(compliance.RequiredChecks, s => s.Contains("EU VAT/IOSS", StringComparison.Ordinal));
        Assert.Contains(compliance.RequiredChecks, s => s.Contains("exact destination territory", StringComparison.Ordinal));
        Assert.False(compliance.ShipmentReleaseApproved);
        var quote = new QuoteService().Calculate(request);
        Assert.Null(quote.ImportDuty); Assert.Null(quote.ImportTax); Assert.Null(quote.LandedTotal);
    }

    [Theory]
    [InlineData(null)][InlineData("")][InlineData("XX")][InlineData("GB")][InlineData("XI")]
    [InlineData("NO")][InlineData("EL")][InlineData("DE ")][InlineData("éé")][InlineData("123")]
    public void Invalid_and_nonmember_destinations_are_not_classified_as_EU(string? country)
    {
        Assert.Null(EuDestinationReferences.Find(country));
    }

    [Fact]
    public void Returned_arrays_cannot_mutate_other_requests()
    {
        var first = EuDestinationReferences.Find("DE")!;
        first.Sources[0] = "changed";
        first.RequiredChecks[0] = "changed";
        var next = EuDestinationReferences.Find("DE")!;
        Assert.NotEqual("changed", next.Sources[0]);
        Assert.NotEqual("changed", next.RequiredChecks[0]);
    }
}
