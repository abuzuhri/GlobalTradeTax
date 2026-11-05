using System.Text.Json.Nodes;
using GlobalTradeTax.Api;
using Xunit;
using static UkApplicabilityTestData;

public sealed class UkMeasureApplicabilityTests
{
    private static UkMeasureApplicabilityEvidence Analyze(JsonObject root, string origin = "US", DateOnly? date = null,
        IReadOnlyDictionary<string, UkAreaMembershipReport>? areas = null) =>
        UkMeasureApplicability.Analyze(Dated(root, date), Code, "m1", origin, date ?? Date,
            areas ?? new Dictionary<string, UkAreaMembershipReport>());

    [Fact]
    public void Complete_direct_import_reference_is_only_a_candidate_for_review()
    {
        var result = Analyze(Commodity(Measure()));
        Assert.Equal("candidate_for_review", result.Status);
        Assert.Equal("import", result.Direction);
        Assert.Equal("within_window", result.EffectiveWindow);
        Assert.Equal("empty_exclusion_list", result.ExclusionEvidence);
        Assert.False(result.VerifiedForCalculation);
        Assert.Contains("conditions_certificates_quotas_preferences_and_legal_eligibility_require_review", result.EvidenceChecks);
    }

    [Theory]
    [InlineData("2026-11-04", "not_candidate")]
    [InlineData("2026-11-05", "candidate_for_review")]
    [InlineData("2026-11-10", "candidate_for_review")]
    [InlineData("2026-11-11", "not_candidate")]
    public void Effective_dates_include_both_boundaries(string date, string expected)
    {
        var measure = Measure();
        measure["attributes"]!["effective_start_date"] = "2026-11-05T00:00:00.000Z";
        measure["attributes"]!["effective_end_date"] = "2026-11-10T00:00:00.000Z";
        Assert.Equal(expected, Analyze(Commodity(measure), date: DateOnly.ParseExact(date, "yyyy-MM-dd")).Status);
    }

    [Theory]
    [InlineData("2026-11-05")]
    [InlineData("2026-11-05T00:00:00Z")]
    [InlineData("2026-11-05T00:00:00.000Z")]
    public void Official_date_and_UTC_midnight_forms_are_supported(string value)
    {
        var measure = Measure();
        measure["attributes"]!["effective_start_date"] = value;
        Assert.Equal("candidate_for_review", Analyze(Commodity(measure)).Status);
    }

    [Theory]
    [InlineData("11/05/2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-11-05T12:00:00Z")]
    [InlineData("2026-11-05T23:59:59.000Z")]
    [InlineData("2026-11-05T00:00:00+03:00")]
    [InlineData("")]
    public void Ambiguous_or_invalid_dates_stay_indeterminate(string value)
    {
        var measure = Measure();
        measure["attributes"]!["effective_start_date"] = value;
        var result = Analyze(Commodity(measure));
        Assert.Equal("indeterminate", result.Status);
        Assert.Equal("unknown", result.EffectiveWindow);
    }

    [Theory]
    [InlineData("missing-start")]
    [InlineData("missing-end")]
    [InlineData("numeric-end")]
    [InlineData("inverted-window")]
    public void Incomplete_or_reversed_windows_are_unknown(string corruption)
    {
        var measure = Measure();
        var attributes = measure["attributes"]!.AsObject();
        switch (corruption)
        {
            case "missing-start": attributes.Remove("effective_start_date"); break;
            case "missing-end": attributes.Remove("effective_end_date"); break;
            case "numeric-end": attributes["effective_end_date"] = 20260101; break;
            case "inverted-window": attributes["effective_end_date"] = "2020-12-31"; break;
        }
        Assert.Equal("indeterminate", Analyze(Commodity(measure)).Status);
    }

    [Theory]
    [InlineData("2026-11-05T23:59:59.000Z", "candidate_for_review")]
    [InlineData("2026-11-04T23:59:59.000Z", "not_candidate")]
    [InlineData("2026-11-05T12:00:00Z", "indeterminate")]
    [InlineData("2026-11-05T00:00:00+03:00", "indeterminate")]
    public void Official_end_of_day_timestamps_keep_the_inclusive_date(string end, string expected)
    {
        var measure = Measure();
        measure["attributes"]!["effective_end_date"] = end;
        Assert.Equal(expected, Analyze(Commodity(measure)).Status);
    }

    [Theory]
    [InlineData("excluded_countries")]
    [InlineData("excluded_geographical_areas")]
    public void Explicit_origin_exclusion_in_either_official_serializer_is_preserved(string relation)
    {
        var measure = Measure();
        measure["relationships"]!.AsObject().Remove("excluded_countries");
        measure["relationships"]![relation] = Many("geographical_area", "US");
        var result = Analyze(Commodity(measure));
        Assert.Equal("not_candidate", result.Status);
        Assert.Equal("origin_listed", result.ExclusionEvidence);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("wrong-type")]
    [InlineData("wrong-shape")]
    [InlineData("other-country-or-group")]
    [InlineData("malformed-alternate")]
    public void Missing_or_unresolved_exclusions_are_not_an_empty_list(string corruption)
    {
        var measure = Measure();
        var relationships = measure["relationships"]!.AsObject();
        switch (corruption)
        {
            case "missing": relationships.Remove("excluded_countries"); break;
            case "null": relationships["excluded_countries"]!["data"] = null; break;
            case "wrong-type": relationships["excluded_countries"] = Many("measure", "US"); break;
            case "wrong-shape": relationships["excluded_countries"]!["data"] = Ref("geographical_area", "US"); break;
            case "other-country-or-group": relationships["excluded_countries"] = Many("geographical_area", "EU"); break;
            case "malformed-alternate": relationships["excluded_geographical_areas"] = new JsonObject { ["data"] = null }; break;
        }
        Assert.Equal("indeterminate", Analyze(Commodity(measure)).Status);
    }

    [Fact]
    public void Exclusion_group_membership_blocks_a_candidate()
    {
        var measure = Measure();
        measure["relationships"]!["excluded_countries"] = Many("geographical_area", "1000");
        var membership = UkAreaMembership.Inspect(Dated(AreaDocument("1000", "US")), "1000", "US", Date);
        var result = Analyze(Commodity(measure), areas: new Dictionary<string, UkAreaMembershipReport> { ["1000"] = membership });
        Assert.Equal("not_candidate", result.Status);
        Assert.Equal("origin_listed", result.ExclusionEvidence);
    }

    [Fact]
    public void Group_membership_needs_the_same_area_origin_and_import_date()
    {
        var membership = UkAreaMembership.Inspect(Dated(AreaDocument("1011", "US")), "1011", "US", Date);
        var areas = new Dictionary<string, UkAreaMembershipReport> { ["1011"] = membership };
        Assert.Equal("candidate_for_review", Analyze(Commodity(Measure(area: "1011")), areas: areas).Status);
        areas["1011"] = membership with { ImportDate = Date.AddDays(-1) };
        Assert.Equal("indeterminate", Analyze(Commodity(Measure(area: "1011")), areas: areas).Status);
        areas["1011"] = membership with { Origin = "DE" };
        Assert.Equal("indeterminate", Analyze(Commodity(Measure(area: "1011")), areas: areas).Status);
    }

    [Fact]
    public void Another_country_is_unknown_without_explicit_membership_proof()
    {
        Assert.Equal("indeterminate", Analyze(Commodity(Measure(area: "GB"))).Status);
    }

    [Theory]
    [InlineData("missing-linkage")]
    [InlineData("wrong-link-type")]
    [InlineData("missing-import-flag")]
    [InlineData("export-flag-conflict")]
    [InlineData("import-flag-conflict")]
    [InlineData("duplicate-measure")]
    [InlineData("wrong-area-type")]
    public void Incomplete_or_conflicting_measure_contracts_stay_unknown(string corruption)
    {
        var root = Commodity(Measure());
        var measure = root["included"]![0]!;
        switch (corruption)
        {
            case "missing-linkage": root["data"]!["relationships"]!.AsObject().Remove("import_measures"); break;
            case "wrong-link-type": root["data"]!["relationships"]!["import_measures"] = Many("geographical_area", "m1"); break;
            case "missing-import-flag": measure["attributes"]!.AsObject().Remove("import"); break;
            case "export-flag-conflict": measure["attributes"]!["export"] = true; break;
            case "import-flag-conflict": measure["attributes"]!["import"] = false; break;
            case "duplicate-measure": root["included"]!.AsArray().Add(measure.DeepClone()); break;
            case "wrong-area-type": measure["relationships"]!["geographical_area"]!["data"]!["type"] = "measure"; break;
        }
        Assert.Equal("indeterminate", Analyze(root).Status);
    }

    [Fact]
    public void Export_or_unlinked_included_measures_are_not_import_candidates()
    {
        var root = Commodity(Measure());
        root["data"]!["relationships"]!["import_measures"] = Many("measure");
        Assert.Equal("not_candidate", Analyze(root).Status);
    }

    [Fact]
    public void A_measure_may_be_explicitly_linked_to_both_import_and_export()
    {
        var root = Commodity(Measure());
        root["included"]![0]!["attributes"]!["export"] = true;
        root["data"]!["relationships"]!["export_measures"] = Many("measure", "m1");
        var result = Analyze(root);
        Assert.Equal("candidate_for_review", result.Status);
        Assert.Equal("import_and_export", result.Direction);
        Assert.False(result.VerifiedForCalculation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undated_or_differently_dated_commodity_is_not_evidence(bool otherDate)
    {
        var source = Dated(Commodity(Measure())) with { AsOfDate = otherDate ? Date.AddDays(-1) : null };
        Assert.Equal("indeterminate", UkMeasureApplicability.Analyze(source, Code, "m1", "US", Date,
            new Dictionary<string, UkAreaMembershipReport>()).Status);
    }
}
