using System.Globalization;

namespace GlobalTradeTax.Api;

/// <summary>A bounded, process-local fixed-window budget for authenticated tenants.</summary>
public sealed class TenantRateLimit
{
    private readonly object gate = new();
    private readonly Dictionary<string, Window> windows = new(StringComparer.Ordinal);
    private readonly TimeProvider clock;
    private readonly int limit;
    private readonly int seconds;
    private readonly int capacity;
    private sealed record Window(long Start, int Count);

    /// <summary>Reads and validates all limits at startup; invalid settings fail closed.</summary>
    public TenantRateLimit(IConfiguration configuration, TimeProvider clock)
    {
        this.clock = clock;
        limit = Read(configuration, "TenantRateLimit:PermitLimit", 120, 1, 10000);
        seconds = Read(configuration, "TenantRateLimit:WindowSeconds", 60, 1, 3600);
        capacity = Read(configuration, "TenantRateLimit:MaxTenants", 10000, 1, 100000);
    }

    private static int Read(IConfiguration configuration, string key, int fallback, int min, int max)
    {
        var value = configuration[key];
        if (value is null) return fallback;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < min || parsed > max)
            throw new InvalidOperationException($"{key} must be between {min} and {max}.");
        return parsed;
    }

    /// <summary>Stops exhausted requests before executing the endpoint and supplies retry guidance.</summary>
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, string tenant)
    {
        var retry = Acquire(tenant);
        if (retry == 0)
        {
            await next(context);
            return;
        }
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retry.ToString(CultureInfo.InvariantCulture);
        await context.Response.WriteAsJsonAsync(new { error = "tenant_rate_limit_exceeded" }, context.RequestAborted);
    }

    /// <summary>Atomically consumes a permit or returns the seconds until retry; capacity exhaustion also fails closed.</summary>
    public int Acquire(string tenant)
    {
        lock (gate)
        {
            var now = clock.GetTimestamp();
            if (windows.TryGetValue(tenant, out var window) && clock.GetElapsedTime(window.Start, now) < TimeSpan.FromSeconds(seconds))
            {
                if (window.Count >= limit)
                    return Math.Max(1, (int)Math.Ceiling((TimeSpan.FromSeconds(seconds) - clock.GetElapsedTime(window.Start, now)).TotalSeconds));
                windows[tenant] = window with { Count = window.Count + 1 };
                return 0;
            }
            if (windows.Count >= capacity && !windows.ContainsKey(tenant))
            {
                foreach (var expired in windows.Where(entry => clock.GetElapsedTime(entry.Value.Start, now) >= TimeSpan.FromSeconds(seconds)).Select(entry => entry.Key).ToArray())
                    windows.Remove(expired);
                if (windows.Count >= capacity) return seconds;
            }
            windows[tenant] = new Window(now, 1);
            return 0;
        }
    }
}
