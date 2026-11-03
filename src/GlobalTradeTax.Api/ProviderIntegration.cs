using System.Net.Http.Json;
using System.Text.Json;

namespace GlobalTradeTax.Api;

// External provider contract: the configured provider must return an independently verified,
// effective tariff/tax quote. Never derive rates from a hardcoded country percentage.
public sealed class ExternalImportRateProvider(HttpClient client, IConfiguration config, ILogger<ExternalImportRateProvider> logger) : IImportRateProvider
{
    public async Task<RateResult> GetRatesAsync(QuoteRequest request, CancellationToken ct)
    {
        var endpoint = config["TRADE_RATE_PROVIDER_URL"];
        var token = config["TRADE_RATE_PROVIDER_TOKEN"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(token))
            return Missing("External provider URL/token not configured.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return Missing("Provider URL must use HTTPS.");
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(request) };
            message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return Missing("Provider unavailable or rejected request.");
            var result = await response.Content.ReadFromJsonAsync<RateResult>(cancellationToken: ct);
            return result ?? Missing("Provider returned an empty response.");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning("Rate provider request failed: {ErrorType}", e.GetType().Name);
            return Missing("Provider request failed; no customs total can be issued.");
        }
    }
    private static RateResult Missing(string reason) => new(null, null, null, "unavailable", null, [reason]);
}
