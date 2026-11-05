using System.Globalization;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public sealed record UkFxReport(UkFxRate[] Rates, string[] Checks);

// Invoice and tariff conversion feeds have different purposes and are never interchangeable.
public static class UkExchangeRates
{
    public static bool IsMonthlyCollection(JsonElement root, DateOnly date)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("errors", out _)) return false;
        var data = UkEvidenceJson.Object(root, "data");
        var attributes = UkEvidenceJson.Object(data, "attributes");
        return UkEvidenceJson.String(data, "type") == "exchange_rate_collection" &&
            UkEvidenceJson.String(attributes, "year") == date.Year.ToString(CultureInfo.InvariantCulture) &&
            int.TryParse(UkEvidenceJson.String(attributes, "month"), NumberStyles.None, CultureInfo.InvariantCulture, out var month) && month == date.Month &&
            UkEvidenceJson.String(attributes, "type") == "monthly";
    }

    public static bool IsTariffCollection(JsonElement root) => root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("errors", out _) &&
        root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array && data.GetArrayLength() <= 1024 &&
        data.EnumerateArray().All(x => UkEvidenceJson.String(x, "type") == "monetary_exchange_rate" &&
            UkEvidenceJson.String(x, "id") is { Length: > 0 } && UkEvidenceJson.Object(x, "attributes").ValueKind == JsonValueKind.Object);

    public static UkFxReport Monthly(UkReferenceResult source, DateOnly date)
    {
        if (source.Status != "reference_only" || source.DestinationTerritory != "GB" || source.AsOfDate != date || source.Data is not { } root || !IsMonthlyCollection(root, date))
            return new([], ["monthly_fx_contract_unverified"]);
        var data = root.GetProperty("data");
        if (!UkEvidenceJson.TryReferences(data, "exchange_rates", "exchange_rate", true, out var ids) ||
            !root.TryGetProperty("included", out var included) || included.ValueKind != JsonValueKind.Array || included.GetArrayLength() > 4096)
            return new([], ["monthly_fx_linkage_incomplete"]);
        var index = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in included.EnumerateArray())
        {
            if (UkEvidenceJson.String(item, "type") != "exchange_rate") continue;
            var id = UkEvidenceJson.String(item, "id");
            if (id is null || !index.TryAdd(id, item)) return new([], ["monthly_fx_duplicate_or_missing_identity"]);
        }
        if (ids.Any(id => !index.ContainsKey(id))) return new([], ["monthly_fx_resource_missing"]);
        var parsed = new List<(string Currency, decimal Rate, DateOnly From, DateOnly To)>();
        var checks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            var a = UkEvidenceJson.Object(index[id], "attributes");
            var currency = UkEvidenceJson.String(a, "currency_code");
            if (currency is not { Length: 3 } || !currency.All(c => c is >= 'A' and <= 'Z')) return new([], ["monthly_fx_currency_unverified"]);
            if (!ExactPositive(UkEvidenceJson.String(a, "rate"), out var rate) ||
                !UkEvidenceJson.TryDate(UkEvidenceJson.String(a, "validity_start_date"), out var from) ||
                !UkEvidenceJson.TryDate(UkEvidenceJson.String(a, "validity_end_date"), out var to) || from > to)
            { checks.Add("monthly_fx_invalid:" + currency); continue; }
            if (date >= from && date <= to) parsed.Add((currency, rate, from, to));
        }
        var rates = new List<UkFxRate>();
        foreach (var group in parsed.GroupBy(x => x.Currency).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var values = group.Distinct().ToArray();
            if (values.Length != 1 || checks.Contains("monthly_fx_invalid:" + group.Key))
            { checks.Add("monthly_fx_ambiguous:" + group.Key); continue; }
            var value = values[0];
            rates.Add(new(group.Key, "invoice_customs_value", 1m / value.Rate, value.From, value.To, source.SourceUrl!, source.ResponseSha256));
        }
        return new(rates.ToArray(), checks.Order(StringComparer.Ordinal).ToArray());
    }

    public static UkFxReport Tariff(UkReferenceResult source, DateOnly date)
    {
        if (source.Status != "reference_only" || source.AsOfDate != date || source.DestinationTerritory != "XI" ||
            source.Data is not { } root || !IsTariffCollection(root)) return new([], ["tariff_fx_contract_unverified"]);
        var records = new List<(DateOnly From, decimal Rate)>();
        foreach (var item in root.GetProperty("data").EnumerateArray())
        {
            var a = UkEvidenceJson.Object(item, "attributes");
            if (UkEvidenceJson.String(a, "child_monetary_unit_code") != "GBP" ||
                !UkEvidenceJson.TryDate(UkEvidenceJson.String(a, "validity_start_date"), out var from) || from.Day != 1 ||
                !ExactPositive(UkEvidenceJson.String(a, "exchange_rate"), out var rate)) return new([], ["tariff_fx_period_unverified"]);
            records.Add((from, rate));
        }
        var candidates = records.Where(x => x.From.Year == date.Year && x.From.Month == date.Month).Distinct().ToArray();
        if (candidates.Length != 1) return new([], ["tariff_fx_missing_or_ambiguous_for_import_month"]);
        var candidate = candidates[0];
        return new([new("EUR", "tariff_duty", candidate.Rate, candidate.From,
            new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)), source.SourceUrl!, source.ResponseSha256)], []);
    }

    private static bool ExactPositive(string? text, out decimal rate)
    {
        rate = 0;
        return text is { Length: > 0 and <= 30 } && !text.Contains('e', StringComparison.OrdinalIgnoreCase) &&
            text.Count(char.IsAsciiDigit) <= 28 && decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out rate) && rate > 0;
    }
}
