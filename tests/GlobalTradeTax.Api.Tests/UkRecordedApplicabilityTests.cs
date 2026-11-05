using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlobalTradeTax.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;
using static UkApplicabilityTestData;

public sealed class UkRecordedApplicabilityTests
{
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            var uri = request.RequestUri!;
            Assert.Equal("?as_of=" + (uri.Query.Contains("2022", StringComparison.Ordinal) ? "2022-01-01" : "2026-11-05"), uri.Query);
            var name = uri.AbsolutePath.Contains("commodities", StringComparison.Ordinal) ? "dated-commodity-0101210000.json"
                : uri.AbsolutePath.EndsWith("/1011", StringComparison.Ordinal) ? "dated-area-1011.json"
                : uri.Query.Contains("2022", StringComparison.Ordinal) ? "dated-area-1060-before-start.json" : "dated-area-1060.json";
            return Task.FromResult(new HttpResponseMessage(name.Contains("before-start", StringComparison.Ordinal) ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new StringContent(Fixture(name), Encoding.UTF8, "application/json") });
        }
    }

    private static UkTariffReferenceClient Client(Handler handler, IMemoryCache cache) => new(new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["TRADE_UK_TARIFF_API_BASE_URL"] = "https://www.trade-tariff.service.gov.uk/uk/api/v2/" }).Build(), cache);

    [Fact]
    public void All_dated_fixtures_match_recorded_bytes_hashes_and_request_dates()
    {
        using var provenance = JsonDocument.Parse(Fixture("applicability-provenance.json"));
        var responses = provenance.RootElement.GetProperty("responses").EnumerateArray().ToArray();
        Assert.Equal(4, responses.Length);
        foreach (var response in responses)
        {
            var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "UkTariff", response.GetProperty("file").GetString()!));
            Assert.Equal(response.GetProperty("bytes").GetInt32(), bytes.Length);
            Assert.Equal(response.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.EndsWith("?as_of=" + response.GetProperty("as_of_date").GetString(), response.GetProperty("url").GetString());
        }
    }

    [Fact]
    public async Task Recorded_dated_world_group_contains_US_as_reference_evidence()
    {
        using var handler = new Handler();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var response = await Client(handler, cache).GetGeographicalAreaAsync("1011", Date, default);
        var membership = UkAreaMembership.Inspect(response, "1011", "US", Date);
        Assert.Equal("candidate_membership_evidence", membership.EvidenceStatus);
        Assert.Equal(new[] { "1011", "US" }, membership.MembershipPath);
        Assert.False(membership.VerifiedForCalculation);
    }

    [Fact]
    public async Task Recorded_historical_group_absence_is_not_replaced_by_current_cache()
    {
        using var handler = new Handler();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var client = Client(handler, cache);
        var current = await client.GetGeographicalAreaAsync("1060", Date, default);
        var historical = await client.GetGeographicalAreaAsync("1060", new DateOnly(2022, 1, 1), default);
        Assert.Equal("reference_only", current.Status);
        Assert.Equal("not_found", historical.Status);
        Assert.Equal("candidate_membership_evidence", UkAreaMembership.Inspect(current, "1060", "IN", Date).EvidenceStatus);
        Assert.Equal("unknown", UkAreaMembership.Inspect(historical, "1060", "IN", new DateOnly(2022, 1, 1)).EvidenceStatus);
        Assert.Equal(current, await client.GetGeographicalAreaAsync("1060", Date, default));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Recorded_commodity_has_67_unique_measures_with_one_shared_import_export_measure()
    {
        using var handler = new Handler();
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var commodity = await Client(handler, cache).GetCommodityAsync(Code, Date, default);
        var evidence = UkMeasureEvidence.Analyze(commodity, Code, "US", Date);
        Assert.Contains(evidence.Measures, m => m.ExclusionReferences > 0);
        var results = evidence.Measures.Select(m => UkMeasureApplicability.Analyze(commodity, Code, m.MeasureId,
            "US", Date, new Dictionary<string, UkAreaMembershipReport>())).ToArray();
        Assert.Equal(67, results.Length);
        Assert.Equal(63, results.Count(m => m.Direction is "import" or "import_and_export"));
        Assert.Single(results, m => m.Direction == "import_and_export");
        Assert.Equal(4, results.Count(m => m.Direction == "other_measure" && m.Status == "not_candidate"));
        Assert.All(results, m => Assert.False(m.VerifiedForCalculation));
        Assert.DoesNotContain(results, m => m.EffectiveWindow == "unknown");
    }
}
