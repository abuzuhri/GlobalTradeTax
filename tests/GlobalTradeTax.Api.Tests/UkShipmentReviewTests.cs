using GlobalTradeTax.Api;
using Xunit;
using static UkCompletionTestData;

public sealed class UkShipmentReviewTests
{
    private static UkShipmentReviewResult Run(UkShipmentReviewRequest request, params UkAssessmentReport[] reports) =>
        UkShipmentReview.Compose(request, Sources(reports.Length == 0 ? [Assessment()] : reports), []);

    [Fact]
    public void Explicit_GB_components_reconcile_without_becoming_payable()
    {
        var result = Run(Request() with { Costs = [new("freight", 30m, false), new("insurance", 10m, false)] });
        var line = Assert.Single(result.Lines);
        Assert.Equal(100m, line.IllustrativeDutyGbp);
        Assert.Equal(1140m, line.IllustrativeVatBaseGbp);
        Assert.Equal(228m, line.IllustrativeVatGbp);
        Assert.Equal(new UkIllustrativeTotals(1000m, 100m, 40m, 1140m, 228m, 1368m), result.IllustrativeTotals);
        Assert.False(result.VerifiedForCalculation);
        Assert.False(result.ApprovedForPayment);
        Assert.Null(result.PayableDutyGbp);
        Assert.Null(result.PayableVatGbp);
        Assert.Null(result.PayableLandedTotalGbp);
        Assert.False(line.VerifiedForCalculation);
    }

    [Fact]
    public void Shared_costs_allocate_by_value_once_and_line_VAT_rates_remain_distinct()
    {
        var request = Request() with { Lines = [
            new("a", Code, "US", 100m, DutyMeasureId: "duty", VatMeasureId: "vat"),
            new("b", "0201300090", "CN", 200m, DutyMeasureId: "duty", VatMeasureId: "vat")],
            Costs = [new("already_in_customs", 99m, true), new("shared", 10m, false), new("b_only", 5m, false, ["b"])] };
        var result = Run(request, Assessment(duty: 0, vat: 20), Assessment("0201300090", "CN", duty: 10, vat: 5));
        Assert.Equal(new decimal?[] { 3.33m, 11.67m }, result.Lines.Select(x => x.AdditionalVatCostsGbp));
        Assert.Equal(new decimal?[] { 20.67m, 11.58m }, result.Lines.Select(x => x.IllustrativeVatGbp));
        var totals = Assert.IsType<UkIllustrativeTotals>(result.IllustrativeTotals);
        Assert.Equal(result.Lines.Sum(x => x.IllustrativeVatBaseGbp), totals.VatBaseGbp);
        Assert.Equal(15m, totals.AdditionalVatCostsGbp);
        Assert.Equal(32.25m, totals.ImportVatGbp);
        Assert.Equal(367.25m, totals.ValuePlusTaxesGbp);
    }

    [Theory]
    [InlineData(null, "incidental")]
    [InlineData(false, "royalties")]
    public void Unknown_cost_inclusion_or_special_valuation_suppresses_totals(bool? included, string category)
    {
        var result = Run(Request() with { Costs = [new("c", 10m, included, Category: category)] });
        Assert.Null(result.IllustrativeTotals);
        Assert.Null(result.Lines[0].IllustrativeVatGbp);
        Assert.Null(result.Lines[0].AdditionalVatCostsGbp);
    }

    [Fact]
    public void Positive_cost_cannot_be_allocated_to_zero_customs_values()
    {
        var request = Request();
        var result = Run(request with { Lines = [request.Lines![0] with { CustomsValue = 0 }], Costs = [new("c", 1m, false)] });
        Assert.Null(result.IllustrativeTotals);
        Assert.Contains("cost_allocation_indeterminate:c", result.Checks);
    }

    [Theory]
    [InlineData("XI", "US", "free_circulation", "commercial", 1000)]
    [InlineData("GB", "XI", "free_circulation", "commercial", 1000)]
    [InlineData("GB", "US", "inward_processing", "commercial", 1000)]
    [InlineData("GB", "US", "free_circulation", "gift", 1000)]
    [InlineData("GB", "US", "free_circulation", "commercial", 135)]
    [InlineData("GB", "US", "free_circulation", null, 1000)]
    public void NI_low_value_gifts_and_special_procedures_do_not_inherit_GB_tax_treatment(
        string territory, string shipFrom, string procedure, string? kind, decimal intrinsic)
    {
        var result = Run(Request() with { DestinationTerritory = territory, ShipFrom = shipFrom, ImportProcedure = procedure,
            TransactionKind = kind, IntrinsicValueGbp = intrinsic }, Assessment(territory: territory));
        Assert.Null(result.IllustrativeTotals);
        Assert.Null(result.Lines[0].IllustrativeDutyGbp);
        Assert.Null(result.Lines[0].IllustrativeVatGbp);
        Assert.False(result.ApprovedForPayment);
    }

    [Fact]
    public void Missing_measure_selection_never_chooses_the_cheapest_available_rate()
    {
        var request = Request(); var report = Assessment();
        report = report with { Measures = [.. report.Measures, report.Measures[0] with { MeasureId = "zero", Rule = Rule("zero", 0) }] };
        var result = Run(request with { Lines = [request.Lines![0] with { DutyMeasureId = null, VatMeasureId = null }] }, report);
        Assert.Equal(2, result.Lines[0].DutyScenarios.Length);
        Assert.Null(result.IllustrativeTotals);
        Assert.Null(result.Lines[0].IllustrativeDutyGbp);
        Assert.Null(result.Lines[0].ProposedVatRatePercent);
        Assert.Contains("explicit_duty_measure_selection_required_no_cheapest_rate_inferred", result.Lines[0].Checks);
    }

    [Fact]
    public void Incomplete_rule_graph_does_not_emit_scenario_amounts()
    {
        var report = Assessment();
        report = report with { Measures = [report.Measures[0] with { Rule = report.Measures[0].Rule! with { Complete = false } }, report.Measures[1]] };
        var result = Run(Request(),report);
        Assert.Null(result.Lines[0].DutyScenarios[0].Arithmetic.IllustrativeGbp);
        Assert.Null(result.IllustrativeTotals);
    }

    [Theory]
    [InlineData("excluded")]
    [InlineData("indeterminate")]
    public void Non_candidate_measures_have_no_even_illustrative_arithmetic(string status)
    {
        var report = Assessment();
        report = report with { Measures = [report.Measures[0] with { Applicability = report.Measures[0].Applicability! with { Status = status } }, report.Measures[1]] };
        var result = Run(Request(), report);
        Assert.Null(result.Lines[0].DutyScenarios[0].Arithmetic.IllustrativeGbp);
        Assert.Null(result.IllustrativeTotals);
    }

    [Theory]
    [InlineData("quota")]
    [InlineData("preference")]
    [InlineData("additional")]
    [InlineData("certificate")]
    public void Selected_conditional_rate_requires_entitlement_even_when_document_is_declared(string kind)
    {
        var report = Assessment(); var rule = report.Measures[0].Rule!;
        rule = kind switch { "quota" => rule with { QuotaIds = ["090001"] }, "preference" => rule with { PreferenceCode = "300" },
            "additional" => rule with { AdditionalCodeIds = ["A001"] },
            _ => rule with { Conditions = [new("c", "B", "29", "C640", "Control certificate", true)] } };
        report = report with { Measures = [report.Measures[0] with { Rule = rule }, report.Measures[1]] };
        var request = Request();
        var result = Run(request with { Lines = [request.Lines![0] with { DeclaredDocuments = ["C640"] }] }, report);
        Assert.Null(result.IllustrativeTotals);
        Assert.Null(result.Lines[0].IllustrativeDutyGbp);
        Assert.False(result.ApprovedForPayment);
        Assert.EndsWith("not_verified", Assert.Single(result.Lines[0].DocumentDeclarations).Status);
    }

    [Theory]
    [InlineData("territory")]
    [InlineData("date")]
    [InlineData("origin")]
    [InlineData("code")]
    public void Evidence_cannot_cross_the_line_context(string mismatch)
    {
        var report = Assessment();
        var changed = mismatch switch { "territory" => report with { DestinationTerritory = "XI" },
            "date" => report with { ImportDate = Date.AddDays(-1) }, "origin" => report with { Origin = "CN" },
            _ => report with { CommodityCode = "0201300090" } };
        var result = UkShipmentReview.Compose(Request(), new Dictionary<(string, string), UkAssessmentReport> { [(Code, "US")] = changed }, []);
        Assert.Null(result.IllustrativeTotals);
        Assert.Contains("line_reference_context_unverified", result.Lines[0].Checks);
    }

    [Fact]
    public void Missing_one_line_does_not_make_partial_shipment_totals_complete()
    {
        var request = Request() with { Lines = [Request().Lines![0], new("b", "0201300090", "CN", 100m)] };
        var result = Run(request);
        Assert.Null(result.IllustrativeTotals);
        Assert.Equal(220m, result.Lines[0].IllustrativeVatGbp);
        Assert.Null(result.Lines[1].IllustrativeVatGbp);
    }

    [Theory]
    [InlineData("footnote")]
    [InlineData("additional")]
    [InlineData("condition")]
    public void Proposed_VAT_rate_does_not_resolve_conditional_VAT_treatment(string kind)
    {
        var report = Assessment(); var rule = report.Measures[1].Rule!;
        rule = kind switch { "footnote" => rule with { FootnoteIds = ["03026"] },
            "additional" => rule with { AdditionalCodeIds = ["VATZ"] },
            _ => rule with { Conditions = [new("condition","B","29","C084",null,true)] } };
        report = report with { Measures = [report.Measures[0],report.Measures[1] with { Rule = rule }] };
        var result = Run(Request(),report);
        Assert.Equal(20m,result.Lines[0].ProposedVatRatePercent);
        Assert.Null(result.Lines[0].IllustrativeVatGbp);
        Assert.Null(result.IllustrativeTotals);
        Assert.Contains("vat_conditions_additional_code_or_footnote_treatment_requires_review",result.Lines[0].Checks);
    }

    [Fact]
    public void Currency_requires_its_own_dated_invoice_feed_and_converts_costs_together()
    {
        var request = Request() with { Currency = "USD", Costs = [new("shipping", 100m, false)] };
        var rate = new UkFxRate("USD", "invoice_customs_value", .8m, Date, Date, "https://www.trade-tariff.service.gov.uk/uk/api/v2/exchange_rates/2026-11", new string('b',64));
        var result = UkShipmentReview.Compose(request, Sources(Assessment()), [rate]);
        Assert.Equal(new UkIllustrativeTotals(800m, 80m, 80m, 960m, 192m, 1152m), result.IllustrativeTotals);
        foreach (var wrong in new[] { rate with { Purpose = "tariff_duty" }, rate with { ValidTo = Date.AddDays(-1) } })
            Assert.Null(UkShipmentReview.Compose(request, Sources(Assessment()), [wrong]).IllustrativeTotals);
        var missing = UkShipmentReview.Compose(request, Sources(Assessment()), []);
        Assert.Null(missing.Lines[0].DeclaredCustomsValueGbp);
        Assert.Null(missing.Lines[0].DutyScenarios[0].Arithmetic.IllustrativeGbp);
    }

    [Fact]
    public void Hash_is_order_independent_but_binds_sources_costs_and_territory()
    {
        var request = Request() with { Lines = [Request().Lines![0], Request().Lines![0] with { LineId = "b" }],
            Costs = [new("c", 5m, false), new("d", 2m, true)] };
        var first = Run(request);
        Assert.Equal(first.EvidenceSha256, Run(request with { Lines = request.Lines.Reverse().ToArray(), Costs = request.Costs.Reverse().ToArray() }).EvidenceSha256);
        Assert.NotEqual(first.EvidenceSha256, Run(request, Assessment() with { ResponseSha256 = new string('b',64) }).EvidenceSha256);
        Assert.NotEqual(first.EvidenceSha256, Run(request with { Costs = [] }).EvidenceSha256);
        Assert.NotEqual(first.EvidenceSha256, Run(request with { DestinationTerritory = "XI" }, Assessment(territory:"XI")).EvidenceSha256);
    }

    [Fact]
    public void Incomplete_or_malicious_inputs_are_invalid_not_guessed()
    {
        var r = Request();
        UkShipmentReviewRequest[] invalid = [r with { Lines = null }, r with { Lines = [null!] }, r with { Costs = null },
            r with { Costs = [null!] }, r with { Lines = [r.Lines![0], r.Lines[0]] },
            r with { Lines = [r.Lines![0] with { GoodsOrigin = "us" }] }, r with { Lines = [r.Lines![0] with { CustomsValue = null }] },
            r with { Lines = [r.Lines![0] with { ItemCount = 1.5m }] }, r with { Lines = [r.Lines![0] with { DeclaredDocuments = ["../x"] }] },
            r with { Costs = [new("c", 10m, false, ["unknown"])] }, r with { Costs = [new("c", -1m, false)] },
            r with { DestinationTerritory = "UK" }, r with { ShipFrom = "GB" }, r with { ImportDate = null },
            r with { ImportDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(21) }, r with { NorthernIrelandRisk = "approved" }];
        foreach (var request in invalid)
        {
            var result = Run(request);
            Assert.Equal("invalid_input", result.Status);
            Assert.Null(result.IllustrativeTotals);
            Assert.NotEmpty(result.Checks);
            Assert.Matches("^[a-f0-9]{64}$", result.EvidenceSha256);
        }
    }
}
