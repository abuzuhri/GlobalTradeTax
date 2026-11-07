using GlobalTradeTax.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class TenantRateLimitTests
{
    private sealed class Clock : TimeProvider
    {
        public long Timestamp { get; set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Timestamp;
    }

    private static TenantRateLimit Create(Clock clock, string limit = "2", string seconds = "10", string capacity = "2") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TenantRateLimit:PermitLimit"] = limit,
            ["TenantRateLimit:WindowSeconds"] = seconds,
            ["TenantRateLimit:MaxTenants"] = capacity
        }).Build(), clock);

    [Fact]
    public void Tenant_budgets_are_isolated_and_recover_at_window_boundary()
    {
        var clock = new Clock();
        var limiter = Create(clock);
        Assert.Equal(0, limiter.Acquire("a"));
        Assert.Equal(0, limiter.Acquire("a"));
        Assert.Equal(10, limiter.Acquire("a"));
        Assert.Equal(0, limiter.Acquire("b"));
        clock.Timestamp = 9;
        Assert.Equal(1, limiter.Acquire("a"));
        clock.Timestamp = 10;
        Assert.Equal(0, limiter.Acquire("a"));
    }

    [Fact]
    public void Capacity_fails_closed_and_expired_entries_are_reclaimed()
    {
        var clock = new Clock();
        var limiter = Create(clock, capacity: "1");
        Assert.Equal(0, limiter.Acquire("a"));
        Assert.Equal(10, limiter.Acquire("b"));
        clock.Timestamp = 10;
        Assert.Equal(0, limiter.Acquire("b"));
    }

    [Fact]
    public void Concurrent_requests_cannot_exceed_budget()
    {
        var limiter = Create(new Clock(), limit: "25");
        var results = Enumerable.Range(0, 100).AsParallel().Select(_ => limiter.Acquire("a")).ToArray();
        Assert.Equal(25, results.Count(result => result == 0));
        Assert.Equal(75, results.Count(result => result > 0));
    }

    [Fact]
    public async Task Exhausted_requests_do_not_execute_endpoint_work()
    {
        var limiter = Create(new Clock(), limit: "1");
        Assert.Equal(0, limiter.Acquire("a"));
        var calls = 0;
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        await limiter.InvokeAsync(context, _ => { calls++; return Task.CompletedTask; }, "a");
        Assert.Equal(0, calls);
        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("10", context.Response.Headers.RetryAfter.ToString());
    }

    [Theory]
    [InlineData("0", "10", "2")]
    [InlineData("10001", "10", "2")]
    [InlineData("2", "3601", "2")]
    [InlineData("2", "10", "100001")]
    [InlineData("2", "-1", "2")]
    [InlineData("bad", "10", "2")]
    public void Invalid_configuration_fails_startup(string limit, string seconds, string capacity) =>
        Assert.Throws<InvalidOperationException>(() => Create(new Clock(), limit, seconds, capacity));
}
