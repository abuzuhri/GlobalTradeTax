using System.Net;
using System.Text;
using System.Text.Json;
using GlobalTradeTax.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class UkManagedApiTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private int calls;
        public int Calls => calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return respond(request, ct);
        }
    }

    private sealed class Factory(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(UkTariffAccessTokenProvider.HttpClientName, name);
            return new(handler, disposeHandler: false);
        }
    }

    private sealed class Harness : IDisposable
    {
        public IConfigurationRoot Configuration { get; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TRADE_UK_TARIFF_API_BASE_URL"] = UkTariffReferenceClient.ManagedApiBaseUrl,
            ["TRADE_UK_TARIFF_CLIENT_ID"] = "example-client",
            ["TRADE_UK_TARIFF_CLIENT_SECRET"] = "example secret +&=/ value"
        }).Build();
        public Clock Clock { get; } = new();
        public Handler TokenHandler { get; }
        public Handler ApiHandler { get; }
        public UkTariffAccessTokenProvider Provider { get; }
        public UkTariffReferenceClient Client { get; }
        private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 100 });
        private readonly HttpClient http;

        public Harness(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? token = null,
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? api = null)
        {
            TokenHandler = new(token ?? ((_, _) => Task.FromResult(Token())));
            ApiHandler = new(api ?? ((request, _) => Task.FromResult(Json(
                request.RequestUri!.AbsolutePath.Contains("/geographical_areas/", StringComparison.Ordinal)
                    ? "{\"data\":{\"id\":\"1011\",\"type\":\"geographical_area\"}}"
                    : File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "UkTariff", "commodity-0101210000.json"))))));
            http = new(ApiHandler);
            Provider = new(new Factory(TokenHandler), Configuration, Clock);
            Client = new(http, Configuration, cache, Provider);
        }

        public void Dispose()
        {
            http.Dispose();
            Provider.Dispose();
            TokenHandler.Dispose();
            cache.Dispose();
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Token(string value = "example-access-token", int seconds = 3600) =>
        Json(JsonSerializer.Serialize(new { access_token = value, token_type = "Bearer", expires_in = seconds }));

    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(body);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_token_bodies_fail_with_or_without_a_declared_length(bool unknownLength)
    {
        var body = "{\"padding\":\"" + new string('a', 65_536) + "\"}";
        using var harness = new Harness(token: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = unknownLength ? new UnknownLengthContent(body) : new StringContent(body) }));
        Assert.Equal("upstream_error", (await harness.Client.GetCommodityAsync("0101210000", default)).Status);
        Assert.Equal(0, harness.ApiHandler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_reference_bodies_fail_with_or_without_a_declared_length(bool unknownLength)
    {
        var body = "{\"padding\":\"" + new string('a', 1_048_576) + "\"}";
        using var harness = new Harness(api: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = unknownLength ? new UnknownLengthContent(body) : new StringContent(body) }));
        var result = await harness.Client.GetCommodityAsync("0101210000", default);
        Assert.Equal("upstream_error", result.Status);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Oversized_access_token_is_never_put_in_an_authorization_header()
    {
        using var harness = new Harness(token: (_, _) => Task.FromResult(Token(new string('a', 16_385))));
        Assert.Equal("upstream_error", (await harness.Client.GetCommodityAsync("0101210000", default)).Status);
        Assert.Equal(0, harness.ApiHandler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Managed_requests_send_form_credentials_only_to_the_fixed_auth_endpoint(bool area)
    {
        using var harness = new Harness(token: async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(UkTariffAccessTokenProvider.TokenEndpoint, request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
            var body = await request.Content.ReadAsStringAsync(ct);
            Assert.Contains("grant_type=client_credentials", body);
            Assert.Contains("client_id=example-client", body);
            Assert.Contains("client_secret=example+secret+%2B%26%3D%2F+value", body);
            return Token();
        }, api: (request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(UkTariffReferenceClient.ManagedApiBaseUrl + (area ? "geographical_areas/1011" : "commodities/0101210000"), request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("example-access-token", request.Headers.Authorization.Parameter);
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "application/vnd.hmrc.2.0+json");
            Assert.Null(request.Content);
            return Task.FromResult(area ? Json("{\"data\":{\"id\":\"1011\",\"type\":\"geographical_area\"}}")
                : Json("{\"data\":{\"id\":\"93796\",\"type\":\"commodity\",\"attributes\":{\"goods_nomenclature_item_id\":\"0101210000\"}}}"));
        });
        var result = area ? await harness.Client.GetGeographicalAreaAsync("1011", default)
            : await harness.Client.GetCommodityAsync("0101210000", default);
        Assert.Equal("reference_only", result.Status);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("example-access-token", serialized);
        Assert.DoesNotContain("example-client", serialized);
        Assert.DoesNotContain("example secret", serialized);
        Assert.Equal(1, harness.TokenHandler.Calls);
    }

    [Theory]
    [InlineData("http://api.trade-tariff.service.gov.uk/uk/api/")]
    [InlineData("https://api.trade-tariff.service.gov.uk:444/uk/api/")]
    [InlineData("https://api.trade-tariff.service.gov.uk/xi/api/")]
    [InlineData("https://api.trade-tariff.service.gov.uk/uk/api/v2/")]
    [InlineData("https://api.trade-tariff.service.gov.uk/uk/api/?redirect=elsewhere")]
    [InlineData("https://api.trade-tariff.service.gov.uk/uk/api/#fragment")]
    [InlineData("https://user@api.trade-tariff.service.gov.uk/uk/api/")]
    [InlineData("https://api.trade-tariff.service.gov.uk.evil.example/uk/api/")]
    [InlineData("https://auth.id.trade-tariff.service.gov.uk/uk/api/")]
    [InlineData("https://www.trade-tariff.service.gov.uk/uk/api/")]
    public async Task Unsafe_base_urls_send_neither_credentials_nor_api_requests(string url)
    {
        using var harness = new Harness();
        harness.Configuration["TRADE_UK_TARIFF_API_BASE_URL"] = url;
        Assert.Equal("not_configured", (await harness.Client.GetCommodityAsync("0101210000", default)).Status);
        Assert.Equal("not_configured", (await harness.Client.GetGeographicalAreaAsync("1011", default)).Status);
        Assert.Equal(0, harness.TokenHandler.Calls);
        Assert.Equal(0, harness.ApiHandler.Calls);
    }

    [Theory]
    [InlineData("TRADE_UK_TARIFF_CLIENT_ID", null)]
    [InlineData("TRADE_UK_TARIFF_CLIENT_SECRET", null)]
    [InlineData("TRADE_UK_TARIFF_CLIENT_SECRET", "   ")]
    public async Task Missing_credentials_fail_before_any_network(string setting, string? value)
    {
        using var harness = new Harness();
        harness.Configuration[setting] = value;
        Assert.Equal("not_configured", (await harness.Client.GetCommodityAsync("0101210000", default)).Status);
        Assert.Equal(0, harness.TokenHandler.Calls);
        Assert.Equal(0, harness.ApiHandler.Calls);
    }

    [Fact]
    public async Task Invalid_resource_codes_do_not_acquire_a_token()
    {
        using var harness = new Harness();
        Assert.Equal("invalid_code", (await harness.Client.GetCommodityAsync("../", default)).Status);
        Assert.Equal("invalid_code", (await harness.Client.GetGeographicalAreaAsync("../", default)).Status);
        Assert.Equal(0, harness.TokenHandler.Calls);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    public async Task Public_access_never_sends_tokens_or_calls_the_auth_endpoint(int status)
    {
        using var harness = new Harness(api: (request, _) =>
        {
            Assert.Equal("www.trade-tariff.service.gov.uk", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(Json("{\"data\":{\"id\":\"1011\",\"type\":\"geographical_area\"}}", (HttpStatusCode)status));
        });
        harness.Configuration["TRADE_UK_TARIFF_API_BASE_URL"] = "https://www.trade-tariff.service.gov.uk/uk/api/v2/";
        var result = await harness.Client.GetGeographicalAreaAsync("1011", default);
        Assert.Equal(status == 200 ? "reference_only" : "upstream_error", result.Status);
        Assert.Equal(0, harness.TokenHandler.Calls);
        Assert.Equal(1, harness.ApiHandler.Calls);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(302)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Token_errors_do_not_fall_back_or_expose_upstream_error_bodies(int status)
    {
        using var harness = new Harness(token: (_, _) => Task.FromResult(Json("example secret + example-access-token", (HttpStatusCode)status)));
        var result = await harness.Client.GetCommodityAsync("0101210000", default);
        Assert.Equal("upstream_error", result.Status);
        Assert.Null(result.Data);
        Assert.DoesNotContain("example", JsonSerializer.Serialize(result));
        Assert.Equal(1, harness.TokenHandler.Calls);
        Assert.Equal(0, harness.ApiHandler.Calls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not JSON")]
    [InlineData("{\"access_token\":\"x\",\"token_type\":\"Basic\",\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"x\",\"token_type\":\"Bearer\"}")]
    [InlineData("{\"access_token\":\"x\",\"token_type\":\"Bearer\",\"expires_in\":\"3600\"}")]
    [InlineData("{\"access_token\":\"x\",\"token_type\":\"Bearer\",\"expires_in\":0}")]
    [InlineData("{\"access_token\":\"x\",\"token_type\":\"Bearer\",\"expires_in\":86401}")]
    [InlineData("{\"access_token\":\"\",\"token_type\":\"Bearer\",\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"x\\r\\nInjected: value\",\"token_type\":\"Bearer\",\"expires_in\":3600}")]
    [InlineData("{\"access_token\":\"x\",\"access_token\":\"y\",\"token_type\":\"Bearer\",\"expires_in\":3600}")]
    [InlineData("{\"error\":\"invalid_client\",\"access_token\":\"x\",\"token_type\":\"Bearer\",\"expires_in\":3600}")]
    public async Task Invalid_token_documents_never_authorize_a_reference_request(string body)
    {
        using var harness = new Harness(token: (_, _) => Task.FromResult(Json(body)));
        var result = await harness.Client.GetCommodityAsync("0101210000", default);
        Assert.Equal("upstream_error", result.Status);
        Assert.Null(result.Data);
        Assert.Equal(0, harness.ApiHandler.Calls);
    }

    [Fact]
    public async Task Token_is_shared_until_early_renewal_and_replaced_before_expiry()
    {
        var issued = 0;
        using var harness = new Harness(token: (_, _) => Task.FromResult(Token("token-" + ++issued)));
        Assert.Equal("reference_only", (await harness.Client.GetCommodityAsync("0101210000", default)).Status);
        harness.Clock.Now = harness.Clock.Now.AddSeconds(3500);
        Assert.Equal("reference_only", (await harness.Client.GetCommodityAsync("0101210000", default)).Status);
        Assert.Equal(1, harness.TokenHandler.Calls);
        harness.Clock.Now = harness.Clock.Now.AddSeconds(40);
        Assert.Equal("reference_only", (await harness.Client.GetCommodityAsync("0101210000", default)).Status);
        Assert.Equal(2, harness.TokenHandler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_refresh_has_one_token_request_including_failure_cooldown(bool fail)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new Harness(token: async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return fail ? new(HttpStatusCode.ServiceUnavailable) : Token();
        });
        var pending = Enumerable.Range(0, 12).Select(_ => harness.Client.GetCommodityAsync("0101210000", default)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var results = await Task.WhenAll(pending);
        Assert.All(results, result => Assert.Equal(fail ? "upstream_error" : "reference_only", result.Status));
        Assert.Equal(1, harness.TokenHandler.Calls);
        Assert.Equal(fail ? 0 : 12, harness.ApiHandler.Calls);
        if (fail)
        {
            harness.Clock.Now = harness.Clock.Now.AddSeconds(5);
            await harness.Client.GetCommodityAsync("0101210000", default);
            Assert.Equal(2, harness.TokenHandler.Calls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_401_refreshes_once_and_a_second_401_stays_closed(bool alwaysUnauthorized)
    {
        var issued = 0;
        var requests = 0;
        using var harness = new Harness(token: (_, _) => Task.FromResult(Token("token-" + ++issued)),
            api: (request, _) =>
            {
                var call = ++requests;
                Assert.Equal("token-" + call, request.Headers.Authorization!.Parameter);
                return Task.FromResult(call == 1 || alwaysUnauthorized ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : Json("{\"data\":{\"id\":\"1011\",\"type\":\"geographical_area\"}}"));
            });
        var result = await harness.Client.GetGeographicalAreaAsync("1011", default);
        Assert.Equal(alwaysUnauthorized ? "upstream_error" : "reference_only", result.Status);
        Assert.Equal(2, harness.TokenHandler.Calls);
        Assert.Equal(2, harness.ApiHandler.Calls);
        if (alwaysUnauthorized)
        {
            Assert.Null(result.Data);
            Assert.Equal("token-3", await harness.Provider.GetAsync(default));
        }
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Other_api_failures_are_not_retried_or_mapped_to_data(int status)
    {
        using var harness = new Harness(api: (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var result = await harness.Client.GetCommodityAsync("0101210000", default);
        Assert.Equal("upstream_error", result.Status);
        Assert.Null(result.Data);
        Assert.Equal(1, harness.TokenHandler.Calls);
        Assert.Equal(1, harness.ApiHandler.Calls);
    }

    [Fact]
    public async Task A_delayed_old_401_cannot_evict_a_new_token()
    {
        var issued = 0;
        using var harness = new Harness(token: (_, _) => Task.FromResult(Token("token-" + ++issued)));
        var old = await harness.Provider.GetAsync(default);
        harness.Provider.Invalidate(old!);
        var current = await harness.Provider.GetAsync(default);
        harness.Provider.Invalidate(old!);
        Assert.Equal(current, await harness.Provider.GetAsync(default));
        Assert.Equal(2, harness.TokenHandler.Calls);
    }

    [Fact]
    public async Task Credential_removal_and_rotation_cannot_reuse_an_old_cached_area()
    {
        var reject = false;
        using var harness = new Harness(token: (_, _) => Task.FromResult(reject ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Token()));
        Assert.Equal("reference_only", (await harness.Client.GetGeographicalAreaAsync("1011", default)).Status);
        harness.Configuration["TRADE_UK_TARIFF_CLIENT_SECRET"] = null;
        Assert.Equal("not_configured", (await harness.Client.GetGeographicalAreaAsync("1011", default)).Status);
        reject = true;
        harness.Configuration["TRADE_UK_TARIFF_CLIENT_SECRET"] = "replacement example secret";
        Assert.Equal("upstream_error", (await harness.Client.GetGeographicalAreaAsync("1011", default)).Status);
        Assert.Equal(2, harness.TokenHandler.Calls);
        Assert.Equal(1, harness.ApiHandler.Calls);
        reject = false;
        harness.Configuration["TRADE_UK_TARIFF_CLIENT_SECRET"] = "another replacement example secret";
        Assert.Equal("reference_only", (await harness.Client.GetGeographicalAreaAsync("1011", default)).Status);
        Assert.Equal(3, harness.TokenHandler.Calls);
        Assert.Equal(2, harness.ApiHandler.Calls);
    }

    [Fact]
    public async Task Cancellation_prevents_token_and_api_network_activity()
    {
        using var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await harness.Client.GetCommodityAsync("0101210000", cancellation.Token);
        Assert.Equal("upstream_error", result.Status);
        Assert.Equal(0, harness.TokenHandler.Calls);
        Assert.Equal(0, harness.ApiHandler.Calls);
    }

    [Fact]
    public async Task Token_transport_exception_details_do_not_reach_results()
    {
        using var harness = new Harness(token: (_, _) => throw new HttpRequestException("example secret + example-access-token"));
        var result = await harness.Client.GetCommodityAsync("0101210000", default);
        Assert.Equal("upstream_error", result.Status);
        Assert.DoesNotContain("example", JsonSerializer.Serialize(result));
        Assert.Equal(0, harness.ApiHandler.Calls);
    }
}
