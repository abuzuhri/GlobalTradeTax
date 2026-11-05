using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace GlobalTradeTax.Api;

// Read-only, non-payable UK tariff reference adapter. No data is treated as a verified quote.
public sealed class UkTariffReferenceClient(HttpClient client, IConfiguration configuration, IMemoryCache cache,
    UkTariffAccessTokenProvider? tokens = null)
{
    public const string ManagedApiBaseUrl = "https://api.trade-tariff.service.gov.uk/uk/api/";

    public Task<UkReferenceResult> GetGeographicalAreaAsync(string areaId, CancellationToken ct) =>
        !IsSafeAreaId(areaId)
            ? Task.FromResult(new UkReferenceResult("invalid_code", null, null, "Area ID must contain 2-12 ASCII letters, digits, hyphens or underscores."))
            : GetReferenceAsync("geographical_areas/", areaId, cacheable: true, ct);

    public Task<UkReferenceResult> GetCommodityAsync(string commodityCode, CancellationToken ct) =>
        commodityCode.Length is < 8 or > 10 || !commodityCode.All(char.IsAsciiDigit)
            ? Task.FromResult(new UkReferenceResult("invalid_code", null, null, "Commodity code must be 8-10 digits."))
            : GetReferenceAsync("commodities/", commodityCode, cacheable: false, ct);

    public static bool IsApprovedApiPath(string path) =>
        string.Equals(path.TrimEnd('/'), "/uk/api/v2", StringComparison.OrdinalIgnoreCase);

    public static bool IsSafeAreaId(string? id) => id is { Length: >= 2 and <= 12 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private bool TryGetBaseUri(out Uri? uri)
    {
        if (!Uri.TryCreate(configuration["TRADE_UK_TARIFF_API_BASE_URL"], UriKind.Absolute, out uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        return IsManaged(uri)
            ? string.Equals(uri.AbsolutePath.TrimEnd('/'), "/uk/api", StringComparison.OrdinalIgnoreCase)
            : (uri.Host == "www.trade-tariff.service.gov.uk" || uri.Host == "trade-tariff.service.gov.uk") &&
                IsApprovedApiPath(uri.AbsolutePath);
    }

    private static bool IsManaged(Uri uri) => uri.Host == "api.trade-tariff.service.gov.uk";

    private async Task<UkReferenceResult> GetReferenceAsync(string resourcePath, string id, bool cacheable, CancellationToken ct)
    {
        if (!TryGetBaseUri(out var baseUri))
            return new("not_configured", null, null, "Configure an approved UK Trade Tariff HTTPS API prefix.");
        var managed = IsManaged(baseUri!);
        var fingerprint = managed ? tokens?.ConfigurationFingerprint : "public";
        if (fingerprint is null)
            return new("not_configured", null, null, "Managed UK API access requires configured client credentials.");
        var uri = new Uri(baseUri!.GetLeftPart(UriPartial.Authority) + baseUri.AbsolutePath.TrimEnd('/') + "/" + resourcePath + id);
        var cacheKey = "uk-ref:" + uri.AbsoluteUri + ":" + fingerprint;
        if (cacheable && cache.TryGetValue(cacheKey, out UkReferenceResult? cached) && cached is not null) return cached;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await SendAsync(uri, managed, timeout.Token);
            if (response is null)
                return new("upstream_error", null, uri.ToString(), "UK API authentication failed.");
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new("not_found", null, uri.ToString(), "Reference not found.");
            if (!response.IsSuccessStatusCode)
                return new("upstream_error", null, uri.ToString(), "UK reference API returned an error.");
            using var document = await UkTariffHttp.ReadDocumentAsync(response.Content, 1_048_576, timeout.Token);
            if (document is null)
                return new("upstream_error", null, uri.ToString(), "UK API response exceeded the size limit.");
            var valid = cacheable ? UkJsonApiContract.IsValidGeographicalArea(document.RootElement, id)
                : UkJsonApiContract.IsValidCommodity(document.RootElement, id);
            if (!valid)
                return new("invalid_upstream_contract", null, uri.ToString(), "UK API response failed structural contract validation.");
            var result = new UkReferenceResult("reference_only", document.RootElement.Clone(), uri.ToString(),
                "Read-only reference; geographical membership and duty applicability are not verified.");
            if (cacheable)
                cache.Set(cacheKey, result, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(10)).SetSize(1));
            return result;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            return new("upstream_error", null, uri.ToString(), "UK reference lookup failed.");
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(Uri uri, bool managed, CancellationToken ct)
    {
        var token = managed ? await tokens!.GetAsync(ct) : null;
        if (managed && token is null) return null;
        var response = await SendRequestAsync(uri, token, ct);
        if (!managed || response.StatusCode != HttpStatusCode.Unauthorized) return response;
        response.Dispose();
        tokens!.Invalidate(token!);
        token = await tokens.GetAsync(ct);
        if (token is null) return null;
        response = await SendRequestAsync(uri, token, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Invalidate(token);
        return response;
    }

    private async Task<HttpResponseMessage> SendRequestAsync(Uri uri, string? token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/vnd.hmrc.2.0+json");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }
}

public sealed record UkReferenceResult(string Status, JsonElement? Data, string? SourceUrl, string Warning);
