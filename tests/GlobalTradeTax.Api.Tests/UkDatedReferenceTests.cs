using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using GlobalTradeTax.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;
using static UkApplicabilityTestData;

public sealed class UkDatedReferenceTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        internal readonly ConcurrentBag<string> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(respond(request));
        }
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static UkTariffReferenceClient Client(Handler handler, IMemoryCache cache) => new(new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["TRADE_UK_TARIFF_API_BASE_URL"] = "https://www.trade-tariff.service.gov.uk/uk/api/v2/" }).Build(), cache);

    [Fact]
    public async Task Commodity_and_area_use_the_same_invariant_import_date()
    {
        using var handler = new Handler(request => Json(request.RequestUri!.AbsolutePath.Contains("commodities", StringComparison.Ordinal)
            ? Commodity(Measure(area: "1011")).ToJsonString() : AreaDocument("1011", "US").ToJsonString()));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var report = await new UkImportAssessment(Client(handler, cache)).AssessAsync(Code, "US", Date, default);
            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, url => Assert.EndsWith("?as_of=2026-11-05", url));
            Assert.Equal("candidate_for_review", Assert.Single(report.Measures).Applicability!.Status);
            Assert.Equal(Date, Assert.Single(report.Areas).AsOfDate);
            Assert.Equal(new[] { "1011", "US" }, report.Areas[0].MembershipPath);
            Assert.Null(report.ImportDuty);
            Assert.Null(report.ImportVat);
            Assert.False(report.VerifiedForCalculation);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Area_cache_separates_two_dates_and_undated_requests()
    {
        using var handler = new Handler(_ => Json(AreaDocument("1011", "US").ToJsonString()));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var client = Client(handler, cache);
        foreach (var date in new DateOnly?[] { Date, Date.AddDays(-1), null })
        {
            var first = await client.GetGeographicalAreaAsync("1011", date, default);
            var replay = await client.GetGeographicalAreaAsync("1011", date, default);
            Assert.Equal(date, first.AsOfDate);
            Assert.Equal(first, replay);
        }
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Future_dates_that_upstream_would_replace_are_rejected_without_network()
    {
        using var handler = new Handler(_ => throw new InvalidOperationException("No network expected"));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var client = Client(handler, cache);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(21);
        Assert.Equal("invalid_import_date", (await client.GetCommodityAsync(Code, date, default)).Status);
        Assert.Equal("invalid_import_date", (await client.GetGeographicalAreaAsync("1011", date, default)).Status);
        Assert.Equal("invalid_import_date", (await new UkImportAssessment(client).AssessAsync(Code, "US", date, default)).Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Missing_included_measures_stays_non_payable_without_throwing()
    {
        var root = Commodity();
        root.Remove("included");
        using var handler = new Handler(_ => Json(root.ToJsonString()));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var report = await new UkImportAssessment(Client(handler, cache)).AssessAsync(Code, "US", Date, default);
        Assert.Empty(report.Measures);
        Assert.Contains("no_measures_is_not_zero_duty", report.Warnings);
        Assert.Null(report.ImportDuty);
    }

    [Fact]
    public async Task Overlapping_direct_group_and_export_measures_stay_separate()
    {
        var export = Measure("export1");
        export["attributes"]!["import"] = false;
        export["attributes"]!["export"] = true;
        var excluded = Measure("excluded1");
        excluded["relationships"]!["excluded_countries"] = Many("geographical_area", "US");
        var root = Commodity(Measure("direct1"), Measure("group1", "1011"), excluded, export);
        root["data"]!["relationships"]!["import_measures"] = Many("measure", "direct1", "group1", "excluded1");
        using var handler = new Handler(request => Json(request.RequestUri!.AbsolutePath.Contains("commodities", StringComparison.Ordinal)
            ? root.ToJsonString() : AreaDocument("1011", "US").ToJsonString()));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var report = await new UkImportAssessment(Client(handler, cache)).AssessAsync(Code, "US", Date, default);
        Assert.Equal(4, report.Measures.Length);
        Assert.Equal(2, report.Measures.Count(m => m.Applicability!.Status == "candidate_for_review"));
        Assert.Equal(2, report.Measures.Count(m => m.Applicability!.Status == "not_candidate"));
        Assert.All(report.Measures, m => Assert.False(m.Applicability!.VerifiedForCalculation));
        Assert.Null(report.ImportDuty);
        Assert.Null(report.ImportVat);
    }

    [Fact]
    public async Task Exclusion_groups_are_included_in_the_bounded_lookup_plan()
    {
        var measure = Measure();
        measure["relationships"]!["excluded_countries"] = Many("geographical_area", "1000");
        using var handler = new Handler(request => Json(request.RequestUri!.AbsolutePath.Contains("commodities", StringComparison.Ordinal)
            ? Commodity(measure.DeepClone().AsObject()).ToJsonString() : AreaDocument("1000", "US").ToJsonString()));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var report = await new UkImportAssessment(Client(handler, cache)).AssessAsync(Code, "US", Date, default);
        Assert.Equal("1000", Assert.Single(report.Areas).AreaId);
        Assert.Equal("not_candidate", Assert.Single(report.Measures).Applicability!.Status);
    }

    [Fact]
    public async Task Measure_screening_budget_is_visible_in_the_assessment()
    {
        var root = Commodity(Enumerable.Range(0, 201).Select(i => Measure("measure" + i)).ToArray());
        using var handler = new Handler(_ => Json(root.ToJsonString()));
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var report = await new UkImportAssessment(Client(handler, cache)).AssessAsync(Code, "US", Date, default);
        Assert.Equal(200, report.Measures.Length);
        Assert.Contains("measure_screening_limit_exceeded_incomplete_evidence", report.Warnings);
        Assert.Null(report.ImportDuty);
    }
}
