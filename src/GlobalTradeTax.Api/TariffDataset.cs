using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GlobalTradeTax.Api;

// Reference-only CSV; SourceSha256 is the upload digest, not a remotely verified source hash.
public sealed record TariffRecord(string Destination, string Origin, string HsCode,
    decimal AdValoremPercent, DateOnly ValidFrom, DateOnly ValidTo, string SourceUrl,
    string SourceVersion, string SourceSha256);
public sealed record TariffDatasetResult(string Status, int Rows, string? Error,
    string? Sha256, IReadOnlyList<TariffRecord> Records)
{
    public ReadOnlyMemory<byte> DocumentBytes { get; init; }
}

public static class TariffDataset
{
    public const int MaxBytes = 1_048_576;
    public const string Header = "destination,origin,hs_code,ad_valorem_percent,valid_from,valid_to,source_url,source_version";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static TariffDatasetResult Parse(string csv)
    {
        try
        {
            if (Utf8.GetByteCount(csv) > MaxBytes) return TooLarge();
            return Parse(Utf8.GetBytes(csv).AsMemory());
        }
        catch (EncoderFallbackException) { return Failure("Dataset must be valid UTF-8.", null); }
    }

    public static async Task<TariffDatasetResult> ReadAsync(Stream stream, CancellationToken ct)
    {
        var bytes = await ReadBytesAsync(stream, MaxBytes, ct);
        return bytes is null ? TooLarge() : Parse(bytes.AsMemory());
    }

    internal static async Task<byte[]?> ReadBytesAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var document = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxBytes + 1 - (int)document.Length)), ct);
            if (read == 0) return document.ToArray();
            document.Write(buffer, 0, read);
            if (document.Length > maxBytes) return null;
        }
    }

    public static TariffDatasetResult Parse(ReadOnlyMemory<byte> document)
    {
        if (document.Length > MaxBytes) return TooLarge();
        // Own the exact bytes: BOM, line endings and trailing newline are significant to this digest.
        var bytes = document.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string csv;
        try { csv = Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Failure("Dataset must be valid UTF-8.", hash); }
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        var header = lines[0].StartsWith('\uFEFF') ? lines[0][1..] : lines[0];
        if (header != Header) return Failure("Unexpected header; only the documented unquoted CSV schema is accepted.", hash);
        var records = new List<TariffRecord>();
        var numbered = new List<(TariffRecord Record, int Line)>();
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
            var rateText = fields[3].Trim();
            var parts = rateText.Split('.');
            var exactRate = parts.Length is 1 or 2 && parts[0].Length is >= 1 and <= 4 && parts[0].All(char.IsAsciiDigit) &&
                (parts.Length == 1 || parts[1].Length is >= 1 and <= 4 && parts[1].All(char.IsAsciiDigit));
            var version = fields[7].Trim();
            if (!Country(destination) || !Country(origin) || hs.Length is < 6 or > 12 || !hs.All(char.IsAsciiDigit) ||
                !exactRate || !decimal.TryParse(rateText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) || rate > 1000 ||
                !DateOnly.TryParseExact(fields[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
                !DateOnly.TryParseExact(fields[5], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) || from > to ||
                !TryProvenanceUrl(fields[6].Trim(), out var url) || !CleanText(version, 256))
                return Failure($"Row {i + 1}: invalid country, HS, exact rate (max four decimals), dates or provenance.", hash);
            var record = new TariffRecord(destination, origin, hs, rate, from, to, url!, version, hash);
            records.Add(record);
            numbered.Add((record, i + 1));
        }
        if (records.Count == 0) return Failure("Dataset has no records.", hash);
        if (records.Select(r => r.SourceUrl).Distinct(StringComparer.Ordinal).Count() != 1 ||
            records.Select(r => r.SourceVersion).Distinct(StringComparer.Ordinal).Count() != 1)
            return Failure("One source URL and version per dataset required.", hash);
        foreach (var group in numbered.GroupBy(x => (x.Record.Destination, x.Record.Origin, x.Record.HsCode)))
        {
            (TariffRecord Record, int Line)? previous = null;
            foreach (var row in group.OrderBy(x => x.Record.ValidFrom).ThenBy(x => x.Record.ValidTo))
            {
                if (previous is { } prior && row.Record.ValidFrom <= prior.Record.ValidTo)
                    return Failure($"Rows {prior.Line} and {row.Line}: duplicate or overlapping effective intervals for the same trade lane and HS code.", hash);
                previous = row;
            }
        }
        return new("validated_reference_only", records.Count, null, hash, records.AsReadOnly()) { DocumentBytes = bytes };
    }

    public static bool TryProvenanceUrl(string? value, out string? canonical)
    {
        canonical = null;
        if (value is null || value.Length > 2048 || value.Any(char.IsControl) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsWellFormedOriginalString() ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        canonical = uri.AbsoluteUri;
        return true;
    }

    internal static bool CleanText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
    private static bool Country(string s) => s.Length == 2 && s.All(c => c is >= 'A' and <= 'Z');
    private static TariffDatasetResult TooLarge() => new("too_large", 0, "Dataset exceeds the 1 MiB byte limit.", null, []);
    private static TariffDatasetResult Failure(string reason, string? hash) => new("rejected", 0, reason, hash, []);
}
