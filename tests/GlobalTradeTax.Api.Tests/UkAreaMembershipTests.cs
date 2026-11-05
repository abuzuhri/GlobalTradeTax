using System.Text.Json;
using GlobalTradeTax.Api;
using Xunit;

public class UkAreaMembershipTests
{
    [Fact]
    public void MissingDataFailsClosed()
    {
        var report = UkAreaMembership.Inspect(new UkReferenceResult("reference_only", JsonDocument.Parse("{}").RootElement.Clone(), "https://example.org", ""), "1011", "US", new DateOnly(2026, 11, 2));
        Assert.Equal("unknown", report.EvidenceStatus);
        Assert.False(report.VerifiedForCalculation);
    }

    [Fact]
    public void Unverified_membership_serializer_is_not_evidence()
    {
        const string json = """
        {"included":[{"type":"geographical_area_membership","id":"m1","attributes":{"validity_start_date":"2020-01-01"},"relationships":{"geographical_area":{"data":{"id":"US"}},"geographical_area_group":{"data":{"id":"1011"}}}}]}
        """;
        using var doc = JsonDocument.Parse(json);
        var result = UkAreaMembership.Inspect(new UkReferenceResult("reference_only", doc.RootElement.Clone(), "https://example.org", ""), "1011", "US", new DateOnly(2026, 11, 2));
        Assert.Equal("unknown", result.EvidenceStatus);
        Assert.False(result.VerifiedForCalculation);
    }

    [Fact]
    public void ExpiredMembershipIsNotEvidence()
    {
        const string json = """
        {"included":[{"type":"geographical_area_membership","id":"m1","attributes":{"validity_start_date":"2020-01-01","validity_end_date":"2021-01-01"},"relationships":{"geographical_area":{"data":{"id":"US"}},"geographical_area_group":{"data":{"id":"1011"}}}}]}
        """;
        using var doc = JsonDocument.Parse(json);
        var result = UkAreaMembership.Inspect(new UkReferenceResult("reference_only", doc.RootElement.Clone(), null, ""), "1011", "US", new DateOnly(2026, 11, 2));
        Assert.Equal("unknown", result.EvidenceStatus);
    }
}
