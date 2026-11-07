using GlobalTradeTax.Api;
using Xunit;

namespace GlobalTradeTax.Api.Tests;

public class UkVatQuoteReviewTests
{
    private static UkVatQuoteReviewRequest Sample() => new(
        new UkVatClassificationInput("GB", "free_circulation", "0101210000", "US",
            new DateOnly(2026, 11, 2), 20m, "https://www.gov.uk/", new DateOnly(2026, 1, 1),
            null, true, true, true, true, true),
        new UkVatInput(1000m, 100m, 30m, 10m, 0m, 20m, true, true, true, true, true),
        "GBP", "shipment-1");

    [Fact]
    public void MatchingProposedInputsStillNeverProducePayableAmounts()
    {
        var result = UkVatQuoteReview.Evaluate(Sample());
        Assert.Equal("requires_review", result.Status);
        Assert.Equal(1140m, result.Amounts.IllustrativeTaxableBaseGbp);
        Assert.Null(result.PayableVatGbp);
        Assert.Null(result.PayableLandedTotalGbp);
        Assert.False(result.VerifiedForCalculation);
    }

    [Fact]
    public void MismatchedRatesAreFlagged()
    {
        var result = UkVatQuoteReview.Evaluate(Sample() with
        { Amounts = Sample().Amounts with { VatRatePercent = 5m } });
        Assert.Contains("vat_rate_mismatch_between_classification_and_amounts", result.UnresolvedChecks);
        Assert.Null(result.Amounts.IllustrativeVatGbp);
        Assert.Null(result.PayableVatGbp);
    }

    [Fact]
    public void NonGbpAmountsAreNotSilentlyConverted()
    {
        var result = UkVatQuoteReview.Evaluate(Sample() with { AmountCurrency = "USD" });
        Assert.Contains("amount_currency_must_be_GBP_no_fx_engine", result.UnresolvedChecks);
        Assert.Null(result.Amounts.IllustrativeTaxableBaseGbp);
        Assert.Null(result.Amounts.IllustrativeVatGbp);
    }

    [Fact]
    public void NorthernIrelandIsNotTreatedAsGreatBritain()
    {
        var request = Sample();
        var result = UkVatQuoteReview.Evaluate(request with
        { Classification = request.Classification with { DestinationTerritory = "XI" } });
        Assert.Contains("only_great_britain_review_supported", result.UnresolvedChecks);
        Assert.Null(result.Amounts.IllustrativeVatGbp);
    }

    [Fact]
    public void NegativeAmountsAreRejected()
    {
        var request = Sample();
        var result = UkVatQuoteReview.Evaluate(request with
        { Amounts = request.Amounts with { ImportDutyGbp = -1m } });
        Assert.Equal("invalid_input", result.Status);
        Assert.Null(result.PayableVatGbp);
        Assert.Null(result.Amounts.IllustrativeVatGbp);
    }

    [Fact]
    public void ExpiredRateSuppressesIllustrativeAmounts()
    {
        var request = Sample();
        var result = UkVatQuoteReview.Evaluate(request with
        { Classification = request.Classification with { RateEffectiveTo = new DateOnly(2025, 12, 31) } });
        Assert.Contains("rate_expired", result.UnresolvedChecks);
        Assert.Null(result.Amounts.IllustrativeVatGbp);
        Assert.Null(result.PayableVatGbp);
    }

    [Fact]
    public void MissingCommodityCodeSuppressesIllustrativeAmounts()
    {
        var request = Sample();
        var result = UkVatQuoteReview.Evaluate(request with
        { Classification = request.Classification with { CommodityCode = null } });
        Assert.Contains("missing_or_invalid_commodity_code", result.UnresolvedChecks);
        Assert.Null(result.Amounts.IllustrativeTaxableBaseGbp);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("classification")]
    [InlineData("procedure")]
    [InlineData("treatment")]
    [InlineData("relief")]
    [InlineData("unsafe_url")]
    public void Any_unresolved_classification_evidence_suppresses_amounts(string gap)
    {
        var request = Sample(); var input = request.Classification;
        input = gap switch {
            "source" => input with { SourceContractVerified = false },
            "classification" => input with { CommodityClassificationVerified = false },
            "procedure" => input with { ImportProcedureVerified = false },
            "treatment" => input with { VatTreatmentVerified = false },
            "relief" => input with { ReliefsAndExemptionsVerified = false },
            _ => input with { RateSourceUrl = "https://user:secret@example.test/" } };
        var result = UkVatQuoteReview.Evaluate(request with { Classification = input });
        Assert.Null(result.Amounts.IllustrativeTaxableBaseGbp);
        Assert.Null(result.Amounts.IllustrativeVatGbp);
        Assert.Null(result.PayableVatGbp);
    }

    [Theory]
    [InlineData("customs_warehousing")]
    [InlineData("temporary_admission")]
    [InlineData("inward_processing")]
    public void Self_asserted_verification_flags_do_not_clear_special_procedures(string procedure)
    {
        var r=Sample();var result=UkVatQuoteReview.Evaluate(r with { Classification=r.Classification with { ImportProcedure=procedure } });
        Assert.Null(result.Amounts.IllustrativeVatGbp);
        Assert.Contains("special_vat_import_procedure_requires_review",result.UnresolvedChecks);
    }

    [Fact]
    public void Art_claim_is_conditional_and_never_replaces_the_ordinary_quote()
    {
        var r=Sample();var result=UkVatQuoteReview.Evaluate(r with { Classification=r.Classification with {
            CommodityCode="9701100000",VatTreatment=new("reduced_valuation_art") } });
        Assert.Null(result.Amounts.IllustrativeVatGbp);Assert.Null(result.PayableVatGbp);
        Assert.Equal(57m,result.VatTreatmentReview!.ConditionalScenario!.ConditionalVatGbp);
        Assert.False(result.VatTreatmentReview.ApprovedForPayment);
    }

    [Theory]
    [InlineData("returned_goods_relief")]
    [InlineData("temporary_admission")]
    [InlineData("vat_exemption")]
    public void Claimed_relief_does_not_bypass_flags_or_become_zero(string treatment)
    {
        var r=Sample();var result=UkVatQuoteReview.Evaluate(r with { Classification=r.Classification with { VatTreatment=new(treatment) } });
        Assert.Null(result.Amounts.IllustrativeTaxableBaseGbp);Assert.Null(result.Amounts.IllustrativeVatGbp);
        Assert.Null(result.PayableVatGbp);Assert.Null(result.VatTreatmentReview!.ConditionalScenario);
    }

    [Fact]
    public void Postponed_accounting_retains_the_illustrative_liability()
    {
        var r=Sample();var result=UkVatQuoteReview.Evaluate(r with { Classification=r.Classification with { VatTreatment=new("ordinary","postponed_accounting") } });
        Assert.Equal(228m,result.Amounts.IllustrativeVatGbp);Assert.Null(result.PayableVatGbp);
        Assert.Contains("postponed_accounting_does_not_zero_vat_liability",result.UnresolvedChecks);
    }

    [Fact]
    public void Missing_evidence_blocks_even_a_conditional_art_scenario()
    {
        var r=Sample();var result=UkVatQuoteReview.Evaluate(r with { Classification=r.Classification with {
            CommodityCode="9701100000",VatTreatment=new("reduced_valuation_art"),SourceContractVerified=false } });
        Assert.Null(result.VatTreatmentReview!.ConditionalScenario);Assert.Null(result.Amounts.IllustrativeVatGbp);
    }

    [Theory]
    [InlineData("GB")]
    [InlineData("XI")]
    public void Known_intra_UK_dispatch_suppresses_legacy_VAT_despite_verification_flags(string shipFrom)
    {
        var r=Sample();var result=UkVatQuoteReview.Evaluate(r with { Classification=r.Classification with { ShipFrom=shipFrom } });
        Assert.Null(result.Amounts.IllustrativeVatGbp);Assert.Null(result.PayableVatGbp);
        Assert.Contains("intra_uk_vat_movement_requires_review",result.UnresolvedChecks);
    }
}
