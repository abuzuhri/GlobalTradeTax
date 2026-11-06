using GlobalTradeTax.Api;
using Xunit;

public sealed class UkLegacyDutyRegressionTests
{
    [Theory]
    [InlineData("2 GBP / 100kg")]
    [InlineData("2 GBP / 100   kg")]
    public void Permitted_whitespace_does_not_change_mass_basis(string expression) =>
        Assert.Equal(2m,UkDutyExpressionEngine.Evaluate(expression,null,100m,null).IllustrativeAmount);

    [Fact]
    public void Fractional_items_or_excessive_rate_precision_remain_unknown()
    {
        Assert.Null(UkDutyExpressionEngine.Evaluate("2 GBP / item",null,null,1.5m).IllustrativeAmount);
        Assert.Null(UkDutyExpressionEngine.Evaluate("0.00000000000000000000000000001 %",1000m,null,null).IllustrativeAmount);
        Assert.Null(UkDutyExpressionEngine.Evaluate(new string('1',1000)+" %",1000m,null,null).IllustrativeAmount);
    }

    [Fact]
    public void Legacy_evaluator_preserves_unit_precision_and_rejects_underflow()
    {
        const decimal smallest = 0.0000000000000000000000000001m;
        Assert.Equal(smallest,UkDutyExpressionEngine.Evaluate("1000 GBP / tonne",null,smallest,null).IllustrativeAmount);
        Assert.Null(UkDutyExpressionEngine.Evaluate("1 %",smallest,null,null).IllustrativeAmount);
        Assert.Null(UkDutyExpressionEngine.Evaluate("1 % + 1 GBP / kg",smallest,1m,null).IllustrativeAmount);
    }
}
