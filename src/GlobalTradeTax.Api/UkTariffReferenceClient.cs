using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace GlobalTradeTax.Api;

// Read-only, non-payable UK tariff reference adapter. No data is treated as a verified quote.
public sealed class UkTariffReferenceClient(HttpClient client, IConfiguration configuration, IMemoryCache cache,
    UkTariffAccessTokenProvider? tokens = null)
{
    public const string ManagedApiBaseUrl = "https://api.trade-tariff.service.gov.uk/uk/api/";
    public const string ManagedXiApiBaseUrl = "https://api.trade-tariff.service.gov.uk/xi/api/";

    public Task<UkReferenceResult> GetGeographicalAreaAsync(string areaId, CancellationToken ct) =>
        GetGeographicalAreaAsync(areaId, null, ct);

    public Task<UkReferenceResult> GetGeographicalAreaAsync(string areaId, DateOnly? asOfDate, CancellationToken ct) =>
        GetGeographicalAreaAsync(areaId, asOfDate, "GB", ct);

    public Task<UkReferenceResult> GetGeographicalAreaAsync(string areaId, DateOnly? asOfDate, string territory, CancellationToken ct) =>
        !IsSafeAreaId(areaId)
            ? Task.FromResult(new UkReferenceResult("invalid_code", null, null, "Area ID must contain 2-12 ASCII letters, digits, hyphens or underscores.", asOfDate)
                { DestinationTerritory = territory })
            : GetReferenceAsync("geographical_areas/", areaId, cacheable: true, asOfDate, territory, ct);

    public Task<UkReferenceResult> GetCommodityAsync(string commodityCode, CancellationToken ct) =>
        GetCommodityAsync(commodityCode, null, ct);

    public Task<UkReferenceResult> GetCommodityAsync(string commodityCode, DateOnly? asOfDate, CancellationToken ct) =>
        GetCommodityAsync(commodityCode, asOfDate, "GB", ct);

    public Task<UkReferenceResult> GetCommodityAsync(string commodityCode, DateOnly? asOfDate, string territory, CancellationToken ct) =>
        commodityCode.Length is < 8 or > 10 || !commodityCode.All(char.IsAsciiDigit)
            ? Task.FromResult(new UkReferenceResult("invalid_code", null, null, "Commodity code must be 8-10 digits.", asOfDate)
                { DestinationTerritory = territory })
            : GetReferenceAsync("commodities/", commodityCode, cacheable: false, asOfDate, territory, ct);

    public Task<UkReferenceResult> GetMonthlyExchangeRatesAsync(DateOnly date, CancellationToken ct) =>
        GetReferenceAsync("exchange_rates/", date.ToString("yyyy-MM", CultureInfo.InvariantCulture), true, date, "GB", ct);

    public Task<UkReferenceResult> GetTariffExchangeRatesAsync(DateOnly date, CancellationToken ct) =>
        GetReferenceAsync("monetary_exchange_rates", "", true, date, "XI", ct);

    public static bool IsApprovedApiPath(string path, string territory = "GB") => territory is "GB" or "XI" &&
        string.Equals(path.TrimEnd('/'), (territory == "GB" ? "/uk" : "/xi") + "/api/v2", StringComparison.OrdinalIgnoreCase);

    public static bool IsSafeAreaId(string? id) => id is { Length: >= 2 and <= 12 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private bool TryGetBaseUri(string territory, out Uri? uri)
    {
        var key = territory == "GB" ? "TRADE_UK_TARIFF_API_BASE_URL" : "TRADE_UK_TARIFF_XI_API_BASE_URL";
        if (!Uri.TryCreate(configuration[key], UriKind.Absolute, out uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        return IsManaged(uri)
            ? string.Equals(uri.AbsolutePath.TrimEnd('/'), (territory == "GB" ? "/uk" : "/xi") + "/api", StringComparison.OrdinalIgnoreCase)
            : (uri.Host == "www.trade-tariff.service.gov.uk" || uri.Host == "trade-tariff.service.gov.uk") &&
                IsApprovedApiPath(uri.AbsolutePath, territory);
    }

    private static bool IsManaged(Uri uri) => uri.Host == "api.trade-tariff.service.gov.uk";

    private async Task<UkReferenceResult> GetReferenceAsync(string resourcePath, string id, bool cacheable,
        DateOnly? asOfDate, string territory, CancellationToken ct)
    {
        UkReferenceResult Error(string status, string warning, string? url = null) =>
            new(status, null, url, warning, asOfDate) { DestinationTerritory = territory };
        if (territory is not ("GB" or "XI")) return Error("invalid_territory", "Destination territory must be GB or XI.");
        // The upstream silently substitutes today for unsupported dates. Refuse them locally.
        if (asOfDate > DateOnly.FromDateTime(DateTime.UtcNow).AddYears(20))
            return Error("invalid_import_date", "UK reference dates cannot be more than 20 years in the future.");
        if (!TryGetBaseUri(territory, out var baseUri))
            return Error("not_configured", "Configure an approved tariff API prefix for the requested territory.");
        var managed = IsManaged(baseUri!);
        var fingerprint = managed ? tokens?.ConfigurationFingerprint : "public";
        if (fingerprint is null)
            return Error("not_configured", "Managed UK API access requires configured client credentials.");
        var query = asOfDate is { } date ? "?as_of=" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        if (resourcePath == "exchange_rates/") query += "&filter%5Btype%5D=monthly";
        var uri = new Uri(baseUri!.GetLeftPart(UriPartial.Authority) + baseUri.AbsolutePath.TrimEnd('/') + "/" + resourcePath + id + query);
        var cacheKey = "uk-ref:" + uri.AbsoluteUri + ":" + fingerprint;
        if (cacheable && cache.TryGetValue(cacheKey, out UkReferenceResult? cached) && cached is not null) return cached;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await SendAsync(uri, managed, timeout.Token);
            if (response is null)
                return Error("upstream_error", "UK API authentication failed.", uri.ToString());
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Error("not_found", "Reference not found.", uri.ToString());
            if (!response.IsSuccessStatusCode)
                return Error("upstream_error", "UK reference API returned an error.", uri.ToString());
            var bytes = await UkTariffHttp.ReadReferenceBytesAsync(response.Content, 1_048_576, timeout.Token);
            if (bytes is null)
                return Error("upstream_error", "UK API response exceeded the size limit.", uri.ToString());
            using var document = JsonDocument.Parse(bytes);
            var valid = resourcePath switch
            {
                "exchange_rates/" => UkExchangeRates.IsMonthlyCollection(document.RootElement, asOfDate!.Value),
                "monetary_exchange_rates" => UkExchangeRates.IsTariffCollection(document.RootElement),
                _ => cacheable ? UkJsonApiContract.IsValidGeographicalArea(document.RootElement, id)
                    : UkJsonApiContract.IsValidCommodity(document.RootElement, id)
            };
            if (!valid)
                return Error("invalid_upstream_contract", "UK API response failed structural contract validation.", uri.ToString());
            var result = new UkReferenceResult("reference_only", document.RootElement.Clone(), uri.ToString(),
                "Read-only reference; geographical membership and duty applicability are not verified.", asOfDate)
            {
                DestinationTerritory = territory,
                ResponseSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                CapturedAtUtc = DateTimeOffset.UtcNow
            };
            if (cacheable)
                cache.Set(cacheKey, result, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(10)).SetSize(1));
            return result;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException)
        {
            return Error("upstream_error", "UK reference lookup failed.", uri.ToString());
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

// AsOfDate records the requested upstream date, not independent legal verification of its response.
public sealed record UkReferenceResult(string Status, JsonElement? Data, string? SourceUrl, string Warning,
    DateOnly? AsOfDate = null)
{
    public string DestinationTerritory { get; init; } = "GB";
    public string? ResponseSha256 { get; init; }
    public DateTimeOffset? CapturedAtUtc { get; init; }
}
