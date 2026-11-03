using GlobalTradeTax.Api;
using Xunit;
namespace GlobalTradeTax.Api.Tests;
public class UkAreaLookupPlannerTests
{
    [Fact] public void ExactlyAtLimitIsComplete()
    {
        var p = UkAreaLookupPlanner.Create(Enumerable.Range(1000,20).Select(x => x.ToString()), "US");
        Assert.True(p.Complete); Assert.Equal(20,p.AreaIds.Length); Assert.Empty(p.OmittedAreaIds);
    }
    [Fact] public void OverLimitReportsOmittedIds()
    {
        var p = UkAreaLookupPlanner.Create(Enumerable.Range(1000,23).Select(x => x.ToString()).Reverse(), "US");
        Assert.False(p.Complete); Assert.Equal(new[]{"1020","1021","1022"},p.OmittedAreaIds);
        Assert.Contains("geographical_lookup_limit_exceeded_incomplete_evidence",p.Warnings);
    }
    [Fact] public void OriginDuplicatesAndUnsafeIdsAreFiltered()
    {
        var p = UkAreaLookupPlanner.Create(new[]{"US","us","1011","1011","../../bad","GB"},"US");
        Assert.Equal(new[]{"1011","GB"},p.AreaIds);
    }
}
