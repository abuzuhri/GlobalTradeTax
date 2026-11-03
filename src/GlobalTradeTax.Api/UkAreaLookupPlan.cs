namespace GlobalTradeTax.Api;

// Coverage planning only; no inference of legal tariff eligibility.
public sealed record UkAreaLookupPlan(string[] AreaIds, string[] OmittedAreaIds, bool Complete, string[] Warnings);
public static class UkAreaLookupPlanner
{
    public const int MaxAreaLookups = 20;
    public static UkAreaLookupPlan Create(IEnumerable<string> references, string origin)
    {
        var all = references.Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => !string.Equals(id, origin, StringComparison.OrdinalIgnoreCase))
            .Where(UkTariffReferenceClient.IsSafeAreaId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        var omitted = all.Skip(MaxAreaLookups).ToArray();
        return new(all.Take(MaxAreaLookups).ToArray(), omitted, omitted.Length == 0,
            omitted.Length == 0 ? [] : ["geographical_lookup_limit_exceeded_incomplete_evidence"]);
    }
}
