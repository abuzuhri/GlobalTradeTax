using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GlobalTradeTax.Api;
using Xunit;

public sealed class UkDutyReferenceCasesTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string Root = Path.Combine(Fixtures, "UkDutyReference");
    private static JsonElement Read(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.Clone();
    }
    private static JsonElement Matrix() => Read(Path.Combine(Root,"cases.json"));
    private static string Text(JsonElement item, string name) => item.GetProperty(name).GetString()!;
    private static decimal? Amount(JsonElement item, string name) => !item.TryGetProperty(name,out var value) || value.ValueKind == JsonValueKind.Null
        ? null : decimal.Parse(value.GetString()!,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture);

    public static IEnumerable<object[]> Cases() => Matrix().GetProperty("cases").EnumerateArray().Select(c => new object[] { Text(c,"id") });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Engine_matches_independently_recorded_calculator_expectation(string id)
    {
        var c = Matrix().GetProperty("cases").EnumerateArray().Single(x => Text(x,"id") == id);
        var date = DateOnly.ParseExact(Text(c,"import_date"),"yyyy-MM-dd",CultureInfo.InvariantCulture);
        var rates = new List<UkFxRate>();
        UkRuleComponent[] components;
        if (Text(c,"reference_kind") == "observed_live_calculator")
        {
            var territory = Text(c,"destination_territory"); var code = Text(c,"commodity_code");
            var bytes = File.ReadAllBytes(Path.Combine(Fixtures,Text(c,"commodity_fixture")));
            var source = new UkReferenceResult("reference_only",Read(Path.Combine(Fixtures,Text(c,"commodity_fixture"))),
                $"https://www.trade-tariff.service.gov.uk/{(territory == "GB" ? "uk" : "xi")}/api/v2/commodities/{code}?as_of={date:yyyy-MM-dd}","Reference only",date)
                { DestinationTerritory = territory, ResponseSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
            var rule = Assert.Single(UkMeasureRules.Read(source,code).Measures,m => m.MeasureId == Text(c,"measure_id"));
            Assert.True(rule.Complete,string.Join(",",rule.Checks));
            Assert.True(rule.ImportLinked); Assert.Equal("103",rule.MeasureTypeId);
            var evidence = Assert.Single(UkMeasureEvidence.Analyze(source,code,Text(c,"goods_origin"),date).Measures,m=>m.MeasureId==rule.MeasureId);
            Assert.Equal(new[] { "1011" },evidence.GeographicalAreaReferences);
            components = rule.Components;
            if (c.TryGetProperty("fx_fixture",out var fx) && fx.ValueKind == JsonValueKind.String)
            {
                var fxBytes = File.ReadAllBytes(Path.Combine(Fixtures,fx.GetString()!));
                var reference = new UkReferenceResult("reference_only",Read(Path.Combine(Fixtures,fx.GetString()!)),
                    "https://www.trade-tariff.service.gov.uk/xi/api/v2/monetary_exchange_rates?as_of=2026-11-05","Reference only",date)
                    { DestinationTerritory="XI",ResponseSha256=Convert.ToHexString(SHA256.HashData(fxBytes)).ToLowerInvariant() };
                var parsed = UkExchangeRates.Tariff(reference,date);
                Assert.Empty(parsed.Checks); rates.AddRange(parsed.Rates);
            }
            AssertCapturedDutyRow(c,date);
        }
        else
        {
            Assert.Equal("pinned_official_rspec",Text(c,"reference_kind"));
            var item = c.GetProperty("inline_component");
            string? Optional(string name) => item.GetProperty(name).GetString();
            components = [new(Text(item,"id"),Text(item,"expression_code"),Amount(item,"amount"),Optional("currency"),Optional("unit"),Optional("qualifier"),item.GetProperty("complete").GetBoolean())];
            var spec = File.ReadAllText(Path.Combine(Root,Text(c,"source_spec")));
            var expectedLiteral = Text(c,"expected_unrounded_gbp");
            Assert.Contains("value: "+expectedLiteral,spec);
            if (c.TryGetProperty("fixture_fx_source",out var path))
            {
                var bytes = File.ReadAllBytes(Path.Combine(Root,path.GetString()!));
                var published = Read(Path.Combine(Root,path.GetString()!)).EnumerateArray().Select(x=>x.GetProperty("attributes"))
                    .Single(x=>Text(x,"validity_start_date").StartsWith("2021-07-01",StringComparison.Ordinal));
                Assert.Equal(Text(c,"fixture_fx_rate"),Text(published,"exchange_rate"));
                Assert.Equal("GBP",Text(published,"child_monetary_unit_code"));
                rates.Add(new("EUR","tariff_duty",decimal.Parse(Text(published,"exchange_rate"),CultureInfo.InvariantCulture),date,date,
                    "https://github.com/trade-tariff/trade-tariff-frontend/blob/bb5d3b83d07abb95b1cb6c6c548b7393786b86a9/spec/fixtures/duty_calculator/xi/monetary_exchange_rates.json",
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
            }
        }
        var result = UkDutyComponents.Evaluate(components,new(Amount(c,"customs_value_gbp"),Amount(c,"net_mass_kg"),Amount(c,"item_count")),date,rates);
        Assert.Equal("illustrative_only",result.Status);
        Assert.Equal(Amount(c,"expected_unrounded_gbp"),result.IllustrativeGbp);
        Assert.Equal(Amount(c,"expected_displayed_gbp"),decimal.Round(result.IllustrativeGbp!.Value,2,MidpointRounding.AwayFromZero));
        Assert.False(result.VerifiedForCalculation);
    }

    private static void AssertCapturedDutyRow(JsonElement c, DateOnly date)
    {
        var result = File.ReadAllText(Path.Combine(Root,Text(c,"result")));
        var confirm = File.ReadAllText(Path.Combine(Root,Text(c,"confirmation")));
        var lines = result.Split('\n').Select(x=>x.TrimEnd('\r')).ToArray();
        var index = Array.FindIndex(lines,x=>x == "Import duty");
        Assert.True(index >= 0 && index+1 < lines.Length);
        var fields = lines[index+1].Split('\t');
        Assert.Equal(Text(c,"duty_row_label"),fields[0]);
        Assert.Equal(Amount(c,"expected_displayed_gbp"),decimal.Parse(fields[^1].Replace("£",""),NumberStyles.Number,CultureInfo.InvariantCulture));
        Assert.Contains("Duty Total\t\t£"+Text(c,"expected_ui_total_gbp"),result);
        Assert.Equal(Text(c,"commodity_code"),lines.Single(x=>x.StartsWith("Commodity code\t",StringComparison.Ordinal)).Split('\t')[1].Replace(" ",""));
        Assert.Contains(date.ToString("d MMMM yyyy",CultureInfo.InvariantCulture),confirm);
        Assert.Contains("United States",confirm);
        Assert.Contains(Text(c,"destination_territory")=="XI" ? "Northern Ireland" : "England, Scotland or Wales (GB)",confirm);
        Assert.True(File.Exists(Path.Combine(Root,Text(c,"screenshot"))));
    }

    [Fact]
    public void Archived_observations_API_and_official_test_sources_match_their_digests()
    {
        var manifest = Read(Path.Combine(Root,"provenance.json"));
        foreach (var item in manifest.GetProperty("files").EnumerateArray().Concat(manifest.GetProperty("responses").EnumerateArray()))
        {
            var bytes = File.ReadAllBytes(Path.Combine(Root,Text(item,"file")));
            Assert.Equal(item.GetProperty("bytes").GetInt32(),bytes.Length);
            Assert.Equal(Text(item,"sha256"),Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        Assert.Contains("Crown Copyright",File.ReadAllText(Path.Combine(Root,"upstream","LICENCE.md")));
    }

    [Fact]
    public void Comparison_is_explicitly_unapproved_and_does_not_relabel_the_headline_as_duty()
    {
        var matrix = Matrix();
        Assert.Equal("awaiting_independent_review",Text(matrix,"approval_status"));
        Assert.False(matrix.GetProperty("approved_for_payment").GetBoolean());
        var live = matrix.GetProperty("cases").EnumerateArray().Where(c=>Text(c,"reference_kind")=="observed_live_calculator").ToArray();
        Assert.Equal(5,live.Length);
        var percentage = Assert.Single(live,c=>Text(c,"id")=="gb-percentage");
        Assert.Equal(100m,Amount(percentage,"expected_displayed_gbp"));
        Assert.Equal(320m,Amount(percentage,"expected_ui_total_gbp"));
        var zero = Assert.Single(live,c=>Text(c,"id")=="gb-zero");
        Assert.Equal(0m,Amount(zero,"expected_displayed_gbp"));
        Assert.Equal(200m,Amount(zero,"expected_ui_total_gbp"));
    }
}
