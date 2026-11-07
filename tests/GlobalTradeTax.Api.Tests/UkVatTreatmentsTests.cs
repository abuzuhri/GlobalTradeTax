using GlobalTradeTax.Api;
using Xunit;
using static UkCompletionTestData;

public sealed class UkVatTreatmentsTests
{
    private static UkShipmentReviewRequest ArtRequest() => Request() with { Lines = [
        Request().Lines![0] with { CommodityCode="9701100000",VatTreatment=new("reduced_valuation_art",EvidenceReference:"review-file-1") }] };
    private static UkShipmentReviewResult Run(UkShipmentReviewRequest request, params UkFxRate[] fx) =>
        UkShipmentReview.Compose(request,Sources(request.Lines!.Select(l=>Assessment(l.CommodityCode!,l.GoodsOrigin!,request.DestinationTerritory!)).ToArray()),fx);

    [Fact]
    public void Reduced_valuation_is_a_separate_conditional_scenario_without_shipment_totals()
    {
        var request = ArtRequest() with { Costs=[new("onward",30m,false)] };
        var result = Run(request); var line = Assert.Single(result.Lines);
        var decision = Assert.IsType<UkVatTreatmentDecision>(line.VatTreatmentReview);
        var scenario = Assert.IsType<UkConditionalVatScenario>(decision.ConditionalScenario);
        Assert.Equal(1130m,scenario.FullBaseGbp); Assert.Equal(.25m,scenario.ValuationFraction);
        Assert.Equal(282.5m,scenario.ConditionalBaseGbp); Assert.Equal(20m,scenario.AssumedVatRatePercent);
        Assert.Equal(56.5m,scenario.ConditionalVatGbp);
        Assert.Null(line.IllustrativeVatBaseGbp); Assert.Null(line.IllustrativeVatGbp); Assert.Null(result.IllustrativeTotals);
        Assert.Null(result.PayableVatGbp); Assert.False(scenario.ApprovedForPayment); Assert.False(decision.VerifiedForCalculation);
        Assert.Equal("review-file-1",decision.DeclaredEvidenceReference);
        Assert.Contains(UkVatTreatments.ValuationSource,decision.SourceUrls);
    }

    [Theory]
    [InlineData(5)] [InlineData(0)] [InlineData(10)]
    public void Reduced_valuation_does_not_stack_with_another_proposed_reduced_rate(decimal rate)
    {
        var result = UkVatTreatments.Assess(new("reduced_valuation_art"),"GB","free_circulation",rate,1100m,commodityCode:"9701100000");
        Assert.Null(result.ConditionalScenario); Assert.True(result.SuppressOrdinaryIllustration);
        Assert.Contains("reduced_valuation_not_combined_with_reduced_or_unknown_rate",result.RequiredChecks);
    }

    [Theory]
    [InlineData("0101299000")] [InlineData("8471300000")] [InlineData(null)]
    public void An_art_claim_does_not_turn_unlisted_classification_into_reduced_valuation(string? code)
    {
        var decision=UkVatTreatments.Assess(new("reduced_valuation_art"),"GB","free_circulation",20m,1100m,commodityCode:code);
        Assert.Null(decision.ConditionalScenario);
        Assert.Contains("commodity_heading_not_in_current_reduced_valuation_guidance",decision.RequiredChecks);
    }

    [Theory]
    [InlineData("returned_goods_relief")]
    [InlineData("temporary_admission")]
    [InlineData("vat_exemption")]
    [InlineData("other_relief")]
    [InlineData("unknown")]
    public void Claimed_relief_or_exemption_is_not_a_zero_VAT_rate(string treatment)
    {
        var request=Request(); request=request with { Lines=[request.Lines![0] with { VatTreatment=new(treatment) }] };
        var result=Run(request);var line=result.Lines[0];
        Assert.Null(line.IllustrativeVatGbp);Assert.Null(result.IllustrativeTotals);Assert.Null(result.PayableVatGbp);
        Assert.True(line.VatTreatmentReview!.SuppressOrdinaryIllustration);
        Assert.Null(line.VatTreatmentReview.ConditionalScenario);
        Assert.False(line.VatTreatmentReview.ApprovedForPayment);
    }

    [Fact]
    public void Postponed_accounting_does_not_zero_the_illustrative_liability()
    {
        var r=Request();r=r with { Lines=[r.Lines![0] with { VatTreatment=new("ordinary","postponed_accounting") }] };
        var result=Run(r);var line=result.Lines[0];
        Assert.Equal(220m,line.IllustrativeVatGbp);Assert.NotNull(result.IllustrativeTotals);
        Assert.Null(result.PayableVatGbp);
        Assert.Contains("postponed_accounting_does_not_zero_vat_liability",line.Checks);
        Assert.Contains(UkVatTreatments.PostponedSource,line.VatTreatmentReview!.SourceUrls);
    }

    [Fact]
    public void Seller_collection_requires_its_own_consignment_and_supply_review()
    {
        var r=Request();r=r with { Lines=[r.Lines![0] with { VatTreatment=new("ordinary","seller_collected") }] };
        var result=Run(r);Assert.Null(result.IllustrativeTotals);Assert.Null(result.Lines[0].IllustrativeVatGbp);
        Assert.Contains("seller_collection_consignment_and_supply_regime_require_separate_review",result.Lines[0].Checks);
    }

    [Theory]
    [InlineData("GB","at_risk")]
    [InlineData("GB","not_at_risk")]
    [InlineData("US","not_at_risk")]
    [InlineData("FR","unknown")]
    public void NI_risk_or_dispatch_does_not_establish_VAT_scope(string dispatch, string risk)
    {
        var r=ArtRequest() with { DestinationTerritory="XI",ShipFrom=dispatch,NorthernIrelandRisk=risk };
        var result=Run(r);var decision=result.Lines[0].VatTreatmentReview!;
        Assert.True(decision.SuppressOrdinaryIllustration);Assert.Null(decision.ConditionalScenario);
        Assert.Null(result.IllustrativeTotals);Assert.Null(result.PayableVatGbp);
        Assert.Contains("ni_duty_risk_declaration_does_not_determine_vat_treatment",decision.RequiredChecks);
        Assert.Contains(dispatch=="GB" ? "gb_to_ni_vat_registration_and_movement_facts_required" :
            "ni_dispatch_customs_status_and_import_or_acquisition_scope_required",decision.RequiredChecks);
    }

    [Fact]
    public void One_exception_line_prevents_partial_VAT_from_becoming_a_shipment_total()
    {
        var r=Request();r=r with { Lines=[r.Lines![0],r.Lines[0] with { LineId="b",CommodityCode="9701100000",VatTreatment=new("reduced_valuation_art") }] };
        var result=Run(r);
        Assert.Equal(220m,result.Lines[0].IllustrativeVatGbp);
        Assert.Null(result.Lines[1].IllustrativeVatGbp);
        Assert.Equal(55m,result.Lines[1].VatTreatmentReview!.ConditionalScenario!.ConditionalVatGbp);
        Assert.Null(result.IllustrativeTotals);
    }

    [Fact]
    public void Treatment_accounting_and_reference_are_bound_to_version_2_evidence()
    {
        var plain=Run(Request());var r=Request();r=r with { Lines=[r.Lines![0] with { VatTreatment=new("ordinary",EvidenceReference:"a") }] };
        var first=Run(r);Assert.Equal("uk-shipment-review/v2",first.SchemaVersion);
        Assert.NotEqual(plain.EvidenceSha256,first.EvidenceSha256);
        Assert.NotEqual(first.EvidenceSha256,Run(r with { Lines=[r.Lines[0] with { VatTreatment=new("ordinary",EvidenceReference:"b") }] }).EvidenceSha256);
        Assert.NotEqual(first.EvidenceSha256,Run(r with { Lines=[r.Lines[0] with { VatTreatment=new("ordinary","postponed_accounting","a") }] }).EvidenceSha256);
    }

    [Fact]
    public void Invoice_FX_underflow_is_unknown_and_never_an_explicit_zero_customs_value()
    {
        const decimal tiny=0.0000000000000000000000000001m;
        var r=Request();r=r with { Currency="USD",Lines=[r.Lines![0] with { CustomsValue=tiny }] };
        var result=Run(r,new UkFxRate("USD","invoice_customs_value",.1m,Date,Date,"https://example.test/fx",null));
        Assert.Contains("invoice_currency_conversion_precision_lost",result.Checks);
        Assert.Null(result.Lines[0].DeclaredCustomsValueGbp);Assert.Null(result.IllustrativeTotals);
    }

    [Fact]
    public void Cost_FX_underflow_is_distinct_from_the_documented_penny_allocation_policy()
    {
        const decimal tiny=0.0000000000000000000000000001m;
        var r=Request() with { Currency="USD",Costs=[new("c",tiny,false)] };
        var result=Run(r,new UkFxRate("USD","invoice_customs_value",.1m,Date,Date,"https://example.test/fx",null));
        Assert.Contains("cost_conversion_precision_lost:c",result.Checks);
        Assert.Null(result.Lines[0].AdditionalVatCostsGbp);Assert.Null(result.IllustrativeTotals);
    }

    [Fact]
    public void Invalid_claims_are_input_errors()
    {
        UkVatTreatmentClaim[] claims=[new(null),new("approved"),new("ordinary","waived"),new("ordinary",EvidenceReference:" "),
            new("ordinary",EvidenceReference:"line\nbreak"),new("ordinary",EvidenceReference:new string('x',257))];
        foreach(var claim in claims)
        {
            var r=Request();r=r with { Lines=[r.Lines![0] with { VatTreatment=claim }] };
            var result=Run(r);Assert.Equal("invalid_input",result.Status);Assert.Null(result.IllustrativeTotals);
        }
    }

    [Fact]
    public void Conditional_precision_loss_is_unavailable()
    {
        var decision=UkVatTreatments.Assess(new("reduced_valuation_art"),"GB","free_circulation",20m,
            0.0000000000000000000000000001m,commodityCode:"9701100000");
        Assert.Null(decision.ConditionalScenario);
        Assert.Contains("conditional_vat_arithmetic_precision_lost",decision.RequiredChecks);
    }

    [Fact]
    public void NI_to_GB_does_not_use_an_ordinary_import_VAT_scenario()
    {
        var r=ArtRequest() with { ShipFrom="XI" };var result=Run(r);var decision=result.Lines[0].VatTreatmentReview!;
        Assert.True(decision.SuppressOrdinaryIllustration);Assert.Null(decision.ConditionalScenario);
        Assert.Contains("intra_uk_vat_movement_scope_requires_separate_review",decision.RequiredChecks);
        Assert.Null(result.IllustrativeTotals);
    }

    [Fact]
    public void Conditional_overflow_stays_unavailable()
    {
        var result=UkVatTreatments.Assess(new("reduced_valuation_art"),"GB","free_circulation",20m,decimal.MaxValue,commodityCode:"9701100000");
        Assert.Null(result.ConditionalScenario);Assert.Contains("conditional_vat_arithmetic_overflow",result.RequiredChecks);
    }

    [Fact]
    public void Shipment_VAT_precision_loss_is_not_a_zero_liability()
    {
        var r=Request();r=r with { Lines=[r.Lines![0] with { CustomsValue=0.0000000000000000000000000001m }] };
        var result=UkShipmentReview.Compose(r,Sources(Assessment(duty:0m,vat:1m)),[]);
        Assert.Null(result.Lines[0].IllustrativeVatGbp);Assert.Null(result.IllustrativeTotals);
        Assert.Contains("vat_arithmetic_precision_lost",result.Lines[0].Checks);
    }
}
