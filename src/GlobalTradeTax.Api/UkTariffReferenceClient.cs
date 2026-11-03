using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace GlobalTradeTax.Api;

// Read-only, non-payable UK tariff reference adapter. No data is treated as a verified quote.
public sealed class UkTariffReferenceClient(HttpClient client, IConfiguration configuration, IMemoryCache cache)
{
    // Geography is a reference lookup, not proof of country membership or tariff applicability.
    public Task<UkReferenceResult> GetGeographicalAreaAsync(string areaId, CancellationToken ct)
    {
        if (!IsSafeAreaId(areaId))
            return Task.FromResult(new UkReferenceResult("invalid_code", null, null, "Area ID must contain 2-12 ASCII letters, digits, hyphens or underscores."));
        return GetReferenceAsync("geographical_areas/" + areaId, ct);
    }

    public static bool IsApprovedApiPath(string path) =>
        string.Equals(path.TrimEnd('/'), "/uk/api/v2", StringComparison.OrdinalIgnoreCase);

    public static bool IsSafeAreaId(string? id) => id is { Length: >= 2 and <= 12 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private async Task<UkReferenceResult> GetReferenceAsync(string path, CancellationToken ct)
    {
        var configured = configuration["TRADE_UK_TARIFF_API_BASE_URL"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps || baseUri.Port != 443 || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) || baseUri.UserInfo.Length != 0 ||
            (baseUri.Host != "www.trade-tariff.service.gov.uk" && baseUri.Host != "trade-tariff.service.gov.uk") ||
            !IsApprovedApiPath(baseUri.AbsolutePath))
            return new("not_configured", null, null, "Configure an approved UK Trade Tariff HTTPS API prefix.");
        var uri = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + baseUri.AbsolutePath.TrimEnd('/') + "/" + path);
        var cacheKey = "uk-ref:" + uri.AbsoluteUri;
        if (cache.TryGetValue(cacheKey, out UkReferenceResult? cached) && cached is not null) return cached;
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new("not_found", null, uri.ToString(), "Reference not found.");
            if (!response.IsSuccessStatusCode)
                return new("upstream_error", null, uri.ToString(), "UK reference API returned an error.");
            if (response.Content.Headers.ContentLength is > 1_048_576)
                return new("upstream_error", null, uri.ToString(), "Response too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) != 0)
            {
                if (memory.Length + read > 1_048_576) return new("upstream_error", null, uri.ToString(), "Response too large.");
                memory.Write(buffer, 0, read);
            }
            memory.Position = 0;
            using var document = await JsonDocument.ParseAsync(memory, cancellationToken: ct);
            if (!UkJsonApiContract.IsValidResource(document.RootElement, path[(path.LastIndexOf('/') + 1)..]))
                return new("invalid_upstream_contract", null, uri.ToString(), "UK API response failed structural contract validation.");
            var result = new UkReferenceResult("reference_only", document.RootElement.Clone(), uri.ToString(),
                "Read-only reference; geographical membership and duty applicability are not verified.");
            cache.Set(cacheKey, result, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(10)).SetSize(1));
            return result;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new("upstream_error", null, uri.ToString(), "UK reference lookup failed.");
        }
    }

    public async Task<UkReferenceResult> GetCommodityAsync(string commodityCode, CancellationToken ct)
    {
        if (commodityCode.Length is < 8 or > 10 || !commodityCode.All(char.IsAsciiDigit))
            return new("invalid_code", null, null, "Commodity code must be 8-10 digits.");

        var configured = configuration["TRADE_UK_TARIFF_API_BASE_URL"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps || baseUri.Port != 443 || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) || baseUri.UserInfo.Length != 0 ||
            (!string.Equals(baseUri.Host, "www.trade-tariff.service.gov.uk", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(baseUri.Host, "trade-tariff.service.gov.uk", StringComparison.OrdinalIgnoreCase)))
            return new("not_configured", null, null, "Set an approved HTTPS UK Trade Tariff API base URL; no request was sent.");

        // Base URL must point to a verified API prefix (for example /uk/api/v2/).
        var prefix = baseUri.AbsolutePath.TrimEnd('/') + "/";
        if (!IsApprovedApiPath(prefix))
            return new("not_configured", null, null, "Configured URL must include an API path prefix.");

        var uri = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + prefix + "commodities/" + commodityCode);
        try
        {
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new("not_found", null, uri.ToString(), "Commodity not found at the configured UK API.");
            if (!response.IsSuccessStatusCode)
                return new("upstream_error", null, uri.ToString(), "UK API returned an unsuccessful status.");
            if (response.Content.Headers.ContentLength is > 1_048_576)
                return new("upstream_error", null, uri.ToString(), "UK API response exceeded the size limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) != 0)
            {
                if (memory.Length + read > 1_048_576)
                    return new("upstream_error", null, uri.ToString(), "UK API response exceeded the size limit.");
                memory.Write(buffer, 0, read);
            }
            memory.Position = 0;
            using var document = await JsonDocument.ParseAsync(memory, cancellationToken: ct);
            if (!UkJsonApiContract.IsValidResource(document.RootElement, commodityCode))
                return new("invalid_upstream_contract", null, uri.ToString(), "UK API response failed structural contract validation.");
            return new("reference_only", document.RootElement.Clone(), uri.ToString(),
                "Unverified reference data only. Not an import-duty, VAT or payable landed-cost quote.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new("upstream_error", null, uri.ToString(), "UK tariff reference service could not be read.");
        }
    }
}

public sealed record UkReferenceResult(string Status, JsonElement? Data, string? SourceUrl, string Warning);
