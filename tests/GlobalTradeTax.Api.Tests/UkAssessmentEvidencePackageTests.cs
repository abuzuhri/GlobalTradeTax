using GlobalTradeTax.Api;
using System.Globalization;
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

    [Fact]
    public void Version_3_hash_covers_classification_date_and_membership_path()
    {
        var applicability = new UkMeasureApplicabilityEvidence("candidate_for_review", "import", "within_window",
            "candidate_group_membership", "empty_exclusion_list", ["legal_review_required"], false);
        var measure = new UkMeasureDecision("m1", "requires_review", null, ["1011"], []) { Applicability = applicability };
        var area = new UkAreaEvidence("1011", "reference_only", "candidate_membership_evidence", null, false)
            { AsOfDate = new DateOnly(2026, 11, 5), MembershipPath = ["1011", "US"] };
        var report = new UkAssessmentReport("requires_review", "0101210000", "US", new DateOnly(2026, 11, 5),
            [measure], [area], null, null, null, [], false);
        var original = UkAssessmentEvidencePackage.Build(report, DateTimeOffset.UtcNow);
        Assert.Equal("uk-reference-evidence/v3", original.SchemaVersion);
        Assert.NotEqual(original.ContentHash, UkAssessmentEvidencePackage.Build(report with
            { Measures = [measure with { Applicability = applicability with { Status = "indeterminate" } }] }, DateTimeOffset.UtcNow).ContentHash);
        Assert.NotEqual(original.ContentHash, UkAssessmentEvidencePackage.Build(report with
            { Areas = [area with { MembershipPath = ["1011", "1000", "US"] }] }, DateTimeOffset.UtcNow).ContentHash);
        Assert.NotEqual(original.ContentHash, UkAssessmentEvidencePackage.Build(report with
            { Areas = [area with { AsOfDate = new DateOnly(2026, 11, 4) }] }, DateTimeOffset.UtcNow).ContentHash);
        Assert.False(original.ApprovedForPayment);
    }

    [Fact]
    public void Evidence_hash_is_independent_of_host_calendar()
    {
        var report = new UkAssessmentReport("requires_review", "0101210000", "US", new DateOnly(2026, 11, 5),
            [], [], null, null, null, [], false);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var first = UkAssessmentEvidencePackage.Build(report, DateTimeOffset.UtcNow).ContentHash;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(first, UkAssessmentEvidencePackage.Build(report, DateTimeOffset.UtcNow).ContentHash);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Version_3_hash_binds_territory_response_bytes_and_structured_rules()
    {
        var report = UkCompletionTestData.Assessment();
        string Hash(UkAssessmentReport r) => UkAssessmentEvidencePackage.Build(r,DateTimeOffset.UtcNow).ContentHash;
        var original = Hash(report);
        Assert.NotEqual(original,Hash(report with { DestinationTerritory = "XI" }));
        Assert.NotEqual(original,Hash(report with { ResponseSha256 = new string('b',64) }));
        var changed = report.Measures[0] with { Rule = report.Measures[0].Rule! with { Components = [UkCompletionTestData.Percent(9)] } };
        Assert.NotEqual(original,Hash(report with { Measures = [changed,report.Measures[1]] }));
    }
}
