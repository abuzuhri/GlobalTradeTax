using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace GlobalTradeTax.Api;

public sealed class TenantAuth(NpgsqlDataSource database)
{
    public static string HashKey(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    public async Task<string?> AuthenticateAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.Headers.TryGetValue("X-API-Key", out var value)) return null;
        var key = value.ToString();
        if (!key.StartsWith("gtt_live_", StringComparison.Ordinal) || key.Length is < 32 or > 256) return null;
        var hash = HashKey(key);
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("""
            SELECT k.tenant_id FROM tenant_api_keys k JOIN tenants t ON t.tenant_id=k.tenant_id
            WHERE k.key_hash=@hash AND k.enabled AND k.revoked_at IS NULL AND (k.expires_at IS NULL OR k.expires_at > clock_timestamp()) AND t.enabled
            """, connection);
        command.Parameters.AddWithValue("hash", hash);
        return await command.ExecuteScalarAsync(ct) as string;
    }
}
