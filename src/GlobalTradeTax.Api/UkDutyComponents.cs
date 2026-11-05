namespace GlobalTradeTax.Api;

public sealed record UkFxRate(string Currency, string Purpose, decimal GbpPerUnit, DateOnly ValidFrom, DateOnly ValidTo,
    string SourceUrl, string? ResponseSha256);
public sealed record UkDutyQuantities(decimal? CustomsValueGbp, decimal? NetMassKg, decimal? ItemCount);
public sealed record UkComponentArithmetic(string ComponentId, string Operation, decimal? SourceAmount,
    string? SourceCurrency, decimal? IllustrativeGbp, string[] Checks);
public sealed record UkDutyArithmetic(string Status, decimal? IllustrativeGbp, UkComponentArithmetic[] Components, string[] Checks)
{
    public bool VerifiedForCalculation => false;
}

// Operates on typed source components, never HTML or a guessed rate. No measure selection is performed.
public static class UkDutyComponents
{
    public static UkDutyArithmetic Evaluate(IReadOnlyList<UkRuleComponent> components, UkDutyQuantities quantities,
        DateOnly date, IReadOnlyList<UkFxRate> rates)
    {
        UkDutyArithmetic Unknown(string reason, UkComponentArithmetic[]? values = null) => new("requires_review", null, values ?? [], [reason]);
        if (components.Count is < 1 or > 16) return Unknown("missing_or_excessive_duty_components");
        if (quantities.CustomsValueGbp is < 0 || quantities.NetMassKg is < 0 || quantities.ItemCount is < 0)
            return Unknown("negative_duty_input");
        if (components.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != components.Count)
            return Unknown("duplicate_duty_component");
        if (components.Count(c => c.ExpressionCode == "01") != 1 || components.Any(c => c.ExpressionCode is not ("01" or "04")))
            return Unknown("unsupported_component_operation");
        var results = new List<UkComponentArithmetic>();
        try
        {
            foreach (var component in components.OrderBy(c => c.ExpressionCode, StringComparer.Ordinal).ThenBy(c => c.Id, StringComparer.Ordinal))
            {
                string? error = null;
                decimal? sourceAmount = null;
                decimal? gbp = null;
                var currency = component.Currency;
                if (!component.Complete || component.Amount is null or < 0) error = "component_data_unverified";
                else if (component.Qualifier is not null) error = "measurement_qualifier_not_supported";
                else if (currency is null && component.Unit is null)
                {
                    currency = "GBP";
                    if (quantities.CustomsValueGbp is null) error = "customs_value_required_for_percentage";
                    else sourceAmount = checked(quantities.CustomsValueGbp.Value * component.Amount.Value / 100m);
                }
                else if (currency is "GBP" or "EUR" && component.Unit is not null)
                {
                    var quantity = component.Unit switch
                    {
                        "KGM" => quantities.NetMassKg,
                        "DTN" => quantities.NetMassKg / 100m,
                        "TNE" => quantities.NetMassKg / 1000m,
                        "NAR" => quantities.ItemCount is { } count && count == decimal.Truncate(count) ? count : null,
                        _ => null
                    };
                    if (quantity is null) error = "explicit_supported_quantity_required:" + component.Unit;
                    else sourceAmount = checked(component.Amount.Value * quantity.Value);
                }
                else error = "unsupported_currency_or_measurement_unit";
                if (sourceAmount is not null)
                {
                    if (TryFactor(rates, currency!, "tariff_duty", date, out var factor)) gbp = checked(sourceAmount.Value * factor);
                    else error = "tariff_currency_conversion_evidence_required:" + currency;
                }
                results.Add(new(component.Id, component.ExpressionCode!, sourceAmount, currency, gbp, error is null ? [] : [error]));
            }
            if (results.Any(r => r.IllustrativeGbp is null)) return Unknown("incomplete_component_arithmetic", results.ToArray());
            decimal amount = results.Where(r => r.Operation is "01" or "04").Sum(r => r.IllustrativeGbp!.Value);
            return new("illustrative_only", amount, results.ToArray(),
                ["legal_measure_selection_and_conditions_require_review", "rounding_and_currency_basis_not_authorized_for_payment"]);
        }
        catch (OverflowException) { return Unknown("duty_arithmetic_overflow", results.ToArray()); }
    }

    public static bool TryFactor(IReadOnlyList<UkFxRate> rates, string currency, string purpose, DateOnly date, out decimal factor)
    {
        factor = 0;
        if (currency == "GBP") { factor = 1; return true; }
        var matches = rates.Where(r => r.Currency == currency && r.Purpose == purpose && r.ValidFrom <= date && r.ValidTo >= date).ToArray();
        if (matches.Length != 1 || matches[0].GbpPerUnit <= 0 || !SafeHttps(matches[0].SourceUrl)) return false;
        factor = matches[0].GbpPerUnit;
        return true;
    }

    internal static bool SafeHttps(string? url) => url is { Length: <= 2048 } &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsWellFormedOriginalString() && uri.Scheme == "https" &&
        uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.AbsoluteUri.Length <= 2048;
}
