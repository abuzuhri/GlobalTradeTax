using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace GlobalTradeTax.Api;

// Durable, tenant-scoped idempotency and quote retrieval.
public sealed class PostgresQuoteStore(NpgsqlDataSource dataSource)
{
    public async Task<(QuoteResponse? Quote, string? Error)> CreateAsync(string tenant, string key, QuoteRequest request, QuoteService service, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(request);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Transaction-scoped advisory lock serializes concurrent requests for the same tenant/key.
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@identity, 0))", connection, tx))
        {
            advisory.Parameters.AddWithValue("identity", tenant + ":" + key);
            await advisory.ExecuteNonQueryAsync(ct);
        }
        await using (var existing = new NpgsqlCommand("SELECT fingerprint, response_json FROM quotes WHERE tenant_id=@tenant AND idempotency_key=@key", connection, tx))
        {
            existing.Parameters.AddWithValue("tenant", tenant);
            existing.Parameters.AddWithValue("key", key);
            await using var reader = await existing.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var storedFingerprint = reader.GetString(0);
                var storedResponse = reader.GetString(1);
                await reader.CloseAsync();
                await tx.CommitAsync(ct);
                return storedFingerprint == fingerprint
                    ? (JsonSerializer.Deserialize<QuoteResponse>(storedResponse), null)
                    : (null, "Idempotency key reused with different request payload.");
            }
        }
        var quote = service.Calculate(request);
        await using (var insert = new NpgsqlCommand("INSERT INTO quotes (quote_id,tenant_id,idempotency_key,fingerprint,response_json) VALUES (@id,@tenant,@key,@fingerprint,@response)", connection, tx))
        {
            insert.Parameters.AddWithValue("id", quote.QuoteId);
            insert.Parameters.AddWithValue("tenant", tenant);
            insert.Parameters.AddWithValue("key", key);
            insert.Parameters.AddWithValue("fingerprint", fingerprint);
            insert.Parameters.AddWithValue("response", JsonSerializer.Serialize(quote));
            await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return (quote, null);
    }

    public async Task<QuoteResponse?> FindAsync(string tenant, string id, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT response_json FROM quotes WHERE tenant_id=@tenant AND quote_id=@id", connection);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("id", id);
        var result = await command.ExecuteScalarAsync(ct);
        return result is string json ? JsonSerializer.Deserialize<QuoteResponse>(json) : null;
    }
}
