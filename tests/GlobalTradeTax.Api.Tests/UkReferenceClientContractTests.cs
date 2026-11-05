using System.Net;
using System.Text;
using GlobalTradeTax.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class UkReferenceClientContractTests
{
    private const string Base = "https://www.trade-tariff.service.gov.uk/uk/api/v2/";
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
    private static UkTariffReferenceClient Client(FakeHandler handler, string? baseUrl = Base)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["TRADE_UK_TARIFF_API_BASE_URL"] = baseUrl }).Build();
        return new(new HttpClient(handler), config, new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }));
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("https://evil.example/uk/api/v2/")]
    [InlineData("http://www.trade-tariff.service.gov.uk/uk/api/v2/")]
    [InlineData("https://www.trade-tariff.service.gov.uk:444/uk/api/v2/")]
    [InlineData("https://www.trade-tariff.service.gov.uk/uk/api/v2/?redirect=evil")]
    [InlineData("https://www.trade-tariff.service.gov.uk/uk/api/v1/")]
    public async Task Rejects_unapproved_base_urls_without_network(string url)
    {
        var handler = new FakeHandler(_ => Json("{}"));
        var result = await Client(handler, url).GetCommodityAsync("0101210000", default);
        Assert.Equal("not_configured", result.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Commodity_request_is_read_only_and_preserves_json()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(Base + "commodities/0101210000", request.RequestUri!.ToString());
            Assert.Contains(request.Headers.Accept, x => x.MediaType == "application/vnd.hmrc.2.0+json");
            return Json("{\"data\":{\"type\":\"commodity\",\"id\":\"93796\",\"attributes\":{\"goods_nomenclature_item_id\":\"0101210000\"}}}");
        });
        var result = await Client(handler).GetCommodityAsync("0101210000", default);
        Assert.Equal("reference_only", result.Status);
        Assert.Equal("93796", result.Data!.Value.GetProperty("data").GetProperty("id").GetString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Area_lookup_is_cached_without_claiming_verified_membership()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.Equal(Base + "geographical_areas/1011", request.RequestUri!.ToString());
            Assert.Contains(request.Headers.Accept, x => x.MediaType == "application/vnd.hmrc.2.0+json");
            return Json("{\"data\":{\"type\":\"geographical_area\",\"id\":\"1011\"}}");
        });
        var client = Client(handler);
        var a = await client.GetGeographicalAreaAsync("1011", default);
        var b = await client.GetGeographicalAreaAsync("1011", default);
        Assert.Equal("reference_only", a.Status);
        Assert.Equal("reference_only", b.Status);
        Assert.Equal(1, handler.Calls);
        Assert.Contains("not verified", a.Warning);
    }

    [Fact]
    public async Task Redirect_is_not_interpreted_as_valid_tariff_response()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        { Headers = { Location = new Uri("https://example.org/") } });
        var result = await Client(handler).GetCommodityAsync("0101210000", default);
        Assert.Equal("upstream_error", result.Status);
    }

    [Fact]
    public async Task Invalid_code_never_triggers_network()
    {
        var handler = new FakeHandler(_ => Json("{}"));
        var result = await Client(handler).GetCommodityAsync("../../etc", default);
        Assert.Equal("invalid_code", result.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Oversized_response_fails_closed()
    {
        var handler = new FakeHandler(_ => Json("{\"large\":\"" + new string('a', 1_048_576) + "\"}"));
        var result = await Client(handler).GetCommodityAsync("0101210000", default);
        Assert.Equal("upstream_error", result.Status);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"type\":\"commodities\",\"id\":\"9999999999\"}}")]
    [InlineData("{\"data\":{\"id\":\"0101210000\"}}")]
    [InlineData("{\"data\":{\"type\":\"commodity\",\"id\":\"0101210000\"}}")]
    [InlineData("{\"data\":{\"type\":\"commodity\",\"id\":\"93796\",\"attributes\":{\"goods_nomenclature_item_id\":\"9999999999\"}}}")]
    [InlineData("{\"data\":{\"type\":\"commodity\",\"id\":\"93796\",\"attributes\":{\"goods_nomenclature_item_id\":101210000}}}")]
    [InlineData("{\"data\":{\"type\":\"commodities\",\"id\":\"93796\",\"attributes\":{\"goods_nomenclature_item_id\":\"0101210000\"}}}")]
    [InlineData("{\"data\":{\"type\":\"geographical_area\",\"id\":\"93796\",\"attributes\":{\"goods_nomenclature_item_id\":\"0101210000\"}}}")]
    public async Task Malformed_or_mismatched_json_api_is_rejected(string body)
    {
        var handler = new FakeHandler(_ => Json(body));
        var result = await Client(handler).GetCommodityAsync("0101210000", default);
        Assert.Equal("invalid_upstream_contract", result.Status);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Malformed_area_is_not_cached()
    {
        var handler = new FakeHandler(_ => Json("{\"data\":{\"id\":\"1011\"}}"));
        var client = Client(handler);
        Assert.Equal("invalid_upstream_contract", (await client.GetGeographicalAreaAsync("1011", default)).Status);
        Assert.Equal("invalid_upstream_contract", (await client.GetGeographicalAreaAsync("1011", default)).Status);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("geographical_areas", "1011")]
    [InlineData("commodity", "1011")]
    [InlineData("geographical_area", "1012")]
    public async Task Wrong_area_identity_or_type_is_not_cached(string type, string id)
    {
        var handler = new FakeHandler(_ => Json(System.Text.Json.JsonSerializer.Serialize(new { data = new { type, id } })));
        var client = Client(handler);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = await client.GetGeographicalAreaAsync("1011", default);
            Assert.Equal("invalid_upstream_contract", result.Status);
            Assert.Null(result.Data);
        }
        Assert.Equal(2, handler.Calls);
    }
}
