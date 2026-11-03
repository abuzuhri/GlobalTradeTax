using System.Text.Json;

namespace GlobalTradeTax.Api;

// An orchestration layer over the UK reference APIs, not a customs determination.
// Explicitly fail-closed for quotas, VAT, trade remedies, licences and unknown measures.
public sealed class UkImportAssessment(UkTariffReferenceClient client)
{
    public async Task<UkAssessmentReport> AssessAsync(string code, string origin, DateOnly date, CancellationToken ct)
    {
        if (origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z'))
            return new("invalid_origin", code, origin, date, [], [], null, null, null,
                ["origin_must_be_uppercase_iso_alpha2"], false);
        var upstream = await client.GetCommodityAsync(code, ct);
        if (upstream.Status != "reference_only")
            return new(upstream.Status, code, origin, date, [], [], upstream.SourceUrl, null, null,
                [upstream.Warning], false);
        var evidence = UkMeasureEvidence.Analyze(upstream, code, origin, date);
        var geography = UkGeographyResolver.Analyze(upstream, code, origin, date);
        var lookupPlan = UkAreaLookupPlanner.Create(evidence.Measures.SelectMany(m => m.GeographicalAreaReferences), origin);
        var areaIds = lookupPlan.AreaIds;
        // Bounded upstream work; cache is implemented in UkTariffReferenceClient.
        using var limiter = new SemaphoreSlim(4);
        var lookups = await Task.WhenAll(areaIds.Select(async id =>
        {
            await limiter.WaitAsync(ct);
            try
            {
                var response = await client.GetGeographicalAreaAsync(id, ct);
                var membership = UkAreaMembership.Inspect(response, id, origin, date);
                return new UkAreaEvidence(id, response.Status, membership.EvidenceStatus, response.SourceUrl,
                    membership.VerifiedForCalculation);
            }
            finally { limiter.Release(); }
        }));
        var areas = lookups.ToDictionary(x => x.AreaId, StringComparer.OrdinalIgnoreCase);
        var decisions = evidence.Measures.Select(m =>
        {
            var checks = new HashSet<string>(m.UnresolvedChecks, StringComparer.Ordinal);
            var geo = geography.Measures.FirstOrDefault(x => x.MeasureId == m.MeasureId);
            if (geo is not null) foreach (var check in geo.UnresolvedChecks) checks.Add(check);
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
                m.GeographicalAreaReferences, checks.Order(StringComparer.Ordinal).ToArray());
        }).ToArray();
        var warnings = new List<string> { "reference_only_not_a_payable_quote", "uk_import_vat_not_calculated",
            "customs_valuation_and_exchange_rate_not_verified", "no_final_duty_without_official_rule_validation" };
        warnings.AddRange(lookupPlan.Warnings);
        if (!lookupPlan.Complete) warnings.Add("omitted_geographical_areas:" + string.Join(",", lookupPlan.OmittedAreaIds));
        if (decisions.Length == 0) warnings.Add("no_measures_is_not_zero_duty");
        return new("requires_review", code, origin, date, decisions, lookups, upstream.SourceUrl,
            null, null, warnings.ToArray(), false);
    }
}

public sealed record UkAreaEvidence(string AreaId, string LookupStatus, string MembershipEvidence,
    string? SourceUrl, bool VerifiedForCalculation);
public sealed record UkMeasureDecision(string MeasureId, string Status, string? RawDutyExpression,
    string[] GeographicalAreaReferences, string[] OutstandingChecks);
public sealed record UkAssessmentReport(string Status, string CommodityCode, string Origin,
    DateOnly ImportDate, UkMeasureDecision[] Measures, UkAreaEvidence[] Areas,
    string? CommoditySourceUrl, decimal? ImportDuty, decimal? ImportVat,
    string[] Warnings, bool VerifiedForCalculation);
