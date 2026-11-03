using System.Text.Json;
using GlobalTradeTax.Api;
using Xunit;

public class UkMeasureScreeningTests
{
    [Fact]
    public void DoesNotAssertEligibilityOrCalculateDuty()
    {
        using var doc = JsonDocument.Parse("""{"data":{"type":"commodity","id":"0101210000"},"included":[{"type":"measure","id":"123","attributes":{"effective_start_date":"2024-01-01"}}]}""");
        var upstream = new UkReferenceResult("reference_only", doc.RootElement.Clone(), "https://www.trade-tariff.service.gov.uk/uk/api/v2/commodities/0101210000", "reference only");
        var result = UkMeasureScreening.Screen(upstream, "0101210000", "US", new DateOnly(2026, 11, 2));
        Assert.False(result.VerifiedForCalculation);
        Assert.Single(result.Measures);
        Assert.Contains("geographical_area_and_exclusions_not_resolved", result.Measures[0].ReviewReasons);
    }

    [Fact]
    public void MissingIncludedDataIsNotTreatedAsZeroDuty()
    {
        using var doc = JsonDocument.Parse("""{"data":{"id":"0101210000"}}""");
        var result = UkMeasureScreening.Screen(new UkReferenceResult("reference_only", doc.RootElement.Clone(), null, ""), "0101210000", "US", new DateOnly(2026, 11, 2));
        Assert.Equal("insufficient_data", result.Status);
        Assert.False(result.VerifiedForCalculation);
    }
}
