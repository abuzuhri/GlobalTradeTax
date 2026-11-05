using System.Net;
using System.Text;
using System.Text.Json;
using GlobalTradeTax.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class UkProviderAndComplianceTests
{
    private static QuoteRequest Request() => new("US", "GB", "GBP", "DDP", [new("product", "0101299000", "CN", 1, 1000m)]);
    private static RateResult Rates() => new(100m, 220m, 10m, "verified", new("broker", "entry-reference", DateTimeOffset.UtcNow.AddMinutes(-1),
        DateTimeOffset.UtcNow.AddMinutes(10), "GBP", "0101299000", "CN", "GB"), []);
    private sealed class Provider(RateResult rates) : IImportRateProvider
    { public Task<RateResult> GetRatesAsync(QuoteRequest r, CancellationToken ct) => Task.FromResult(rates); }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(response(r)); }

    [Fact]
    public async Task Provider_evidence_matches_actual_goods_origin_instead_of_shipping_country()
    {
        var rates = Rates();
        Assert.Equal(1330m, (await new LandedCostService(new Provider(rates)).CalculateAsync(Request(), default)).Total);
        Assert.Null((await new LandedCostService(new Provider(rates with { Evidence = rates.Evidence! with { OriginCountry = "US" } }))
            .CalculateAsync(Request(), default)).Total);
        Assert.Null((await new LandedCostService(new Provider(rates)).CalculateAsync(Request() with { Lines = [Request().Lines[0], Request().Lines[0]] }, default)).Total);
    }

    [Fact]
    public async Task Provider_total_overflow_is_unavailable_instead_of_a_server_error()
    {
        var result = await new LandedCostService(new Provider(Rates() with { Duty = decimal.MaxValue })).CalculateAsync(Request(), default);
        Assert.Null(result.Total); Assert.Null(result.ImportDuty); Assert.Equal("requires_provider_quote", result.Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"status\":\"verified\",\"warnings\":null}")]
    [InlineData("{\"status\":\"verified\",\"warnings\":[null]}")]
    [InlineData("{\"status\":\"verified\",\"warnings\":[],\"duty\":1e-100}")]
    [InlineData("{\"status\":\"verified\",\"warnings\":[],\"duty\":0.00000000000000000000000000001}")]
    [InlineData("{\"status\":\"verified\",\"Status\":\"unavailable\",\"warnings\":[]}")]
    public async Task Invalid_provider_financial_JSON_cannot_reach_the_payable_gate(string json)
    {
        var result = await External(json);
        Assert.Equal("unavailable", result.Status); Assert.Null(result.Duty); Assert.NotNull(result.Warnings);
    }

    [Fact]
    public async Task Provider_body_is_bounded_and_valid_contract_remains_compatible()
    {
        Assert.Equal("unavailable", (await External(new string(' ', 1_048_577))).Status);
        var result = await External(JsonSerializer.Serialize(Rates()));
        Assert.Equal("verified", result.Status); Assert.Equal(100m, result.Duty);
    }

    private static async Task<RateResult> External(string json)
    {
        using var handler = new Handler(r => {
            Assert.Equal("Bearer", r.Headers.Authorization!.Scheme);
            return new(HttpStatusCode.OK) { Content = new StringContent(json,Encoding.UTF8,"application/json") };
        });
        using var http = new HttpClient(handler);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["TRADE_RATE_PROVIDER_URL"] = "https://example.test/rates", ["TRADE_RATE_PROVIDER_TOKEN"] = "example-test-value" }).Build();
        return await new ExternalImportRateProvider(http,config,NullLogger<ExternalImportRateProvider>.Instance).GetRatesAsync(Request(),default);
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("XI")]
    public void UK_import_controls_require_manual_review_and_do_not_clear_declared_goods(string territory)
    {
        var result = ShipmentCompliance.Evaluate(Request() with { ShipTo = territory });
        Assert.Equal("manual_review_required", result.Decision);
        Assert.Contains(result.RequiredChecks, x => x.Contains("EORI",StringComparison.Ordinal));
        Assert.Contains(result.RequiredChecks, x => x.Contains("certificate alternatives",StringComparison.Ordinal));
        Assert.Contains(result.RequiredChecks, x => x.Contains("quota",StringComparison.Ordinal));
        if (territory == "XI") Assert.Contains(result.RequiredChecks,x=>x.Contains("UKIMS",StringComparison.Ordinal));
    }

    [Fact]
    public void Controlled_goods_hold_applies_even_when_not_shipped_from_US()
    {
        var result = ShipmentCompliance.Evaluate(Request() with { ShipFrom = "CN", ShipTo = "DE", ItarControlled = true });
        Assert.Equal("manual_review_required", result.Decision); Assert.NotEmpty(result.Warnings);
    }
}
