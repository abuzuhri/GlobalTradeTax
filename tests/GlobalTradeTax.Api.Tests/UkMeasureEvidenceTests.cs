using System.Text.Json;
using GlobalTradeTax.Api;
using Xunit;

public class UkMeasureEvidenceTests
{
    [Theory]
    [InlineData("GB", "different_direct_country_reference", "different_country_reference_not_proof_of_inapplicability")]
    [InlineData("1011", "unknown", "geographical_group_membership_unresolved")]
    public void OtherGeographiesRemainUnverified(string areaId, string expectedEvidence, string expectedCheck)
    {
        using var doc = JsonDocument.Parse("""{"included":[{"type":"measure","id":"m1","attributes":{"effective_start_date":"2025-01-01"},"relationships":{"geographical_area":{"data":{"type":"geographical_area","id":"AREA_ID"}}}}]}""".Replace("AREA_ID", areaId, StringComparison.Ordinal));
        var upstream = new UkReferenceResult("reference_only", doc.RootElement.Clone(), null, "reference");
        var result = UkMeasureEvidence.Analyze(upstream, "0101210000", "US", new DateOnly(2026, 11, 2));
        var measure = Assert.Single(result.Measures);
        Assert.Equal(expectedEvidence, measure.GeographyEvidence);
        Assert.Equal([areaId], measure.GeographicalAreaReferences);
        Assert.Contains(expectedCheck, measure.UnresolvedChecks);
        Assert.False(result.VerifiedForCalculation);
        Assert.Equal("requires_review", measure.Status);
    }

    [Fact]
    public void CountryMatchIsOnlyEvidenceNotEligibility()
    {
        using var doc = JsonDocument.Parse("""{"included":[{"type":"measure","id":"m1","attributes":{"effective_start_date":"2025-01-01","duty_expression":"5%"},"relationships":{"geographical_area":{"data":{"type":"geographical_area","id":"US"}},"measure_conditions":{"data":[{"type":"measure_condition","id":"c1"}]}}}]}""");
        var upstream = new UkReferenceResult("reference_only", doc.RootElement.Clone(), "https://example.org", "reference");
        var result = UkMeasureEvidence.Analyze(upstream, "0101210000", "US", new DateOnly(2026, 11, 2));
        Assert.False(result.VerifiedForCalculation);
        Assert.Equal("direct_country_reference", Assert.Single(result.Measures).GeographyEvidence);
        Assert.Contains("measure_conditions_unresolved", result.Measures[0].UnresolvedChecks);
        Assert.Equal("requires_review", result.Measures[0].Status);
    }

    [Fact]
    public void NoIncludedMeasuresNeverMeansZeroDuty()
    {
        using var doc = JsonDocument.Parse("""{"data":{"id":"0101210000"}}""");
        var result = UkMeasureEvidence.Analyze(new UkReferenceResult("reference_only", doc.RootElement.Clone(), null, ""), "0101210000", "US", new DateOnly(2026, 11, 2));
        Assert.Empty(result.Measures);
        Assert.False(result.VerifiedForCalculation);
        Assert.Equal("insufficient_data", result.Status);
    }
}
