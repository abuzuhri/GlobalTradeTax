using System.Text.Json;
using GlobalTradeTax.Api;
using Xunit;

public class UkGeographyResolverTests
{
    [Fact]
    public void DirectCountryReferenceDoesNotVerifyRate()
    {
        using var doc = JsonDocument.Parse("""{"included":[{"type":"measure","id":"m1","attributes":{"effective_start_date":"2026-01-01"},"relationships":{"geographical_area":{"data":{"type":"geographical_area","id":"US"}}}}]}""");
        var report = UkGeographyResolver.Analyze(new("reference_only", doc.RootElement.Clone(), null, ""), "0101210000", "US", new DateOnly(2026, 11, 2));
        Assert.False(report.VerifiedForCalculation);
        Assert.Contains("US", Assert.Single(report.Measures).DirectCountryReferences);
        Assert.Equal("requires_review", report.Measures[0].Status);
    }

    [Fact]
    public void MissingGroupMembershipFailsClosed()
    {
        using var doc = JsonDocument.Parse("""{"included":[{"type":"measure","id":"m1","attributes":{"effective_start_date":"2026-01-01"},"relationships":{"geographical_area":{"data":{"type":"geographical_area","id":"1011"}}}},{"type":"geographical_area","id":"1011","attributes":{"description":"Group"}}]}""");
        var report = UkGeographyResolver.Analyze(new("reference_only", doc.RootElement.Clone(), null, ""), "0101210000", "US", new DateOnly(2026, 11, 2));
        Assert.Contains("group_memberships_not_embedded:1011", Assert.Single(report.Measures).UnresolvedChecks);
        Assert.False(report.VerifiedForCalculation);
    }
}
