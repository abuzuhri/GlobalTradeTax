using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GlobalTradeTax.Api;

// One process-wide token cache. Credentials and token response bodies never enter result/log messages.
public sealed class UkTariffAccessTokenProvider(
    IHttpClientFactory clients, IConfiguration configuration, TimeProvider clock) : IDisposable
{
    public const string HttpClientName = "UkTariffOAuth";
    public const string TokenEndpoint = "https://auth.id.trade-tariff.service.gov.uk/oauth2/token";
    private readonly SemaphoreSlim refreshLock = new(1);
    private TokenState? cached;
    private string? failedFingerprint;
    private DateTimeOffset retryAfter;

    private sealed class TokenState(string value, string fingerprint, DateTimeOffset refreshAt)
    {
        public string Value { get; } = value;
        public string Fingerprint { get; } = fingerprint;
        public DateTimeOffset RefreshAt { get; } = refreshAt;
        public override string ToString() => "UK tariff token (redacted)";
    }

    private sealed class Credentials(string clientId, string clientSecret)
    {
        public string ClientId { get; } = clientId;
        public string ClientSecret { get; } = clientSecret;
        public string Fingerprint { get; } = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{clientId.Length}:{clientId}{clientSecret}")));
        public override string ToString() => "UK tariff credentials (redacted)";
    }

    // Also partitions reference-cache entries when an operator replaces the credentials.
    public string? ConfigurationFingerprint => ReadCredentials()?.Fingerprint;

    private Credentials? ReadCredentials()
    {
        var id = configuration["TRADE_UK_TARIFF_CLIENT_ID"];
        var secret = configuration["TRADE_UK_TARIFF_CLIENT_SECRET"];
        return string.IsNullOrWhiteSpace(id) || id.Length > 4096 ||
            string.IsNullOrWhiteSpace(secret) || secret.Length > 4096 ? null : new(id, secret);
    }

    public async Task<string?> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var credentials = ReadCredentials();
        if (credentials is null) return null;
        var current = Volatile.Read(ref cached);
        if (IsCurrent(current, credentials)) return current!.Value;

        await refreshLock.WaitAsync(ct);
        try
        {
            credentials = ReadCredentials();
            if (credentials is null) return null;
            current = Volatile.Read(ref cached);
            if (IsCurrent(current, credentials)) return current!.Value;
            if (failedFingerprint == credentials.Fingerprint && clock.GetUtcNow() < retryAfter) return null;

            var fresh = await RequestAsync(credentials, ct);
            if (fresh is null)
            {
                failedFingerprint = credentials.Fingerprint;
                retryAfter = clock.GetUtcNow().AddSeconds(5);
                return null;
            }
            Volatile.Write(ref cached, fresh);
            failedFingerprint = null;
            return fresh.Value;
        }
        finally { refreshLock.Release(); }
    }

    private bool IsCurrent(TokenState? token, Credentials credentials) =>
        token is not null && token.Fingerprint == credentials.Fingerprint && clock.GetUtcNow() < token.RefreshAt;

    public void Invalidate(string rejectedToken)
    {
        var observed = Volatile.Read(ref cached);
        // A delayed 401 for an old token must not evict a newer token acquired by another caller.
        if (observed?.Value == rejectedToken) Interlocked.CompareExchange(ref cached, null, observed);
    }

    private async Task<TokenState?> RequestAsync(Credentials credentials, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var requestedAt = clock.GetUtcNow();
        try
        {
            using var client = clients.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = credentials.ClientId,
                    ["client_secret"] = credentials.ClientSecret
                })
            };
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            using var document = await UkTariffHttp.ReadDocumentAsync(response.Content, 65_536, timeout.Token);
            if (document is null || document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var root = document.RootElement;
            if (root.TryGetProperty("error", out _) ||
                root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
                return null;
            if (!root.TryGetProperty("access_token", out var accessToken) || accessToken.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("token_type", out var type) || type.ValueKind != JsonValueKind.String ||
                !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase) ||
                !root.TryGetProperty("expires_in", out var expiry) || expiry.ValueKind != JsonValueKind.Number ||
                !expiry.TryGetInt32(out var seconds) || seconds is < 1 or > 86400) return null;
            var value = accessToken.GetString();
            if (value is not { Length: > 0 and <= 16384 } ||
                !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~' or '+' or '/' or '=')) return null;
            // Use the declared lifetime, with a bounded early-renewal margin; do not assume the documented one day.
            var refreshAt = requestedAt.AddSeconds(seconds - Math.Min(60, seconds / 10d));
            if (refreshAt <= clock.GetUtcNow()) return null;
            return new(value, credentials.Fingerprint, refreshAt);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    public void Dispose() => refreshLock.Dispose();
}

internal static class UkTariffHttp
{
    // Reference documents need an exact response digest; token bodies never use this evidence path.
    public static async Task<byte[]?> ReadReferenceBytesAsync(HttpContent content, int byteLimit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > byteLimit) return null;
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, byteLimit + 1 - (int)memory.Length)), ct);
            if (count == 0) return memory.ToArray();
            memory.Write(buffer, 0, count);
            if (memory.Length > byteLimit) return null;
        }
    }

    public static async Task<JsonDocument?> ReadDocumentAsync(HttpContent content, int byteLimit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > byteLimit) return null;
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (memory.Length + count > byteLimit) return null;
            memory.Write(buffer, 0, count);
        }
        memory.Position = 0;
        return await JsonDocument.ParseAsync(memory, cancellationToken: ct);
    }
}
