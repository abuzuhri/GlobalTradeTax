using System.Globalization;
using System.Text.RegularExpressions;

namespace GlobalTradeTax.Api;

// Mathematical evaluator only. It never establishes legal applicability, quota entitlement,
// customs value, currency conversion, or authority to issue a payable quote.
public sealed record UkDutyExpressionResult(decimal? IllustrativeAmount, string Status,
    string[] UnresolvedChecks, string Expression, bool VerifiedForCalculation = false);

public static class UkDutyExpressionEngine
{
    private static readonly Regex Percent = new(@"^\s*(\d+(?:\.\d+)?)\s*%\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Specific = new(@"^\s*(\d+(?:\.\d+)?)\s*GBP\s*/\s*(100\s*kg|kg|tonne|item)\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Compound = new(@"^\s*(\d+(?:\.\d+)?)\s*%\s*\+\s*(\d+(?:\.\d+)?)\s*GBP\s*/\s*(100\s*kg|kg|tonne|item)\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static UkDutyExpressionResult Evaluate(string? expression, decimal? customsValueGbp,
        decimal? netMassKg, decimal? itemCount)
    {
        var original = expression ?? string.Empty;
        UkDutyExpressionResult Unknown(string reason) => new(null, "requires_review", [reason], original);
        if (string.IsNullOrWhiteSpace(expression)) return Unknown("missing_duty_expression");
        if (expression.Length > 512) return Unknown("unsupported_duty_expression");
        if (customsValueGbp is < 0 || netMassKg is < 0 || itemCount is < 0)
            return Unknown("negative_input");
        try
        {
            var match = Percent.Match(expression);
            if (match.Success)
            {
                if (customsValueGbp is null) return Unknown("missing_verified_customs_value_gbp");
                return Proportion(match.Groups[1].Value,customsValueGbp.Value,100m);
            }
            match = Specific.Match(expression);
            if (match.Success)
                return UnitAmount(match.Groups[1].Value, match.Groups[2].Value);
            match = Compound.Match(expression);
            if (match.Success)
            {
                if (customsValueGbp is null) return Unknown("missing_verified_customs_value_gbp");
                var unit = UnitAmount(match.Groups[2].Value, match.Groups[3].Value);
                if (unit.IllustrativeAmount is null) return unit;
                var percent = Proportion(match.Groups[1].Value,customsValueGbp.Value,100m);
                if (percent.IllustrativeAmount is null) return percent;
                return Result(checked(percent.IllustrativeAmount.Value + unit.IllustrativeAmount.Value));
            }
            return Unknown("unsupported_duty_expression");
        }
        catch (Exception ex) when (ex is OverflowException or FormatException)
        {
            return Unknown("invalid_or_overflowing_expression");
        }

        UkDutyExpressionResult UnitAmount(string rateText, string unitText)
        {
            var unit = Regex.Replace(unitText.Trim().ToLowerInvariant(), @"\s+", "");
            decimal? quantity = unit switch
            {
                "kg" => netMassKg,
                "100kg" or "tonne" => netMassKg,
                "item" => itemCount is { } count && count == decimal.Truncate(count) ? count : null,
                _ => null
            };
            if (quantity is null) return Unknown("missing_quantity_for_" + unit.Replace(' ', '_'));
            var divisor = unit switch { "100kg" => 100m, "tonne" => 1000m, _ => 1m };
            return Proportion(rateText,quantity.Value,divisor);
        }
        UkDutyExpressionResult Proportion(string rateText, decimal quantity, decimal divisor)
        {
            var value = UkDutyComponents.MultiplyDivide(Rate(rateText),quantity,divisor);
            return value is null ? Unknown("arithmetic_precision_lost") : Result(value.Value);
        }
        static decimal Rate(string text)
        {
            if (text.Count(char.IsAsciiDigit) > 28 || text.Contains('.') && text.Split('.')[1].Length > 28)
                throw new FormatException("Unsupported duty precision.");
            return decimal.Parse(text, CultureInfo.InvariantCulture);
        }
        UkDutyExpressionResult Result(decimal amount) => new(amount, "illustrative_only",
            ["legal_measure_applicability_not_verified", "quota_preference_and_exclusion_checks_required", "customs_valuation_and_currency_evidence_required"], original);
    }
}
