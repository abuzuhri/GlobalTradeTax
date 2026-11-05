using Npgsql;
using NpgsqlTypes;

namespace GlobalTradeTax.Api;

// Archives and review attestations never implement or feed IImportRateProvider.
public sealed class TariffStagingStore(NpgsqlDataSource dataSource, TimeProvider clock)
{
    private sealed record Batch(Guid Id, string Sha, string Version, string Url, int Rows, string Status, DateTime ImportedAt);

    public async Task<TariffStageResult> StageAsync(TariffDatasetResult dataset, CancellationToken ct)
    {
        var parsed = TariffDataset.Parse(dataset.DocumentBytes);
        if (dataset.Status != "validated_reference_only" || parsed.Status != "validated_reference_only" ||
            dataset.Sha256 != parsed.Sha256 || dataset.Rows != parsed.Rows || !dataset.Records.SequenceEqual(parsed.Records))
            throw new ArgumentException("Validated exact-byte dataset required.", nameof(dataset));
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var batchId = Guid.NewGuid();
        bool inserted;
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO tariff_import_batches(batch_id,dataset_sha256,source_version,source_url,record_count)
            VALUES (@id,@sha,@version,@url,@count) ON CONFLICT (dataset_sha256) DO NOTHING RETURNING batch_id
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", batchId);
            insert.Parameters.AddWithValue("sha", parsed.Sha256!);
            insert.Parameters.AddWithValue("version", parsed.Records[0].SourceVersion);
            insert.Parameters.AddWithValue("url", parsed.Records[0].SourceUrl);
            insert.Parameters.AddWithValue("count", parsed.Rows);
            inserted = await insert.ExecuteScalarAsync(ct) is not null;
        }
        if (!inserted)
        {
            await using var lookup = new NpgsqlCommand("SELECT batch_id FROM tariff_import_batches WHERE dataset_sha256=@sha FOR UPDATE", conn, tx);
            lookup.Parameters.AddWithValue("sha", parsed.Sha256!);
            batchId = (Guid)(await lookup.ExecuteScalarAsync(ct))!;
            var batch = (await ReadBatchAsync(conn, tx, batchId, false, ct))!;
            if (!MatchesBatch(batch, parsed) || !await RowsMatchAsync(conn, tx, batchId, parsed, ct))
                return new(null, true, "stored_batch_does_not_match_exact_upload");
        }
        else
        {
            for (var i = 0; i < parsed.Rows; i++)
            {
                var row = parsed.Records[i];
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
                cmd.Parameters.AddWithValue("from", NpgsqlDbType.Date, row.ValidFrom);
                cmd.Parameters.AddWithValue("to", NpgsqlDbType.Date, row.ValidTo);
                cmd.Parameters.AddWithValue("url", row.SourceUrl);
                cmd.Parameters.AddWithValue("version", row.SourceVersion);
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        await using (var archive = new NpgsqlCommand("""
            INSERT INTO tariff_import_documents(batch_id,dataset_sha256,document_bytes) VALUES (@id,@sha,@bytes)
            ON CONFLICT (batch_id) DO NOTHING
            """, conn, tx))
        {
            archive.Parameters.AddWithValue("id", batchId);
            archive.Parameters.AddWithValue("sha", parsed.Sha256!);
            archive.Parameters.AddWithValue("bytes", NpgsqlDbType.Bytea, parsed.DocumentBytes.ToArray());
            await archive.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(batchId, !inserted);
    }

    public async Task<TariffReviewResult> ReviewAsync(Guid id, TariffReviewRequest request, CancellationToken ct)
    {
        var (draft, error) = TariffReviewPolicy.Normalize(request);
        if (draft is null) return new("invalid_request", Error: error);
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Serializes review revisions and legacy-document backfills for this batch only.
        var batch = await ReadBatchAsync(conn, tx, id, true, ct);
        if (batch is null) return new("not_found");
        if (batch.Sha != draft.DatasetSha256) return new("conflict", Error: "dataset_digest_precondition_failed");
        var fingerprint = TariffReviewPolicy.Fingerprint(draft, batch.Url, batch.Version);
        var replay = await ReadReviewAsync(conn, tx, id, fingerprint, ct);
        if (replay is not null)
        {
            await tx.CommitAsync(ct);
            return new("already_recorded", replay);
        }
        var previous = await ReadReviewAsync(conn, tx, id, null, ct);
        if (previous?.ReviewId != draft.PreviousReviewId) return new("conflict", Error: "previous_review_precondition_failed");
        var now = clock.GetUtcNow();
        if (draft.Disposition == "reviewed_reference_only" && draft.ExpiresAt <= now)
            return new("invalid_request", Error: "reference_review_expiry_must_be_in_the_future");
        if (batch.Status != "staged" && draft.Disposition != "rejected") return new("conflict", Error: "batch_is_rejected");
        byte[]? bytes;
        await using (var document = new NpgsqlCommand("SELECT document_bytes FROM tariff_import_documents WHERE batch_id=@id", conn, tx))
        {
            document.Parameters.AddWithValue("id", id);
            bytes = await document.ExecuteScalarAsync(ct) as byte[];
        }
        if (bytes is null) return new("conflict", Error: "exact_upload_archive_required");
        var parsed = TariffDataset.Parse(bytes.AsMemory());
        if (parsed.Status != "validated_reference_only" || !MatchesBatch(batch, parsed) || !await RowsMatchAsync(conn, tx, id, parsed, ct))
            return new("conflict", Error: "archive_or_stored_rows_failed_validation");
        var reviewId = Guid.NewGuid();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO tariff_reference_reviews(batch_id,dataset_sha256,review_id,revision,previous_review_id,disposition,
                reviewed_by,licence_url,review_note,reviewed_at,expires_at,source_url,source_version,request_sha256)
            VALUES (@id,@sha,@review,@revision,@previous,@disposition,@reviewer,@licence,@note,@at,@expires,@url,@version,@fingerprint)
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("sha", batch.Sha);
            insert.Parameters.AddWithValue("review", reviewId);
            insert.Parameters.AddWithValue("revision", (previous?.Revision ?? 0) + 1);
            insert.Parameters.AddWithValue("previous", NpgsqlDbType.Uuid, (object?)draft.PreviousReviewId ?? DBNull.Value);
            insert.Parameters.AddWithValue("disposition", draft.Disposition);
            insert.Parameters.AddWithValue("reviewer", draft.ReviewedBy);
            insert.Parameters.AddWithValue("licence", NpgsqlDbType.Text, (object?)draft.LicenceUrl ?? DBNull.Value);
            insert.Parameters.AddWithValue("note", draft.Note);
            insert.Parameters.AddWithValue("at", now.UtcDateTime);
            insert.Parameters.AddWithValue("expires", NpgsqlDbType.TimestampTz, (object?)draft.ExpiresAt?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("url", batch.Url);
            insert.Parameters.AddWithValue("version", batch.Version);
            insert.Parameters.AddWithValue("fingerprint", fingerprint);
            await insert.ExecuteNonQueryAsync(ct);
        }
        var view = await ReadReviewAsync(conn, tx, id, fingerprint, ct);
        await tx.CommitAsync(ct);
        return new("recorded", view);
    }

    public async Task<TariffBatchView?> GetBatchAsync(Guid id, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var batch = await ReadBatchAsync(conn, null, id, false, ct);
        if (batch is null) return null;
        var review = await ReadReviewAsync(conn, null, id, null, ct);
        await using var cmd = new NpgsqlCommand("""
            SELECT min(valid_from),max(valid_to),count(*) FILTER (WHERE valid_to < @today),
                count(*) FILTER (WHERE valid_from > @today),count(*) FILTER (WHERE valid_from <= @today AND valid_to >= @today),
                EXISTS (SELECT 1 FROM tariff_import_documents WHERE batch_id=@id)
            FROM tariff_reference_rows WHERE batch_id=@id
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("today", NpgsqlDbType.Date, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new(id, batch.Sha, batch.Version, batch.Url, batch.Rows, batch.Status, batch.ImportedAt,
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1),
            reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetBoolean(5), review);
    }

    public async Task<TariffReviewHistory?> GetReviewsAsync(Guid id, int afterRevision, int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        if (await ReadBatchAsync(conn, null, id, false, ct) is null) return null;
        await using var cmd = new NpgsqlCommand(ReviewSelect + " WHERE batch_id=@id AND revision>@after ORDER BY revision LIMIT @limit", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("after", afterRevision);
        cmd.Parameters.AddWithValue("limit", limit + 1);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var reviews = new List<TariffReviewView>();
        while (await reader.ReadAsync(ct)) reviews.Add(Review(reader));
        var more = reviews.Count > limit;
        if (more) reviews.RemoveAt(limit);
        return new(id, reviews, reviews.Count == 0 ? afterRevision : reviews[^1].Revision, more);
    }

    private static bool MatchesBatch(Batch batch, TariffDatasetResult parsed) => batch.Sha == parsed.Sha256 && batch.Rows == parsed.Rows &&
        batch.Url == parsed.Records[0].SourceUrl && batch.Version == parsed.Records[0].SourceVersion;

    private static async Task<bool> RowsMatchAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid id, TariffDatasetResult dataset, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT row_number,destination,origin,hs_code,ad_valorem_percent,valid_from,valid_to,source_url,source_version
            FROM tariff_reference_rows WHERE batch_id=@id ORDER BY row_number
            """, conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var i = 0;
        while (await reader.ReadAsync(ct))
        {
            if (i >= dataset.Rows) return false;
            var row = dataset.Records[i];
            if (reader.GetInt32(0) != i + 1 || reader.GetString(1) != row.Destination || reader.GetString(2) != row.Origin ||
                reader.GetString(3) != row.HsCode || reader.GetDecimal(4) != row.AdValoremPercent ||
                reader.GetFieldValue<DateOnly>(5) != row.ValidFrom || reader.GetFieldValue<DateOnly>(6) != row.ValidTo ||
                reader.GetString(7) != row.SourceUrl || reader.GetString(8) != row.SourceVersion) return false;
            i++;
        }
        return i == dataset.Rows;
    }

    private static async Task<Batch?> ReadBatchAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid id, bool locked, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT batch_id,dataset_sha256,source_version,source_url,record_count,status,imported_at
            FROM tariff_import_batches WHERE batch_id=@id
            """ + (locked ? " FOR UPDATE" : ""), conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetInt32(4), reader.GetString(5), reader.GetDateTime(6)) : null;
    }

    private const string ReviewSelect = """
        SELECT review_id,dataset_sha256,disposition,reviewed_by,licence_url,expires_at,review_note,reviewed_at,
            source_url,source_version,request_sha256,revision,previous_review_id FROM tariff_reference_reviews
        """;

    private async Task<TariffReviewView?> ReadReviewAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, Guid id, string? fingerprint, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(ReviewSelect + " WHERE batch_id=@id" +
            (fingerprint is null ? " ORDER BY revision DESC LIMIT 1" : " AND request_sha256=@fingerprint"), conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        if (fingerprint is not null) cmd.Parameters.AddWithValue("fingerprint", fingerprint);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Review(reader) : null;
    }

    private TariffReviewView Review(NpgsqlDataReader reader)
    {
        DateTimeOffset? expiry = reader.IsDBNull(5) ? null : new DateTimeOffset(reader.GetDateTime(5));
        var disposition = reader.GetString(2);
        return new(reader.GetGuid(0), reader.GetString(1), disposition, reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            expiry, reader.GetString(6), new DateTimeOffset(reader.GetDateTime(7)), reader.GetString(8), reader.GetString(9),
            TariffReviewPolicy.EvidenceStatus(disposition, expiry, clock.GetUtcNow()), reader.GetString(10), reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12));
    }
}

public sealed record TariffBatchView(Guid BatchId, string Sha256, string SourceVersion, string SourceUrl,
    int Rows, string Status, DateTime ImportedAt, DateOnly? ValidFrom, DateOnly? ValidTo,
    long ExpiredRows, long FutureRows, long EffectiveRows, bool DocumentArchived, TariffReviewView? Review)
{
    public bool ApprovedForCalculation => false;
    public string Disclaimer => "Staged reference data and operator attestations only. Not activated for payable customs quotes.";
}
public sealed record TariffReviewHistory(Guid BatchId, IReadOnlyList<TariffReviewView> Reviews, int NextAfterRevision, bool HasMore);
