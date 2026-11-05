using System.Numerics;

namespace GlobalTradeTax.Api;

// Exact largest-remainder allocation for illustrations; no claim about statutory rounding policy.
public static class UkMoneyAllocation
{
    public static IReadOnlyDictionary<string, decimal>? Allocate(decimal amountGbp, IReadOnlyDictionary<string, decimal> weights)
    {
        if (amountGbp < 0 || weights.Count is < 1 or > 20 || weights.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value < 0)) return null;
        try
        {
            var cents = new BigInteger(decimal.Round(checked(amountGbp * 100m), 0, MidpointRounding.AwayFromZero));
            var scales = weights.ToDictionary(x => x.Key, x => (decimal.GetBits(x.Value)[3] >> 16) & 0xff, StringComparer.Ordinal);
            var maxScale = scales.Values.Max();
            BigInteger IntegerWeight(KeyValuePair<string, decimal> pair)
            {
                var bits = decimal.GetBits(pair.Value);
                var mantissa = new BigInteger((uint)bits[0]) + (new BigInteger((uint)bits[1]) << 32) + (new BigInteger((uint)bits[2]) << 64);
                return mantissa * BigInteger.Pow(10, maxScale - scales[pair.Key]);
            }
            var values = weights.Select(x => (Id: x.Key, Weight: IntegerWeight(x))).ToArray();
            var sum = values.Aggregate(BigInteger.Zero, (total, x) => total + x.Weight);
            if (sum == 0) return cents == 0 ? weights.ToDictionary(x => x.Key, _ => 0m, StringComparer.Ordinal) : null;
            var parts = values.Select(x => (x.Id, Cents: cents * x.Weight / sum, Remainder: cents * x.Weight % sum)).ToArray();
            var remaining = (int)(cents - parts.Aggregate(BigInteger.Zero, (total, x) => total + x.Cents));
            var increments = parts.OrderByDescending(x => x.Remainder).ThenBy(x => x.Id, StringComparer.Ordinal)
                .Take(remaining).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            return parts.ToDictionary(x => x.Id, x => (decimal)(x.Cents + (increments.Contains(x.Id) ? 1 : 0)) / 100m, StringComparer.Ordinal);
        }
        catch (OverflowException) { return null; }
    }
}
