using GlobalTradeTax.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

// Normal builds remain offline. An explicit environment switch enables bounded official-API probes.
public sealed class UkLiveFactAttribute : FactAttribute
{
    public UkLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRADE_RUN_UK_LIVE_CONTRACT") != "1")
            Skip = "Opt in with TRADE_RUN_UK_LIVE_CONTRACT=1 to contact the official UK API.";
    }
}

public sealed class UkLiveContractTests : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 10 });

    private UkTariffReferenceClient Client() => new(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TRADE_UK_TARIFF_API_BASE_URL"] = "https://www.trade-tariff.service.gov.uk/uk/api/v2/"
        }).Build(), cache);

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_commodity_matches_the_runtime_contract()
    {
        var result = await Client().GetCommodityAsync("0101210000", default);
        Assert.True(result.Status == "reference_only", $"UK commodity contract failed: {result.Status}. {result.Warning}");
        var normalized = UkTariffNormalizer.Normalize("0101210000", result);
        Assert.Equal("0101210000", normalized.CommodityCode);
        Assert.NotEmpty(normalized.MeasureReferences);
        Assert.False(normalized.VerifiedForCalculation);
    }

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_area_matches_the_runtime_contract()
    {
        var result = await Client().GetGeographicalAreaAsync("1011", default);
        Assert.True(result.Status == "reference_only", $"UK area contract failed: {result.Status}. {result.Warning}");
        Assert.False(UkAreaMembership.Inspect(result, "1011", "US", new DateOnly(2026, 11, 4)).VerifiedForCalculation);
    }

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_missing_commodity_is_not_a_zero_rate()
    {
        var result = await Client().GetCommodityAsync("0000000000", default);
        Assert.Equal("not_found", result.Status);
        Assert.Null(result.Data);
    }

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_missing_area_is_not_membership_evidence()
    {
        var result = await Client().GetGeographicalAreaAsync("ZZZZZZZZZZZZ", default);
        Assert.Equal("not_found", result.Status);
        Assert.Null(result.Data);
    }

    public void Dispose()
    {
        http.Dispose();
        cache.Dispose();
    }
}
