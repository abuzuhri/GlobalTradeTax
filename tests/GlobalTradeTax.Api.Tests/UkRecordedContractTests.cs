using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlobalTradeTax.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class UkRecordedContractTests
{
    private sealed class RecordedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "UkTariff", name));

    private static HttpResponseMessage Response(string fixture, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(Fixture(fixture), Encoding.UTF8, "application/json") };

    private static UkTariffReferenceClient Client(HttpClient http, IMemoryCache cache) => new(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TRADE_UK_TARIFF_API_BASE_URL"] = "https://www.trade-tariff.service.gov.uk/uk/api/v2/"
        }).Build(), cache);

    [Fact]
    public void Recorded_fixtures_match_their_provenance_hashes()
    {
        using var provenance = JsonDocument.Parse(Fixture("provenance.json"));
        var records = provenance.RootElement.GetProperty("responses").EnumerateArray().ToArray();
        Assert.Equal(4, records.Length);
        foreach (var record in records)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "UkTariff", record.GetProperty("file").GetString()!);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(record.GetProperty("bytes").GetInt32(), bytes.Length);
            Assert.Equal(record.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }

    [Fact]
    public async Task Recorded_commodity_uses_internal_id_and_keeps_projections_unverified()
    {
        using var handler = new RecordedHandler(_ => Response("commodity-0101210000.json"));
        using var http = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var result = await Client(http, cache).GetCommodityAsync("0101210000", default);
        Assert.Equal("reference_only", result.Status);
        var data = result.Data!.Value.GetProperty("data");
        Assert.Equal("commodity", data.GetProperty("type").GetString());
        Assert.Equal("93796", data.GetProperty("id").GetString());

        var normalized = UkTariffNormalizer.Normalize("0101210000", result);
        Assert.Equal("0101210000", normalized.CommodityCode);
        Assert.NotEmpty(normalized.MeasureReferences);
        Assert.False(normalized.VerifiedForCalculation);
        var screen = UkMeasureScreening.Screen(result, "0101210000", "US", new DateOnly(2026, 11, 4));
        Assert.Equal("reference_screened", screen.Status);
        Assert.NotEmpty(screen.Measures);
        Assert.False(screen.VerifiedForCalculation);
        var evidence = UkMeasureEvidence.Analyze(result, "0101210000", "US", new DateOnly(2026, 11, 4));
        Assert.NotEmpty(evidence.Measures);
        Assert.False(evidence.VerifiedForCalculation);
        Assert.All(evidence.Measures, measure => Assert.Equal("requires_review", measure.Status));
    }

    [Fact]
    public async Task Recorded_area_is_cached_without_inventing_dated_membership()
    {
        using var handler = new RecordedHandler(_ => Response("geographical-area-1011.json"));
        using var http = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var client = Client(http, cache);
        var result = await client.GetGeographicalAreaAsync("1011", default);
        Assert.Equal("reference_only", result.Status);
        Assert.Equal("reference_only", (await client.GetGeographicalAreaAsync("1011", default)).Status);
        Assert.Equal(1, handler.Calls);
        var membership = UkAreaMembership.Inspect(result, "1011", "US", new DateOnly(2026, 11, 4));
        Assert.Equal("unknown", membership.EvidenceStatus);
        Assert.Contains("no_explicit_dated_membership_proof", membership.UnresolvedChecks);
        Assert.False(membership.VerifiedForCalculation);
    }

    [Theory]
    [InlineData("commodity-not-found.json", false)]
    [InlineData("geographical-area-not-found.json", true)]
    public async Task Recorded_missing_resource_returns_no_data_and_is_not_cached(string fixture, bool area)
    {
        using var handler = new RecordedHandler(_ => Response(fixture, HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var client = Client(http, cache);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = area
                ? await client.GetGeographicalAreaAsync("ZZZZZZZZZZZZ", default)
                : await client.GetCommodityAsync("0000000000", default);
            Assert.Equal("not_found", result.Status);
            Assert.Null(result.Data);
        }
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Recorded_commodity_can_be_assessed_when_area_lookups_fail_without_payable_amounts()
    {
        using var handler = new RecordedHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("/commodities/", StringComparison.Ordinal)
                ? Response("commodity-0101210000.json")
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var report = await new UkImportAssessment(Client(http, cache))
            .AssessAsync("0101210000", "US", new DateOnly(2026, 11, 4), default);
        Assert.Equal("requires_review", report.Status);
        Assert.NotEmpty(report.Measures);
        Assert.NotEmpty(report.Areas);
        Assert.All(report.Areas, area => Assert.Equal("upstream_error", area.LookupStatus));
        Assert.All(report.Measures, measure => Assert.Contains("measure_type_and_duty_expression_not_legally_validated", measure.OutstandingChecks));
        Assert.Contains(report.Measures, measure => measure.OutstandingChecks.Any(
            check => check.StartsWith("group_membership_not_verified:", StringComparison.Ordinal)));
        Assert.Null(report.ImportDuty);
        Assert.Null(report.ImportVat);
        Assert.False(report.VerifiedForCalculation);
        Assert.False(UkAssessmentEvidencePackage.Build(report, DateTimeOffset.UtcNow).ApprovedForPayment);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("type")]
    [InlineData("code")]
    [InlineData("attributes")]
    [InlineData("relationships")]
    [InlineData("included")]
    [InlineData("included-resource")]
    [InlineData("included-type")]
    [InlineData("errors-array")]
    [InlineData("errors-object")]
    public async Task Corrupted_recorded_commodity_fails_closed(string corruption)
    {
        var root = JsonNode.Parse(Fixture("commodity-0101210000.json"))!;
        switch (corruption)
        {
            case "id": root["data"]!["id"] = ""; break;
            case "type": root["data"]!["type"] = "geographical_area"; break;
            case "code": root["data"]!["attributes"]!["goods_nomenclature_item_id"] = "9999999999"; break;
            case "attributes": root["data"]!["attributes"] = new JsonArray(); break;
            case "relationships": root["data"]!["relationships"] = new JsonArray(); break;
            case "included": root["included"] = new JsonObject(); break;
            case "included-resource": root["included"]!.AsArray()[0] = null; break;
            case "included-type": root["included"]!.AsArray()[0]!["type"] = 123; break;
            case "errors-array": root["errors"] = new JsonArray(); break;
            case "errors-object": root["errors"] = new JsonObject(); break;
        }
        using var handler = new RecordedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json") });
        using var http = new HttpClient(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var result = await Client(http, cache).GetCommodityAsync("0101210000", default);
        Assert.Equal("invalid_upstream_contract", result.Status);
        Assert.Null(result.Data);
    }
}
