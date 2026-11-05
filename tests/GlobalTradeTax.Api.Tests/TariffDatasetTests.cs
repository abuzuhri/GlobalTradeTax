using System.Security.Cryptography;
using System.Text;
using GlobalTradeTax.Api;
using Npgsql;
using Xunit;

public sealed class TariffDatasetTests
{
    private const string Row = "GB,US,0101210000,9.5,2026-01-01,2026-12-31,https://example.test/source,reference-1";
    private static string Csv(params string[] rows) => TariffDataset.Header + "\n" + string.Join("\n", rows) + "\n";

    [Theory]
    [InlineData("0")]
    [InlineData("1000.0000")]
    [InlineData("9.1234")]
    public void Exact_supported_rates_keep_their_value_and_upload_hash(string rate)
    {
        var csv = Csv(Row.Replace(",9.5,", "," + rate + ",", StringComparison.Ordinal));
        var result = TariffDataset.Parse(csv);
        Assert.Equal("validated_reference_only", result.Status);
        Assert.Equal(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), Assert.Single(result.Records).AdValoremPercent);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv))).ToLowerInvariant(), result.Sha256);
        Assert.Equal(Encoding.UTF8.GetBytes(csv), result.DocumentBytes.ToArray());
    }

    [Theory]
    [InlineData("9.12345")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("1000.0001")]
    [InlineData("-1")]
    [InlineData("1e1")]
    [InlineData("NaN")]
    [InlineData(".5")]
    [InlineData("١.٥")]
    public void Rates_that_need_rounding_or_unsupported_notation_are_rejected(string rate)
    {
        var result = TariffDataset.Parse(Csv(Row.Replace(",9.5,", "," + rate + ",", StringComparison.Ordinal)));
        Assert.Equal("rejected", result.Status);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Duplicate_rows_are_rejected_without_a_partial_dataset()
    {
        var result = TariffDataset.Parse(Csv(Row, Row));
        Assert.Equal("rejected", result.Status);
        Assert.Contains("Rows 2 and 3", result.Error);
        Assert.Empty(result.Records);
        Assert.Equal(0, result.Rows);
    }

    [Theory]
    [InlineData("2026-12-31", "2027-01-01", "rejected")]
    [InlineData("2026-06-01", "2026-06-30", "rejected")]
    [InlineData("2025-01-01", "2027-01-01", "rejected")]
    [InlineData("2027-01-01", "2027-12-31", "validated_reference_only")]
    public void Intervals_are_inclusive_and_checked_even_when_rows_are_out_of_order(string from, string to, string expected)
    {
        var second = Row.Replace("2026-01-01,2026-12-31", from + "," + to, StringComparison.Ordinal);
        Assert.Equal(expected, TariffDataset.Parse(Csv(second, Row)).Status);
    }

    [Theory]
    [InlineData("GB,US", "GB,CA")]
    [InlineData("GB,US", "FR,US")]
    [InlineData("0101210000", "0101299000")]
    public void Different_trade_lanes_or_commodities_can_share_dates(string before, string after)
    {
        Assert.Equal(2, TariffDataset.Parse(Csv(Row, Row.Replace(before, after, StringComparison.Ordinal))).Rows);
    }

    [Fact]
    public void Country_normalization_does_not_hide_a_duplicate()
    {
        Assert.Equal("rejected", TariffDataset.Parse(Csv(Row, Row.Replace("GB,US", "gb,us", StringComparison.Ordinal))).Status);
    }

    [Theory]
    [InlineData("http://example.test/source")]
    [InlineData("https://name:password@example.test/source")]
    [InlineData("https://example.test:8443/source")]
    [InlineData("https://example.test/source#fragment")]
    [InlineData("https://example.test/bad path")]
    public void Unsafe_or_ambiguous_source_links_are_rejected(string url)
    {
        Assert.Equal("rejected", TariffDataset.Parse(Csv(Row.Replace("https://example.test/source", url, StringComparison.Ordinal))).Status);
    }

    [Theory]
    [InlineData("https://example.test/source", "https://other.test/source")]
    [InlineData("reference-1", "reference-2")]
    public void A_dataset_has_one_source_and_version(string before, string after)
    {
        var second = Row.Replace("0101210000", "0101299000", StringComparison.Ordinal).Replace(before, after, StringComparison.Ordinal);
        Assert.Equal("rejected", TariffDataset.Parse(Csv(Row, second)).Status);
    }

    [Theory]
    [InlineData("2026-01-01,2026-12-31", "2027-01-01,2026-12-31")]
    [InlineData("2026-01-01", "2026-02-30")]
    [InlineData("GB,US", "G,US")]
    [InlineData("0101210000", "01A1210000")]
    [InlineData("reference-1", "")]
    [InlineData("reference-1", "bad\u0000version")]
    public void Malformed_rows_have_no_records(string before, string after)
    {
        var result = TariffDataset.Parse(Csv(Row.Replace(before, after, StringComparison.Ordinal)));
        Assert.Equal("rejected", result.Status);
        Assert.Empty(result.Records);
    }

    [Fact]
    public void Exact_bytes_preserve_BOM_and_CRLF_and_do_not_alias_the_callers_buffer()
    {
        var text = Csv(Row).Replace("\n", "\r\n", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var result = TariffDataset.Parse(bytes.AsMemory());
        Assert.Equal("validated_reference_only", result.Status);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Sha256);
        Assert.NotEqual(TariffDataset.Parse(Csv(Row)).Sha256, result.Sha256);
        bytes[0] = 0;
        Assert.Equal(0xef, result.DocumentBytes.Span[0]);
    }

    [Fact]
    public void Invalid_UTF8_and_UTF16_are_rejected_instead_of_silently_replaced()
    {
        Assert.Equal("rejected", TariffDataset.Parse(new byte[] { 0xc3, 0x28 }.AsMemory()).Status);
        Assert.Equal("rejected", TariffDataset.Parse(Encoding.Unicode.GetBytes(Csv(Row)).AsMemory()).Status);
        Assert.Equal("rejected", TariffDataset.Parse(Csv(Row) + "\ud800").Status);
    }

    [Fact]
    public async Task Byte_limit_is_exact_and_works_without_content_length()
    {
        var valid = Encoding.UTF8.GetBytes(Csv(Row));
        var exact = valid.Concat(Enumerable.Repeat((byte)' ', TariffDataset.MaxBytes - valid.Length)).ToArray();
        Assert.Equal("validated_reference_only", (await TariffDataset.ReadAsync(new MemoryStream(exact), default)).Status);
        using var stream = new CountingStream(exact.Concat(new byte[10000]).ToArray());
        Assert.Equal("too_large", (await TariffDataset.ReadAsync(stream, default)).Status);
        Assert.Equal(TariffDataset.MaxBytes + 1, stream.Consumed);
    }

    [Fact]
    public void UTF8_byte_count_limits_multibyte_versions_before_allocation()
    {
        Assert.Equal("too_large", TariffDataset.Parse(new string('é', TariffDataset.MaxBytes / 2 + 1)).Status);
    }

    [Fact]
    public async Task Forged_staging_results_are_rejected_before_opening_a_database()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=not-used;Username=test;Timeout=1");
        var store = new TariffStagingStore(dataSource, TimeProvider.System);
        var parsed = TariffDataset.Parse(Csv(Row));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StageAsync(parsed with { Sha256 = new string('0', 64) }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.StageAsync(parsed with { Records = [] }, default));
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int Consumed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            Consumed += count;
            return count;
        }
    }
}
