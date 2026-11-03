using GlobalTradeTax.Api;
using Xunit;

namespace GlobalTradeTax.Api.Tests;

public class UkDutyExpressionEngineTests
{
    [Theory]
    [InlineData("10%", 1000, 100)]
    [InlineData("0%", 1000, 0)]
    [InlineData("5.5%", 200, 11)]
    public void PercentIsIllustrativeOnly(string expression, decimal value, decimal expected)
    {
        var result = UkDutyExpressionEngine.Evaluate(expression, value, null, null);
        Assert.Equal(expected, result.IllustrativeAmount);
        Assert.False(result.VerifiedForCalculation);
    }

    [Fact]
    public void SpecificAndCompoundUseExplicitUnits()
    {
        Assert.Equal(15m, UkDutyExpressionEngine.Evaluate("3 GBP / 100 kg", 500, 500, null).IllustrativeAmount);
        Assert.Equal(65m, UkDutyExpressionEngine.Evaluate("10% + 3 GBP / 100 kg", 500, 500, null).IllustrativeAmount);
    }

    [Fact]
    public void SpecificDutyDoesNotRequireCustomsValue()
    {
        var result = UkDutyExpressionEngine.Evaluate("3 GBP / 100 kg", null, 500m, null);
        Assert.Equal(15m, result.IllustrativeAmount);
        Assert.False(result.VerifiedForCalculation);
    }

    [Fact]
    public void CompoundRequiresCustomsValue()
    {
        var result = UkDutyExpressionEngine.Evaluate("10% + 3 GBP / kg", null, 10m, null);
        Assert.Null(result.IllustrativeAmount);
        Assert.Contains("missing_verified_customs_value_gbp", result.UnresolvedChecks);
    }

    [Fact]
    public void ItemBasedDutyUsesCountOnly()
    {
        Assert.Equal(12m, UkDutyExpressionEngine.Evaluate("4 GBP / item", null, null, 3m).IllustrativeAmount);
    }

    [Theory]
    [InlineData("12 EUR / kg")]
    [InlineData("MAX 10%")]
    [InlineData("")]
    public void UnsupportedExpressionsFailClosed(string expression)
    {
        var result = UkDutyExpressionEngine.Evaluate(expression, 100, 10, 1);
        Assert.Null(result.IllustrativeAmount);
        Assert.Equal("requires_review", result.Status);
    }

    [Fact]
    public void MissingUnitAndValueFailClosed()
    {
        Assert.Null(UkDutyExpressionEngine.Evaluate("3 GBP / kg", 100, null, null).IllustrativeAmount);
        Assert.Null(UkDutyExpressionEngine.Evaluate("10%", null, null, null).IllustrativeAmount);
        Assert.Null(UkDutyExpressionEngine.Evaluate("10%", -1, null, null).IllustrativeAmount);
    }
}
