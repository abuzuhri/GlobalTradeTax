using Npgsql;
using NpgsqlTypes;

namespace GlobalTradeTax.Api;

// Deliberately isolated from IImportRateProvider: staging is NOT an authoritative rate service.
public sealed class TariffStagingStore(NpgsqlDataSource dataSource)
{
    public async Task<(Guid BatchId, bool AlreadyExists)> StageAsync(TariffDatasetResult dataset, CancellationToken ct)
    {
        if (dataset.Status != "validated_reference_only" || dataset.Records.Count == 0 || dataset.Sha256 is null)
            throw new ArgumentException("Validated dataset required.");
        if (dataset.Records.Select(x => x.SourceUrl).Distinct().Count() != 1 ||
            dataset.Records.Select(x => x.SourceVersion).Distinct().Count() != 1)
            throw new ArgumentException("One source URL and version per batch required.");
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var batchId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO tariff_import_batches(batch_id,dataset_sha256,source_version,source_url,record_count)
            VALUES (@id,@sha,@version,@url,@count)
            ON CONFLICT (dataset_sha256) DO NOTHING
            RETURNING batch_id
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", batchId);
            insert.Parameters.AddWithValue("sha", dataset.Sha256);
            insert.Parameters.AddWithValue("version", dataset.Records[0].SourceVersion);
            insert.Parameters.AddWithValue("url", dataset.Records[0].SourceUrl);
            insert.Parameters.AddWithValue("count", dataset.Records.Count);
            var inserted = await insert.ExecuteScalarAsync(ct);
            if (inserted is null)
            {
                await using var existing = new NpgsqlCommand("SELECT batch_id FROM tariff_import_batches WHERE dataset_sha256=@sha", conn, tx);
                existing.Parameters.AddWithValue("sha", dataset.Sha256);
                var found = await existing.ExecuteScalarAsync(ct);
                await tx.CommitAsync(ct);
                return ((Guid)found!, true);
            }
        }
        for (var i = 0; i < dataset.Records.Count; i++)
        {
            var row = dataset.Records[i];
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO tariff_reference_rows(batch_id,row_number,destination,origin,hs_code,ad_valorem_percent,valid_from,valid_to,source_url,source_version)
                VALUES (@id,@n,@dest,@origin,@hs,@rate,@from,@to,@url,@version)
                """, conn, tx);
            cmd.Parameters.AddWithValue("id", batchId);
            cmd.Parameters.AddWithValue("n", i + 1);
            cmd.Parameters.AddWithValue("dest", row.Destination);
            cmd.Parameters.AddWithValue("origin", row.Origin);
            cmd.Parameters.AddWithValue("hs", row.HsCode);
            cmd.Parameters.AddWithValue("rate", row.AdValoremPercent);
            cmd.Parameters.Add("from", NpgsqlDbType.Date).Value = row.ValidFrom;
            cmd.Parameters.Add("to", NpgsqlDbType.Date).Value = row.ValidTo;
            cmd.Parameters.AddWithValue("url", row.SourceUrl);
            cmd.Parameters.AddWithValue("version", row.SourceVersion);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return (batchId, false);
    }
    public async Task<object?> GetBatchAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT dataset_sha256,source_version,source_url,record_count,status,imported_at FROM tariff_import_batches WHERE batch_id=@id");
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new { batchId = id, sha256 = r.GetString(0), sourceVersion = r.GetString(1), sourceUrl = r.GetString(2),
            rows = r.GetInt32(3), status = r.GetString(4), importedAt = r.GetDateTime(5),
            disclaimer = "Staged reference data only. Not activated for payable customs quotes." };
    }
}
