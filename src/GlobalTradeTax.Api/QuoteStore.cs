using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public sealed record StoredQuote(string TenantId, string IdempotencyKey, string RequestFingerprint,
    QuoteResponse Response, DateTimeOffset CreatedAt);

// Development-only in-memory storage. Replace with transactional PostgreSQL persistence before production.
public sealed class QuoteStore
{
    private readonly ConcurrentDictionary<string, StoredQuote> _quotes = new();
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public (QuoteResponse? Quote, string? Error) Create(string tenant, string key, QuoteRequest request, QuoteService service)
    {
        var composite = $"{tenant.Length}:{tenant}:{key}";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        var gate = _locks.GetOrAdd(composite, _ => new object());
        lock (gate)
        {
            if (_quotes.TryGetValue(composite, out var existing))
                return existing.RequestFingerprint == fingerprint ? (existing.Response, null) : (null, "Idempotency key reused with different request payload.");
            var response = service.Calculate(request);
            _quotes[composite] = new StoredQuote(tenant, key, fingerprint, response, DateTimeOffset.UtcNow);
            return (response, null);
        }
    }
    public QuoteResponse? Find(string tenant, string quoteId) => _quotes.Values
        .FirstOrDefault(q => q.TenantId == tenant && q.Response.QuoteId == quoteId)?.Response;
}
