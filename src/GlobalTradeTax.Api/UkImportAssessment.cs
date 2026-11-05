using System.Text.Json;

namespace GlobalTradeTax.Api;

// An orchestration layer over the UK reference APIs, not a customs determination.
// Explicitly fail-closed for quotas, VAT, trade remedies, licences and unknown measures.
public sealed class UkImportAssessment(UkTariffReferenceClient client)
{
    public Task<UkAssessmentReport> AssessAsync(string code, string origin, DateOnly date, CancellationToken ct) =>
        AssessAsync(code, origin, date, "GB", ct);

    public async Task<UkAssessmentReport> AssessAsync(string code, string origin, DateOnly date, string territory, CancellationToken ct)
    {
        if (origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z'))
            return new("invalid_origin", code, origin, date, [], [], null, null, null,
                ["origin_must_be_uppercase_iso_alpha2"], false) { DestinationTerritory = territory };
        var upstream = await client.GetCommodityAsync(code, date, territory, ct);
        if (upstream.Status != "reference_only")
            return new(upstream.Status, code, origin, date, [], [], upstream.SourceUrl, null, null,
                [upstream.Warning], false) { DestinationTerritory = territory };
        var evidence = UkMeasureEvidence.Analyze(upstream, code, origin, date);
        var rules = UkMeasureRules.Read(upstream, code);
        var included = upstream.Data!.Value.TryGetProperty("included", out var includedValue) && includedValue.ValueKind == JsonValueKind.Array
            ? includedValue.EnumerateArray().ToArray() : [];
        var exclusions = included
            .Where(item => UkEvidenceJson.String(item, "type") == "measure")
            .SelectMany(item => UkMeasureApplicability.ReadExclusions(item, out _));
        var lookupPlan = UkAreaLookupPlanner.Create(evidence.Measures.SelectMany(m => m.GeographicalAreaReferences).Concat(exclusions), origin);
        var areaIds = lookupPlan.AreaIds;
        // Bounded upstream work; cache is implemented in UkTariffReferenceClient.
        using var limiter = new SemaphoreSlim(4);
        var lookups = await Task.WhenAll(areaIds.Select(async id =>
        {
            await limiter.WaitAsync(ct);
            try
            {
                var response = await client.GetGeographicalAreaAsync(id, date, territory, ct);
                var membership = UkAreaMembership.Inspect(response, id, origin, date);
                return (Area: new UkAreaEvidence(id, response.Status, membership.EvidenceStatus, response.SourceUrl,
                    membership.VerifiedForCalculation) { MembershipPath = membership.MembershipPath,
                        UnresolvedChecks = membership.UnresolvedChecks, AsOfDate = response.AsOfDate,
                        DestinationTerritory = territory, ResponseSha256 = response.ResponseSha256 }, Membership: membership);
            }
            finally { limiter.Release(); }
        }));
        var areas = lookups.ToDictionary(x => x.Area.AreaId, x => x.Area, StringComparer.Ordinal);
        var memberships = lookups.ToDictionary(x => x.Area.AreaId, x => x.Membership, StringComparer.Ordinal);
        var decisions = evidence.Measures.Select(m =>
        {
            var checks = new HashSet<string>(m.UnresolvedChecks, StringComparer.Ordinal);
            var applicability = UkMeasureApplicability.Analyze(upstream, code, m.MeasureId, origin, date, memberships);
            if (applicability.GeographyEvidence != "unknown") checks.Remove("geographical_group_membership_unresolved");
            if (applicability.ExclusionEvidence != "unknown")
            {
                checks.Remove("geographical_exclusions_unresolved");
                checks.Remove("exclusion_relationship_unverified");
                if (applicability.GeographyEvidence != "unknown") checks.Remove("geographical_area_and_exclusions_not_resolved");
            }
            foreach (var check in applicability.EvidenceChecks) checks.Add(check);
            foreach (var id in m.GeographicalAreaReferences)
            {
                if (string.Equals(id, origin, StringComparison.OrdinalIgnoreCase)) continue;
                if (!areas.TryGetValue(id, out var area)) checks.Add("area_not_resolved:" + id);
                else if (area.MembershipEvidence == "candidate_membership_evidence") checks.Add("candidate_group_membership_requires_legal_review:" + id);
                else checks.Add("group_membership_not_verified:" + id);
            }
            if (m.GeographicalAreaReferences.Length == 0) checks.Add("measure_geography_unknown");
            // Even a straightforward percentage is not enough to determine payable duty.
            checks.Add("measure_type_and_duty_expression_not_legally_validated");
            checks.Add("trade_remedies_preferences_quotas_and_licences_not_fully_resolved");
            return new UkMeasureDecision(m.MeasureId, "requires_review", m.RawDutyExpression,
                m.GeographicalAreaReferences, checks.Order(StringComparer.Ordinal).ToArray())
                { Applicability = applicability, Rule = rules.Measures.FirstOrDefault(x => x.MeasureId == m.MeasureId) };
        }).ToArray();
        var warnings = new List<string> { "reference_only_not_a_payable_quote", "uk_import_vat_not_calculated",
            "customs_valuation_and_exchange_rate_not_verified", "no_final_duty_without_official_rule_validation" };
        warnings.AddRange(lookupPlan.Warnings);
        warnings.AddRange(rules.Warnings);
        warnings.AddRange(UkMeasureScreening.Screen(upstream, code, origin, date).Warnings);
        if (!lookupPlan.Complete) warnings.Add("omitted_geographical_areas:" + string.Join(",", lookupPlan.OmittedAreaIds));
        if (decisions.Length == 0) warnings.Add("no_measures_is_not_zero_duty");
        return new("requires_review", code, origin, date, decisions, lookups.Select(x => x.Area).ToArray(), upstream.SourceUrl,
            null, null, warnings.Distinct().Order(StringComparer.Ordinal).ToArray(), false)
            { DestinationTerritory = territory, ResponseSha256 = upstream.ResponseSha256, ReferenceGraphComplete = rules.Complete };
    }
}

public sealed record UkAreaEvidence(string AreaId, string LookupStatus, string MembershipEvidence,
    string? SourceUrl, bool VerifiedForCalculation)
{
    public DateOnly? AsOfDate { get; init; }
    public string[] MembershipPath { get; init; } = [];
    public string[] UnresolvedChecks { get; init; } = [];
    public string DestinationTerritory { get; init; } = "GB";
    public string? ResponseSha256 { get; init; }
}
public sealed record UkMeasureDecision(string MeasureId, string Status, string? RawDutyExpression,
    string[] GeographicalAreaReferences, string[] OutstandingChecks)
{
    public UkMeasureApplicabilityEvidence? Applicability { get; init; }
    public UkMeasureRule? Rule { get; init; }
}
public sealed record UkAssessmentReport(string Status, string CommodityCode, string Origin,
    DateOnly ImportDate, UkMeasureDecision[] Measures, UkAreaEvidence[] Areas,
    string? CommoditySourceUrl, decimal? ImportDuty, decimal? ImportVat,
    string[] Warnings, bool VerifiedForCalculation)
{
    public string DestinationTerritory { get; init; } = "GB";
    public string? ResponseSha256 { get; init; }
    public bool ReferenceGraphComplete { get; init; }
}
