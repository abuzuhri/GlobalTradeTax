using GlobalTradeTax.Api;
using Xunit;

public class UkAssessmentEvidencePackageTests
{
    [Fact]
    public void Hash_is_stable_for_same_assessment_regardless_of_capture_time()
    {
        var report = new UkAssessmentReport("requires_review", "0101210000", "US",
            new DateOnly(2026, 11, 2), [], [], "https://www.trade-tariff.service.gov.uk/uk/api/v2/commodities/0101210000",
            null, null, ["no_final_duty_without_official_rule_validation"], false);
        var a = UkAssessmentEvidencePackage.Build(report, DateTimeOffset.UtcNow);
        var b = UkAssessmentEvidencePackage.Build(report, DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(a.ContentHash, b.ContentHash);
        Assert.Equal(64, a.ContentHash.Length);
        Assert.False(a.ApprovedForPayment);
        Assert.Null(a.Assessment.ImportDuty);
    }

    [Fact]
    public void Hash_changes_when_warning_changes()
    {
        UkAssessmentReport Make(string warning) => new("requires_review", "0101210000", "US",
            new DateOnly(2026, 11, 2), [], [], null, null, null, [warning], false);
        Assert.NotEqual(UkAssessmentEvidencePackage.Build(Make("A"), DateTimeOffset.UtcNow).ContentHash,
            UkAssessmentEvidencePackage.Build(Make("B"), DateTimeOffset.UtcNow).ContentHash);
    }
}
