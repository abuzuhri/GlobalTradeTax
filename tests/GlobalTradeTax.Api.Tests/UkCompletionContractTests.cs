using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlobalTradeTax.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;
using static UkCompletionTestData;

public sealed class UkCompletionContractTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requests.Add(request.RequestUri!); Assert.Null(request.Headers.Authorization); return Task.FromResult(respond(request)); }
    }
    private static UkTariffReferenceClient Client(HttpClient http, IMemoryCache cache, string? xi = "https://www.trade-tariff.service.gov.uk/xi/api/v2/") =>
        new(http, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["TRADE_UK_TARIFF_API_BASE_URL"] = "https://www.trade-tariff.service.gov.uk/uk/api/v2/",
            ["TRADE_UK_TARIFF_XI_API_BASE_URL"] = xi }).Build(), cache);
    private static UkReferenceResult Mutate(string fixture, Action<JsonNode> mutate, string territory = "GB")
    {
        var source = Fixture(fixture, territory); var root = JsonNode.Parse(Bytes(fixture))!; mutate(root);
        return source with { Data = JsonSerializer.SerializeToElement(root) };
    }

    [Fact]
    public void All_seven_exact_responses_match_their_attributed_manifest()
    {
        using var manifest = JsonDocument.Parse(Bytes("provenance.json"));
        var responses = manifest.RootElement.GetProperty("responses").EnumerateArray().ToArray();
        Assert.Equal(7, responses.Length);
        foreach (var r in responses)
        {
            var bytes = Bytes(r.GetProperty("file").GetString()!);
            Assert.Equal(bytes.Length, r.GetProperty("bytes").GetInt32());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), r.GetProperty("sha256").GetString());
        }
    }

    [Fact]
    public async Task GB_and_XI_cache_entries_and_exact_body_hashes_are_distinct()
    {
        using var handler = new Handler(r => new(HttpStatusCode.OK) { Content = new ByteArrayContent(
            System.Text.Encoding.UTF8.GetBytes("{\"data\":{\"type\":\"geographical_area\",\"id\":\"1011\"},\"meta\":\"" + r.RequestUri!.AbsolutePath + "\"}")) });
        using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var client = Client(http, cache);
        var gb = await client.GetGeographicalAreaAsync("1011", Date, "GB", default);
        var xi = await client.GetGeographicalAreaAsync("1011", Date, "XI", default);
        Assert.Equal("reference_only", gb.Status); Assert.Equal("reference_only", xi.Status);
        Assert.NotEqual(gb.ResponseSha256, xi.ResponseSha256);
        Assert.Equal("XI", xi.DestinationTerritory);
        Assert.Same(xi, await client.GetGeographicalAreaAsync("1011", Date, "XI", default));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, uri => Assert.Equal("?as_of=2026-11-05", uri.Query));
        Assert.NotNull(xi.CapturedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://www.trade-tariff.service.gov.uk/uk/api/v2/")]
    [InlineData("https://example.test/xi/api/v2/")]
    [InlineData("https://api.trade-tariff.service.gov.uk/uk/api/")]
    public async Task Missing_or_cross_territory_XI_configuration_never_falls_back_to_GB(string? xi)
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("No network expected"));
        using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var result = await Client(http, cache, xi).GetCommodityAsync(Code, Date, "XI", default);
        Assert.Equal("not_configured", result.Status); Assert.Equal("XI", result.DestinationTerritory);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FX_requests_use_the_documented_distinct_feed_contracts()
    {
        using var handler = new Handler(r => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes(
            r.RequestUri!.AbsolutePath.Contains("monetary_exchange_rates") ? "xi-tariff-fx.json" : "gb-monthly-fx.json")) });
        using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 20 });
        var client = Client(http, cache);
        var invoice = await client.GetMonthlyExchangeRatesAsync(Date, default);
        var tariff = await client.GetTariffExchangeRatesAsync(Date, default);
        Assert.Equal("reference_only", invoice.Status); Assert.Equal("reference_only", tariff.Status);
        Assert.Contains("/uk/api/v2/exchange_rates/2026-11", handler.Requests[0].AbsoluteUri);
        Assert.Contains("filter%5Btype%5D=monthly", handler.Requests[0].AbsoluteUri);
        Assert.Contains("/xi/api/v2/monetary_exchange_rates?as_of=2026-11-05", handler.Requests[1].AbsoluteUri);
        var parsed = UkExchangeRates.Monthly(invoice, Date);
        Assert.Empty(parsed.Checks); Assert.NotEmpty(parsed.Rates);
        Assert.All(parsed.Rates, rate => Assert.Equal("invoice_customs_value", rate.Purpose));
        Assert.Empty(UkExchangeRates.Tariff(invoice, Date).Rates);
        Assert.Empty(UkExchangeRates.Monthly(tariff, Date).Rates);
    }

    [Fact]
    public void Monthly_rates_invert_units_per_GBP_and_require_matching_context()
    {
        var source = Fixture("gb-monthly-fx.json");
        var root = source.Data!.Value;
        var record = root.GetProperty("included").EnumerateArray().First(x => x.GetProperty("type").GetString() == "exchange_rate" &&
            x.GetProperty("attributes").GetProperty("currency_code").GetString() == "USD");
        var published = decimal.Parse(record.GetProperty("attributes").GetProperty("rate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var rate = Assert.Single(UkExchangeRates.Monthly(source, Date).Rates, x => x.Currency == "USD");
        Assert.Equal(1m / published, rate.GbpPerUnit);
        Assert.Empty(UkExchangeRates.Monthly(source with { DestinationTerritory = "XI" }, Date).Rates);
        Assert.Empty(UkExchangeRates.Monthly(source, Date.AddMonths(1)).Rates);
    }

    [Fact]
    public void Tariff_conversion_is_monthly_without_using_the_latest_available_month()
    {
        var source = Fixture("xi-tariff-fx.json", "XI");
        var october = new DateOnly(2026,10,31);
        Assert.Equal(.85785m, Assert.Single(UkExchangeRates.Tariff(source with { AsOfDate = october }, october).Rates).GbpPerUnit);
        var december = Date.AddMonths(1);
        Assert.Empty(UkExchangeRates.Tariff(source with { AsOfDate = december }, december).Rates);
        var bad = Mutate("xi-tariff-fx.json", r => {
            var records = r["data"]!.AsArray(); var november = records.Last()!.DeepClone();
            november["attributes"]!["exchange_rate"] = "0.9"; records.Add(november);
        }, "XI");
        Assert.Empty(UkExchangeRates.Tariff(bad, Date).Rates);
    }

    [Fact]
    public void Conditions_preserve_alternative_sets_and_declarations_are_never_clearance()
    {
        var report = UkMeasureRules.Read(Fixture("gb-compound.json"), "0201300090");
        Assert.Contains(report.Measures, x => x.Conditions.Any(c => c.DocumentCode == "C084"));
        Assert.Contains(report.Measures, x => x.ConditionAlternatives.Any(g => g.Options.Length > 1));
        foreach (var rule in report.Measures)
            foreach (var group in rule.ConditionAlternatives.Where(x => x.Complete))
                Assert.All(group.Options.SelectMany(x => x), id => Assert.Contains(rule.Conditions, c => c.Id == id));
    }

    [Fact]
    public void All_reference_projections_preserve_XI_identity_and_exact_source_digest()
    {
        var source = Fixture("xi-compound.json","XI"); const string code = "0201300090";
        var normalized = UkTariffNormalizer.Normalize(code,source);
        var screen = UkMeasureScreening.Screen(source,code,"US",Date);
        var evidence = UkMeasureEvidence.Analyze(source,code,"US",Date);
        var geography = UkGeographyResolver.Analyze(source,code,"US",Date);
        Assert.All(new[] { normalized.DestinationTerritory,screen.DestinationTerritory,evidence.DestinationTerritory,geography.DestinationTerritory },x=>Assert.Equal("XI",x));
        Assert.All(new[] { normalized.ResponseSha256,screen.ResponseSha256,evidence.ResponseSha256,geography.ResponseSha256 },x=>Assert.Equal(source.ResponseSha256,x));
    }

    [Fact]
    public void Missing_resources_duplicates_and_malformed_flags_cannot_produce_complete_rules()
    {
        var missing = Mutate("gb-compound.json", r => {
            var included = r["included"]!.AsArray();
            var measure = included.First(x => x!["id"]!.GetValue<string>() == "20000070")!;
            measure["relationships"]!["order_number"] = JsonNode.Parse("{\"data\":{\"id\":\"missing-quota\",\"type\":\"order_number\"}}");
            measure["attributes"]!.AsObject().Remove("vat");
        });
        var mfn = Assert.Single(UkMeasureRules.Read(missing,"0201300090").Measures, x => x.MeasureId == "20000070");
        Assert.False(mfn.Complete); Assert.Equal(new[] { "missing-quota" }, mfn.QuotaIds);
        Assert.Contains("measure_flag_unverified:vat",mfn.Checks);
        var duplicate = Mutate("gb-compound.json", r => r["included"]!.AsArray().Add(r["included"]![0]!.DeepClone()));
        var report = UkMeasureRules.Read(duplicate,"0201300090");
        Assert.False(report.Complete); Assert.All(report.Measures, x => Assert.False(x.Complete));
    }

    [Fact]
    public async Task Recorded_response_flows_through_client_assessment_and_shipment_review()
    {
        using var handler = new Handler(r => {
            var path = r.RequestUri!.AbsolutePath;
            if (path.Contains("/commodities/")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes("gb-compound.json")) };
            if (path.EndsWith("/1011")) return new(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory,"Fixtures","UkTariff","dated-area-1011.json"))) };
            return new(HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler); using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var client = Client(http,cache);
        var request = Request();
        request = request with { Lines = [request.Lines![0] with { CommodityCode = "0201300090", DutyMeasureId = "20000070", VatMeasureId = "-1012549171" }] };
        var result = await new UkShipmentReview(new(client),client).ReviewAsync(request,default);
        Assert.Equal("requires_review",result.Status);
        Assert.Equal(373m,Assert.Single(result.Lines).IllustrativeDutyGbp);
        // Only the duty's world group was resolved. VAT group 1400 deliberately returned 404.
        Assert.Null(result.Lines[0].ProposedVatRatePercent);
        Assert.Null(result.Lines[0].IllustrativeVatGbp);
        Assert.Null(result.IllustrativeTotals);
        Assert.Contains(result.Lines[0].Assessment.Areas,a=>a.AreaId=="1400" && a.LookupStatus=="not_found");
        Assert.False(result.ApprovedForPayment); Assert.Null(result.PayableDutyGbp);
        Assert.NotNull(result.Lines[0].Assessment.ResponseSha256);
    }
}
