using GlobalTradeTax.Api;
using Xunit;

public sealed class UkMoneyAllocationTests
{
    [Fact]
    public void Allocation_reconciles_to_the_penny_and_is_independent_of_line_order()
    {
        var a = UkMoneyAllocation.Allocate(.01m, new Dictionary<string, decimal> { ["b"] = 1, ["a"] = 1, ["c"] = 1 })!;
        var b = UkMoneyAllocation.Allocate(.01m, new Dictionary<string, decimal> { ["c"] = 1, ["b"] = 1, ["a"] = 1 })!;
        Assert.Equal(.01m, a["a"]);
        Assert.Equal(.01m, a.Values.Sum());
        Assert.All(a, x => Assert.Equal(x.Value, b[x.Key]));
    }

    [Fact]
    public void Proportional_allocation_handles_very_different_decimal_scales_without_overflow()
    {
        var result = UkMoneyAllocation.Allocate(1000000000000m, new Dictionary<string, decimal>
            { ["small"] = .0000000000000000000000000001m, ["large"] = 1000000000000m })!;
        Assert.Equal(1000000000000m, result.Values.Sum());
        Assert.Equal(0m, result["small"]);
        var thirds = UkMoneyAllocation.Allocate(10m, new Dictionary<string, decimal> { ["a"] = 1, ["b"] = 2 })!;
        Assert.Equal(3.33m, thirds["a"]);
        Assert.Equal(6.67m, thirds["b"]);
    }

    [Fact]
    public void Missing_weight_and_invalid_amounts_do_not_create_equal_split_or_silent_zero()
    {
        Assert.Null(UkMoneyAllocation.Allocate(1m, new Dictionary<string, decimal> { ["a"] = 0 }));
        Assert.Null(UkMoneyAllocation.Allocate(1m, new Dictionary<string, decimal>()));
        Assert.Null(UkMoneyAllocation.Allocate(-1m, new Dictionary<string, decimal> { ["a"] = 1 }));
        Assert.Null(UkMoneyAllocation.Allocate(1m, new Dictionary<string, decimal> { ["a"] = -1 }));
        Assert.Equal(0m, UkMoneyAllocation.Allocate(0m, new Dictionary<string, decimal> { ["a"] = 0 })!["a"]);
    }
}
