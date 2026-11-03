using GlobalTradeTax.Api;
using Xunit;

public class UkAreaLookupTests
{
    [Theory]
    [InlineData("US", true)]
    [InlineData("1011", true)]
    [InlineData("A-B_12", true)]
    [InlineData("../admin", false)]
    [InlineData("", false)]
    [InlineData("AB?x=1", false)]
    public void AreaIdIsStrictlyValidated(string id, bool expected) =>
        Assert.Equal(expected, UkTariffReferenceClient.IsSafeAreaId(id));
}
