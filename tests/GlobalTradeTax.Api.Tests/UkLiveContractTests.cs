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
            ["TRADE_UK_TARIFF_API_BASE_URL"] = "https://www.trade-tariff.service.gov.uk/uk/api/v2/",
            ["TRADE_UK_TARIFF_XI_API_BASE_URL"] = "https://www.trade-tariff.service.gov.uk/xi/api/v2/"
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
    public async Task Official_dated_commodity_preserves_the_requested_context()
    {
        var date = new DateOnly(2026, 11, 5);
        var result = await Client().GetCommodityAsync("0101210000", date, default);
        Assert.Equal("reference_only", result.Status);
        Assert.Equal(date, result.AsOfDate);
        Assert.EndsWith("?as_of=2026-11-05", result.SourceUrl);
        Assert.NotEmpty(UkMeasureEvidence.Analyze(result, "0101210000", "US", date).Measures);
    }

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_dated_world_group_provides_only_candidate_membership()
    {
        var date = new DateOnly(2026, 11, 5);
        var result = await Client().GetGeographicalAreaAsync("1011", date, default);
        Assert.Equal("reference_only", result.Status);
        var membership = UkAreaMembership.Inspect(result, "1011", "US", date);
        Assert.Equal("candidate_membership_evidence", membership.EvidenceStatus);
        Assert.Equal(new[] { "1011", "US" }, membership.MembershipPath);
        Assert.False(membership.VerifiedForCalculation);
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

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_XI_commodity_preserves_territory_and_structured_components()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await Client().GetCommodityAsync("0201300090", date, "XI", default);
        Assert.Equal("reference_only", result.Status); Assert.Equal("XI", result.DestinationTerritory);
        Assert.NotNull(result.ResponseSha256);
        Assert.Contains(UkMeasureRules.Read(result,"0201300090").Measures, r => r.ImportLinked && r.Components.Length > 0);
    }

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_XI_area_preserves_territory()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await Client().GetGeographicalAreaAsync("1011",date,"XI",default);
        Assert.Equal("reference_only",result.Status); Assert.Equal("XI",result.DestinationTerritory);
        var membership = UkAreaMembership.Inspect(result,"1011","US",date);
        Assert.Equal("XI",membership.DestinationTerritory); Assert.False(membership.VerifiedForCalculation);
    }

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_monthly_invoice_FX_has_a_dated_USD_conversion()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await Client().GetMonthlyExchangeRatesAsync(date,default);
        Assert.Equal("reference_only",result.Status);
        Assert.Contains(UkExchangeRates.Monthly(result,date).Rates, r => r.Currency == "USD" && r.Purpose == "invoice_customs_value" && r.GbpPerUnit > 0);
    }

    [UkLiveFact]
    [Trait("Category", "UkLiveContract")]
    public async Task Official_XI_tariff_FX_has_its_separate_monthly_EUR_conversion()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await Client().GetTariffExchangeRatesAsync(date,default);
        Assert.Equal("reference_only",result.Status);
        Assert.Equal("tariff_duty",Assert.Single(UkExchangeRates.Tariff(result,date).Rates).Purpose);
    }

    public void Dispose()
    {
        http.Dispose();
        cache.Dispose();
    }
}
