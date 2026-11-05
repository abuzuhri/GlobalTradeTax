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
}
