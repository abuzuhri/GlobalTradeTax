using GlobalTradeTax.Api;
using Xunit;
using static UkCompletionTestData;

public sealed class UkDutyComponentsTests
{
    [Theory]
    [InlineData("gb-ad-valorem.json", "0101299000", 100)]
    [InlineData("gb-compound.json", "0201300090", 373)]
    public void Recorded_GB_rates_produce_reference_arithmetic_without_legal_approval(string fixture, string code, decimal expected)
    {
        var rules = UkMeasureRules.Read(Fixture(fixture), code);
        // The graph also contains a future zero-rate measure. Select this recorded ID explicitly.
        var mfn = Assert.Single(rules.Measures, x => x.MeasureId == (code == "0101299000" ? "20000002" : "20000070"));
        Assert.True(mfn.Complete, string.Join(",", mfn.Checks));
        var result = UkDutyComponents.Evaluate(mfn.Components, new(1000m, 100m, 1m), Date, []);
        Assert.Equal(expected, result.IllustrativeGbp);
        Assert.False(result.VerifiedForCalculation);
    }

    [Fact]
    public void Recorded_XI_compound_uses_its_published_tariff_conversion_not_invoice_FX()
    {
        var rules = UkMeasureRules.Read(Fixture("xi-compound.json", "XI"), "0201300090");
        var mfn = Assert.Single(rules.Measures, x => x.MeasureTypeId == "103" && x.ImportLinked);
        var rates = UkExchangeRates.Tariff(Fixture("xi-tariff-fx.json", "XI"), Date);
        Assert.Empty(rates.Checks);
        Assert.Equal(0.83578m, Assert.Single(rates.Rates).GbpPerUnit);
        var result = UkDutyComponents.Evaluate(mfn.Components, new(1000m, 100m, 1m), Date, rates.Rates);
        Assert.Equal(381.575652m, result.IllustrativeGbp);
        Assert.False(result.VerifiedForCalculation);
    }

    [Theory]
    [InlineData("KGM", 10, 30)]
    [InlineData("DTN", 1000, 30)]
    [InlineData("TNE", 10000, 30)]
    public void Specific_amounts_use_explicit_net_mass_units(string unit, decimal mass, decimal expected)
    {
        var result = UkDutyComponents.Evaluate([new("c", "01", 3m, "GBP", unit, null, true)], new(null, mass, null), Date, []);
        Assert.Equal(expected, result.IllustrativeGbp);
    }

    [Fact]
    public void Piece_counts_require_whole_explicit_items()
    {
        var component = new UkRuleComponent("c", "01", 4m, "GBP", "NAR", null, true);
        Assert.Equal(12m, UkDutyComponents.Evaluate([component], new(null, null, 3m), Date, []).IllustrativeGbp);
        Assert.Null(UkDutyComponents.Evaluate([component], new(null, null, 3.5m), Date, []).IllustrativeGbp);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("14")]
    [InlineData("21")]
    [InlineData("25")]
    [InlineData("27")]
    [InlineData("29")]
    [InlineData("99")]
    public void Agricultural_additional_and_supplementary_codes_are_not_guessed_as_arithmetic(string code)
    {
        var result = UkDutyComponents.Evaluate([Percent(10), new("extra", code, 20m, null, null, null, true)], new(1000m, 100m, 1m), Date, []);
        Assert.Null(result.IllustrativeGbp);
        Assert.Contains("unsupported_component_operation", result.Checks);
    }

    [Fact]
    public void Missing_or_invalid_inputs_never_default_to_zero()
    {
        Assert.Null(UkDutyComponents.Evaluate([], new(1000, 100, 1), Date, []).IllustrativeGbp);
        Assert.Null(UkDutyComponents.Evaluate([Percent(10)], new(null, 100, 1), Date, []).IllustrativeGbp);
        Assert.Null(UkDutyComponents.Evaluate([Percent(10) with { Amount = null }], new(1000, 100, 1), Date, []).IllustrativeGbp);
        Assert.Null(UkDutyComponents.Evaluate([Percent(10) with { Complete = false }], new(1000, 100, 1), Date, []).IllustrativeGbp);
        Assert.Null(UkDutyComponents.Evaluate([Percent(10)], new(-1, 100, 1), Date, []).IllustrativeGbp);
        Assert.Equal(0m, UkDutyComponents.Evaluate([Percent(0)], new(1000, 100, 1), Date, []).IllustrativeGbp);
    }

    [Fact]
    public void Ambiguous_bases_duplicate_components_qualifiers_and_overflow_are_unresolved()
    {
        Assert.Null(UkDutyComponents.Evaluate([Percent(10), Percent(20)], new(1000, 100, 1), Date, []).IllustrativeGbp);
        Assert.Null(UkDutyComponents.Evaluate([Percent(10), Percent(20, "other")], new(1000, 100, 1), Date, []).IllustrativeGbp);
        Assert.Null(UkDutyComponents.Evaluate([new("c", "01", 2, "GBP", "KGM", "A", true)], new(1000, 100, 1), Date, []).IllustrativeGbp);
        Assert.Null(UkDutyComponents.Evaluate([Percent(decimal.MaxValue)], new(decimal.MaxValue, 100, 1), Date, []).IllustrativeGbp);
    }

    [Fact]
    public void FX_purpose_period_source_and_ambiguity_are_enforced()
    {
        var component = new UkRuleComponent("c", "01", 10m, "EUR", "KGM", null, true);
        var rate = new UkFxRate("EUR", "tariff_duty", .8m, Date, Date.AddDays(1), "https://example.test/reference", null);
        UkDutyArithmetic Evaluate(params UkFxRate[] values) => UkDutyComponents.Evaluate([component], new(null, 10m, null), Date, values);
        Assert.Equal(80m, Evaluate(rate).IllustrativeGbp);
        Assert.Null(Evaluate(rate with { Purpose = "invoice_customs_value" }).IllustrativeGbp);
        Assert.Null(Evaluate(rate with { ValidTo = Date.AddDays(-1) }).IllustrativeGbp);
        Assert.Null(Evaluate(rate with { SourceUrl = "http://example.test/reference" }).IllustrativeGbp);
        Assert.Null(Evaluate(rate, rate).IllustrativeGbp);
    }
}
