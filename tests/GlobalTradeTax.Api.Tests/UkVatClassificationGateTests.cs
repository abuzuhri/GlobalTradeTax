using GlobalTradeTax.Api;
using Xunit;

namespace GlobalTradeTax.Api.Tests;

public class UkVatClassificationGateTests
{
    private static UkVatClassificationInput ValidLooking() => new(
        "GB", "free_circulation", "0101210000", "US", new DateOnly(2026, 11, 2),
        20m, "https://www.gov.uk/guidance/rates-of-vat-on-different-goods-and-services",
        new DateOnly(2026, 1, 1), null, true, true, true, true, true);

    [Fact]
    public void AllSelfReportedChecksStillCannotConfirmRate()
    {
        var result = UkVatClassificationGate.Evaluate(ValidLooking());
        Assert.Null(result.ConfirmedVatRatePercent);
        Assert.False(result.VerifiedForCalculation);
        Assert.Contains("independent_authoritative_vat_determination_not_implemented", result.UnresolvedChecks);
    }

    [Fact]
    public void NorthernIrelandRequiresDistinctRules()
    {
        var result = UkVatClassificationGate.Evaluate(ValidLooking() with { DestinationTerritory = "XI" });
        Assert.Contains("northern_ireland_specific_rules_not_implemented", result.UnresolvedChecks);
    }

    [Fact]
    public void ExpiredRateIsRejected()
    {
        var result = UkVatClassificationGate.Evaluate(ValidLooking() with { RateEffectiveTo = new DateOnly(2026, 1, 31) });
        Assert.Contains("rate_expired", result.UnresolvedChecks);
    }

    [Fact]
    public void MissingEvidenceNeverProducesRate()
    {
        var result = UkVatClassificationGate.Evaluate(new UkVatClassificationInput(
            null, null, null, null, null, null, null, null, null));
        Assert.Null(result.ConfirmedVatRatePercent);
        Assert.Contains("missing_vat_rate", result.UnresolvedChecks);
    }

    [Fact]
    public void HttpRateSourceIsRejected()
    {
        var result = UkVatClassificationGate.Evaluate(ValidLooking() with { RateSourceUrl = "http://example.com/rates" });
        Assert.Contains("missing_https_authoritative_rate_source", result.UnresolvedChecks);
    }
}
