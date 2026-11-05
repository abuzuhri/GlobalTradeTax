using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;

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
        if (!UkDutyComponents.SafeHttps(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            token.Length > 16384 || token.Any(char.IsControl)) return Missing("Provider configuration is invalid.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(request) };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return Missing("Provider unavailable or rejected request.");
            var bytes = await UkTariffHttp.ReadReferenceBytesAsync(response.Content, 1_048_576, timeout.Token);
            if (bytes is null) return Missing("Provider response exceeded the size limit.");
            var result = ExactJsonInput.Deserialize<RateResult>(bytes);
            return result is not null && result.Status is not null && result.Warnings is not null &&
                result.Warnings.Length <= 100 && result.Warnings.All(w => w is { Length: <= 2048 })
                    ? result : Missing("Provider returned an invalid response.");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException or IOException or FormatException)
        {
            logger.LogWarning("Rate provider request failed: {ErrorType}", e.GetType().Name);
            return Missing("Provider request failed; no customs total can be issued.");
        }
    }
    private static RateResult Missing(string reason) => new(null, null, null, "unavailable", null, [reason]);
}
