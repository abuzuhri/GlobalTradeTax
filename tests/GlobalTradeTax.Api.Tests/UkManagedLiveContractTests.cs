using GlobalTradeTax.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class UkManagedLiveFactAttribute : FactAttribute
{
    public UkManagedLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRADE_RUN_UK_MANAGED_LIVE_CONTRACT") != "1")
            Skip = "Managed UK live probes require explicit opt-in and operator-provisioned credentials.";
    }
}

public sealed class UkManagedLiveContractTests : IDisposable
{
    private readonly ServiceProvider services;

    public UkManagedLiveContractTests()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddInMemoryCollection(new Dictionary<string, string?>
        { ["TRADE_UK_TARIFF_API_BASE_URL"] = UkTariffReferenceClient.ManagedApiBaseUrl }).Build();
        var collection = new ServiceCollection();
        collection.AddSingleton<IConfiguration>(configuration);
        collection.AddSingleton(TimeProvider.System);
        collection.AddMemoryCache(options => options.SizeLimit = 100);
        collection.AddHttpClient(UkTariffAccessTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        collection.AddSingleton<UkTariffAccessTokenProvider>();
        collection.AddHttpClient<UkTariffReferenceClient>(client => client.Timeout = TimeSpan.FromSeconds(8))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
        services = collection.BuildServiceProvider();
    }

    [UkManagedLiveFact]
    [Trait("Category", "UkManagedLiveContract")]
    public async Task Managed_commodity_matches_the_runtime_contract()
    {
        var result = await services.GetRequiredService<UkTariffReferenceClient>().GetCommodityAsync("0101210000", default);
        Assert.True(result.Status == "reference_only", $"Managed UK commodity probe failed: {result.Status}. {result.Warning}");
        var normalized = UkTariffNormalizer.Normalize("0101210000", result);
        Assert.Equal("0101210000", normalized.CommodityCode);
        Assert.False(normalized.VerifiedForCalculation);
    }

    [UkManagedLiveFact]
    [Trait("Category", "UkManagedLiveContract")]
    public async Task Managed_area_matches_the_runtime_contract()
    {
        var result = await services.GetRequiredService<UkTariffReferenceClient>().GetGeographicalAreaAsync("1011", default);
        Assert.True(result.Status == "reference_only", $"Managed UK area probe failed: {result.Status}. {result.Warning}");
    }

    [UkManagedLiveFact]
    [Trait("Category", "UkManagedLiveContract")]
    public async Task Managed_missing_commodity_is_not_a_zero_rate()
    {
        var result = await services.GetRequiredService<UkTariffReferenceClient>().GetCommodityAsync("0000000000", default);
        Assert.Equal("not_found", result.Status);
        Assert.Null(result.Data);
    }

    [UkManagedLiveFact]
    [Trait("Category", "UkManagedLiveContract")]
    public async Task Managed_missing_area_is_not_membership_evidence()
    {
        var result = await services.GetRequiredService<UkTariffReferenceClient>().GetGeographicalAreaAsync("ZZZZZZZZZZZZ", default);
        Assert.Equal("not_found", result.Status);
        Assert.Null(result.Data);
    }

    public void Dispose() => services.Dispose();
}
