using System.Text.Json;
using Npgsql;
namespace GlobalTradeTax.Api;

public sealed class AuditStore(NpgsqlDataSource database)
{
    public async Task WriteAsync(string tenant, string action, string status, object details, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("INSERT INTO audit_events(tenant_id,action,status,details_json) VALUES (@tenant,@action,@status,@details)", connection);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("details", JsonSerializer.Serialize(details));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
