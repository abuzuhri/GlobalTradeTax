using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GlobalTradeTax.Api;

// Reference-data ingestion only. A published tariff is not sufficient to calculate a payable quote:
// customs valuation, preferential origin, measures, exclusions and VAT bases require separate rules.
public sealed record TariffRecord(string Destination, string Origin, string HsCode,
    decimal AdValoremPercent, DateOnly ValidFrom, DateOnly ValidTo, string SourceUrl,
    string SourceVersion, string SourceSha256);
public sealed record TariffDatasetResult(string Status, int Rows, string? Error,
    string? Sha256, IReadOnlyList<TariffRecord> Records);

public static class TariffDataset
{
    public const string Header = "destination,origin,hs_code,ad_valorem_percent,valid_from,valid_to,source_url,source_version";
    public static TariffDatasetResult Parse(string csv)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv))).ToLowerInvariant();
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != Header)
            return Failure("Unexpected header; only the documented unquoted CSV schema is accepted.", hash);
        var records = new List<TariffRecord>();
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.Contains('"')) return Failure($"Row {i + 1}: quoted CSV is not supported.", hash);
            var fields = line.Split(',');
            if (fields.Length != 8) return Failure($"Row {i + 1}: expected eight fields.", hash);
            var destination = fields[0].Trim().ToUpperInvariant();
            var origin = fields[1].Trim().ToUpperInvariant();
            var hs = fields[2].Trim();
            if (!Country(destination) || !Country(origin) || hs.Length is < 6 or > 12 || !hs.All(char.IsAsciiDigit) ||
                !decimal.TryParse(fields[3], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) || rate < 0 || rate > 1000 ||
                !DateOnly.TryParseExact(fields[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
                !DateOnly.TryParseExact(fields[5], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) || from > to ||
                !Uri.TryCreate(fields[6].Trim(), UriKind.Absolute, out var url) || url.Scheme != "https" ||
                string.IsNullOrWhiteSpace(fields[7]))
                return Failure($"Row {i + 1}: invalid country, HS, rate, dates or provenance.", hash);
            records.Add(new TariffRecord(destination, origin, hs, rate, from, to, url.ToString(), fields[7].Trim(), hash));
        }
        if (records.Count == 0) return Failure("Dataset has no records.", hash);
        return new TariffDatasetResult("validated_reference_only", records.Count, null, hash, records);
    }
    private static bool Country(string s) => s.Length == 2 && s.All(c => c is >= 'A' and <= 'Z');
    private static TariffDatasetResult Failure(string reason, string hash) => new("rejected", 0, reason, hash, []);
}
