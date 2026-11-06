using GlobalTradeTax.Api;
using Xunit;

namespace GlobalTradeTax.Api.Tests;

public class UkVatReadinessTests
{
    [Fact]
    public void MissingEvidenceNeverCalculates()
    {
        var result = UkVatReadiness.Evaluate(new UkVatInput(1000m, null, 30m, 10m, 0m, 20m));
        Assert.Equal("requires_review", result.Status);
        Assert.Null(result.IllustrativeVatGbp);
        Assert.Null(result.PayableVatGbp);
        Assert.Contains("missing_verified_import_duty_gbp", result.UnresolvedChecks);
    }

    [Fact]
    public void FullyFlaggedInputStillIllustrative()
    {
        var result = UkVatReadiness.Evaluate(new UkVatInput(1000m, 100m, 30m, 10m, 0m, 20m,
            true, true, true, true, true));
        Assert.Equal(1140m, result.IllustrativeTaxableBaseGbp);
        Assert.Equal(228m, result.IllustrativeVatGbp);
        Assert.Null(result.PayableVatGbp);
        Assert.False(result.VerifiedForCalculation);
    }

    [Fact]
    public void NegativeCostsRejected()
    {
        var result = UkVatReadiness.Evaluate(new UkVatInput(1000m, 100m, -1m, 0m, 0m, 20m));
        Assert.Equal("invalid_input", result.Status);
        Assert.Null(result.PayableVatGbp);
    }

    [Fact]
    public void OutOfRangeRateRejected()
    {
        var result = UkVatReadiness.Evaluate(new UkVatInput(1000m, 100m, 0m, 0m, 0m, 120m));
        Assert.Equal("invalid_input", result.Status);
    }

    [Fact]
    public void Positive_VAT_below_decimal_precision_is_unknown()
    {
        var result=UkVatReadiness.Evaluate(new(0.0000000000000000000000000001m,0m,0m,0m,0m,1m,true,true,true,true,true));
        Assert.Equal("requires_review",result.Status);Assert.Null(result.IllustrativeVatGbp);
        Assert.Contains("vat_arithmetic_precision_lost",result.UnresolvedChecks);
    }
}
