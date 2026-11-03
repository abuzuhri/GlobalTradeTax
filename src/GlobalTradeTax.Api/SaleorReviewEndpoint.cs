using System.Text.Json;

namespace GlobalTradeTax.Api;

public static class SaleorReviewEndpoint
{
    public const int MaxBodyBytes = 65_536;

    public static async Task<IResult> HandleAsync(HttpRequest request, string tenant, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Saleor:ReviewEnabled")) return Results.Problem("Saleor review pilot is disabled.", statusCode: 503);
        if (!request.Headers.TryGetValue("Saleor-Api-Url", out var apiUrls) || apiUrls.Count != 1 ||
            !request.Headers.TryGetValue("Saleor-Signature", out var signatures) || signatures.Count != 1)
            return Results.Unauthorized();
        var apiUrl = apiUrls.ToString();
        if (!IsApprovedApiUrl(apiUrl)) return Results.Unauthorized();
        var stores = configuration.GetSection("Saleor:Stores").GetChildren().Where(store =>
            store.GetValue<bool>("Enabled") && store["TenantId"] == tenant && store["ApiUrl"] == apiUrl).ToArray();
        if (stores.Length != 1) return Results.Unauthorized();
        if (!request.HasJsonContentType()) return Results.StatusCode(415);
        if (request.ContentLength is > MaxBodyBytes) return Results.StatusCode(413);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await request.Body.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (body.Length + bytesRead > MaxBodyBytes) return Results.StatusCode(413);
            body.Write(buffer, 0, bytesRead);
        }
        var bytes = body.ToArray();
        if (!SaleorSignature.Verify(bytes, signatures.ToString(), stores[0]["KeyId"] ?? "", stores[0]["PublicKeyPem"] ?? ""))
            return Results.Unauthorized();
        try
        {
            var review = SaleorReview.Analyze(bytes, tenant, apiUrl, DateTimeOffset.UtcNow);
            request.HttpContext.Response.Headers.CacheControl = "no-store";
            return Results.Ok(review);
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
        {
            return Results.BadRequest(new { error = "invalid_saleor_review_payload" });
        }
    }

    public static bool IsApprovedApiUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath == "/graphql/" && uri.AbsoluteUri == value;
}
