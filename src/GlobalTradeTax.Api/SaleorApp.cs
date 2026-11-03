using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public sealed record SaleorAppRegistration(string Id, string TenantId, string ApiUrl, string TransportUrl,
    string PublicBaseUrl, string CallbackBaseUrl, string DashboardOrigin, string KeyId, string PublicKeyPem,
    SaleorAppVerificationKey[]? VerificationKeys = null);

public sealed record SaleorAppVerificationKey(string KeyId, string PublicKeyPem);

public sealed class SaleorApp(IConfiguration configuration, IHostEnvironment environment, SaleorAppStore store, IHttpClientFactory clients)
{
    public const string Identifier = "globaltradetax.checkout-review";

    public SaleorAppRegistration? Resolve(string id)
    {
        if (!configuration.GetValue<bool>("SaleorApp:Enabled") || id.Length is < 1 or > 64 ||
            !id.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')) return null;
        var candidates = configuration.GetSection("SaleorApp:Stores").GetChildren()
            .Where(item => item.GetValue<bool>("Enabled") && item["Id"] == id).ToArray();
        if (candidates.Length != 1) return null;
        var settings = candidates[0];
        var keys = settings.GetSection("VerificationKeys").GetChildren()
            .Select(item => new SaleorAppVerificationKey(item["KeyId"] ?? "", item["PublicKeyPem"] ?? "")).ToArray();
        if (keys.Length > 3 || keys.Any(key => string.IsNullOrWhiteSpace(key.KeyId) || string.IsNullOrWhiteSpace(key.PublicKeyPem)) ||
            keys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() != keys.Length) return null;
        var value = new SaleorAppRegistration(id, settings["TenantId"] ?? "", settings["ApiUrl"] ?? "",
            settings["TransportUrl"] ?? settings["ApiUrl"] ?? "", settings["PublicBaseUrl"] ?? "",
            settings["CallbackBaseUrl"] ?? settings["PublicBaseUrl"] ?? "", settings["DashboardOrigin"] ?? "",
            settings["KeyId"] ?? "", settings["PublicKeyPem"] ?? "", keys.Length == 0 ? null : keys);
        var sandbox = environment.IsDevelopment() && configuration.GetValue<bool>("SaleorApp:AllowLocalSandboxHttp");
        if (string.IsNullOrWhiteSpace(value.TenantId) || (keys.Length == 0 &&
            (string.IsNullOrWhiteSpace(value.KeyId) || string.IsNullOrWhiteSpace(value.PublicKeyPem)))) return null;
        if (!SaleorReviewEndpoint.IsApprovedApiUrl(value.ApiUrl) && !(sandbox && value.ApiUrl == "http://localhost:18080/graphql/")) return null;
        if (value.TransportUrl != value.ApiUrl && !(sandbox && value.TransportUrl == "http://api:8000/graphql/")) return null;
        foreach (var address in new[] { value.PublicBaseUrl, value.CallbackBaseUrl, value.DashboardOrigin })
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                uri.AbsolutePath != "/" || (uri.Scheme != "https" && !(sandbox && address is "http://localhost:18081" or "http://gtt.sandbox.test:8080" or "http://localhost:19090"))) return null;
        }
        return value;
    }

    public object Manifest(SaleorAppRegistration registration) => new
    {
        id = Identifier, name = "GlobalTradeTax Review", version = "0.2.0", requiredSaleorVersion = ">=3.23 <4",
        about = "Signed checkout evidence for manual customs review. No payment or tax calculation.",
        permissions = new[] { "MANAGE_CHECKOUTS" },
        appUrl = registration.PublicBaseUrl + "/saleor/" + registration.Id + "/app",
        tokenTargetUrl = registration.CallbackBaseUrl + "/saleor/" + registration.Id + "/register",
        webhooks = new[] { new
        {
            name = "GlobalTradeTax checkout review", isActive = true, identifier = "checkout-review",
            targetUrl = registration.CallbackBaseUrl + "/saleor/" + registration.Id + "/events",
            asyncEvents = new[] { "CHECKOUT_CREATED", "CHECKOUT_UPDATED" }, syncEvents = Array.Empty<string>(), query = SaleorCheckoutEvent.Query
        }, new
        {
            name = "GlobalTradeTax App lifecycle", isActive = true, identifier = "app-lifecycle",
            targetUrl = registration.CallbackBaseUrl + "/saleor/" + registration.Id + "/events",
            asyncEvents = new[] { "APP_INSTALLED", "APP_UPDATED", "APP_STATUS_CHANGED", "APP_DELETED" },
            syncEvents = Array.Empty<string>(), query = SaleorAppLifecycle.Query
        } }
    };

    private static bool MatchesStore(HttpRequest request, SaleorAppRegistration registration) =>
        request.Headers.TryGetValue("Saleor-Api-Url", out var urls) && urls.Count == 1 && urls[0] == registration.ApiUrl;

    public async Task<IResult> RegisterAsync(HttpRequest request, SaleorAppRegistration registration, CancellationToken cancellationToken)
    {
        if (!MatchesStore(request, registration)) return Results.Unauthorized();
        var bytes = await ReadBodyAsync(request, 4096, cancellationToken);
        if (bytes is null) return Results.StatusCode(413);
        using var document = JsonDocument.Parse(bytes);
        SaleorReview.RejectDuplicateProperties(document.RootElement);
        var token = document.RootElement.GetProperty("auth_token").GetString();
        if (token is null || token.Length is < 16 or > 512 || token.Any(char.IsControl)) return Results.Unauthorized();
        using var identity = await QueryAsync(registration, token, "{ app { id identifier isActive permissions { code } } }", cancellationToken);
        if (identity is null || !identity.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("app", out var app) || app.ValueKind != JsonValueKind.Object ||
            app.GetProperty("identifier").GetString() != Identifier || !app.GetProperty("isActive").GetBoolean()) return Results.Unauthorized();
        var permissions = app.GetProperty("permissions").EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToArray();
        if (permissions.Length != 1 || permissions[0] != "MANAGE_CHECKOUTS") return Results.Unauthorized();
        var registered = await store.RegisterAsync(registration.TenantId, registration.ApiUrl, app.GetProperty("id").GetString()!, token, cancellationToken);
        return registered ? Results.Ok(new { registered = true, mode = "review-only" }) : Results.Conflict(new { error = "installation_already_bound" });
    }

    public async Task<IResult> ReceiveAsync(HttpRequest request, SaleorAppRegistration registration, CancellationToken cancellationToken)
    {
        if (!MatchesStore(request, registration) || !request.Headers.TryGetValue("Saleor-Signature", out var signatures) || signatures.Count != 1 ||
            !request.Headers.TryGetValue("Saleor-Event", out var events) || events.Count != 1) return Results.Unauthorized();
        var bytes = await ReadBodyAsync(request, SaleorReviewEndpoint.MaxBodyBytes, cancellationToken);
        if (bytes is null) return Results.StatusCode(413);
        if (!VerifySignature(bytes, signatures.ToString(), registration)) return Results.Unauthorized();
        var state = await store.ReadAsync(registration.TenantId, registration.ApiUrl, cancellationToken);
        if (state is null) return Results.Problem("App is not registered.", statusCode: 503);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        SaleorReview.RejectDuplicateProperties(document.RootElement);
        if (document.RootElement.GetProperty("recipient").GetProperty("id").GetString() != state.AppId) return Results.Unauthorized();
        var eventType = events.ToString();
        if (eventType is "app_installed" or "app_updated" or "app_status_changed" or "app_deleted")
        {
            var change = SaleorAppLifecycle.Analyze(document.RootElement, eventType, state.AppId, DateTimeOffset.UtcNow);
            if (change.Active && !change.Deleted && !state.Revoked && !await IsInstalledAsync(registration, state, cancellationToken))
                return Results.Problem("Active installation could not be verified.", statusCode: 503);
            return await store.ApplyLifecycleAsync(registration.TenantId, registration.ApiUrl, change, cancellationToken)
                ? Results.Ok(new { accepted = true, lifecycle = true }) : Results.Conflict();
        }
        if (!state.Active || state.Revoked) return Results.StatusCode(403);
        var review = SaleorCheckoutEvent.Analyze(bytes, eventType, registration.TenantId, registration.ApiUrl, DateTimeOffset.UtcNow);
        if (!await IsInstalledAsync(registration, state, cancellationToken)) return Results.Problem("Active installation could not be verified.", statusCode: 503);
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existing = state.Events.FirstOrDefault(entry => entry.Hash == digest);
        if (existing is not null) return Results.Ok(new { accepted = true, duplicate = true, reference = existing.Review.Reference });
        if (!await store.AppendAsync(registration.TenantId, registration.ApiUrl, new(digest, eventType, DateTimeOffset.UtcNow, review),
            cancellationToken, state.AppId, state.Token)) return Results.Conflict();
        return Results.Ok(new { accepted = true, duplicate = false, reference = review.Reference });
    }

    public async Task<IResult> ReviewsAsync(HttpRequest request, SaleorAppRegistration registration, CancellationToken cancellationToken)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var authorization) || authorization.Scheme != "Bearer" ||
            string.IsNullOrWhiteSpace(authorization.Parameter) || authorization.Parameter.Length > 8192) return Results.Unauthorized();
        using var identity = await QueryAsync(registration, authorization.Parameter, "{ me { isStaff } checkouts(first: 1) { totalCount } }", cancellationToken);
        if (identity is null || !identity.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("me", out var user) || user.ValueKind != JsonValueKind.Object || !user.GetProperty("isStaff").GetBoolean()) return Results.Unauthorized();
        var state = await store.ReadAsync(registration.TenantId, registration.ApiUrl, cancellationToken);
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        if (state is not null && (!state.Active || state.Revoked)) return Results.StatusCode(403);
        if (state is not null && !await IsInstalledAsync(registration, state, cancellationToken))
            return Results.Problem("Active installation could not be verified.", statusCode: 503);
        var current = await store.ReadAsync(registration.TenantId, registration.ApiUrl, cancellationToken);
        if (state is not null && (current is null || !current.Active || current.Revoked ||
            current.AppId != state.AppId || !SaleorAppStore.SecretEquals(current.Token, state.Token))) return Results.StatusCode(403);
        return Results.Ok(new { mode = "review-only", registered = state is not null, reviews = state?.Events ?? [] });
    }

    public static bool VerifySignature(byte[] body, string signature, SaleorAppRegistration registration) =>
        (registration.VerificationKeys ?? [new(registration.KeyId, registration.PublicKeyPem)])
            .Any(key => SaleorSignature.Verify(body, signature, key.KeyId, key.PublicKeyPem));

    private async Task<bool> IsInstalledAsync(SaleorAppRegistration registration, SaleorAppState state, CancellationToken cancellationToken)
    {
        using var identity = await QueryAsync(registration, state.Token, "{ app { id identifier isActive permissions { code } } }", cancellationToken);
        if (identity is null || !identity.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("app", out var app) || app.ValueKind != JsonValueKind.Object ||
            app.GetProperty("id").GetString() != state.AppId || app.GetProperty("identifier").GetString() != Identifier ||
            !app.GetProperty("isActive").GetBoolean()) return false;
        var permissions = app.GetProperty("permissions").EnumerateArray().Select(item => item.GetProperty("code").GetString()).ToArray();
        return permissions.Length == 1 && permissions[0] == "MANAGE_CHECKOUTS";
    }

    private async Task<JsonDocument?> QueryAsync(SaleorAppRegistration registration, string token, string query, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, registration.TransportUrl);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        message.Content = JsonContent.Create(new { query });
        using var response = await clients.CreateClient("SaleorApp").SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + count > 65_536) return null;
            buffer.Write(chunk, 0, count);
        }
        var result = JsonDocument.Parse(buffer.ToArray());
        if (result.RootElement.TryGetProperty("errors", out _)) { result.Dispose(); return null; }
        return result;
    }

    public static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int limit, CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType()) throw new FormatException("application_json_required");
        if (request.ContentLength > limit) return null;
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await request.Body.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + count > limit) return null;
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/saleor/{id}/manifest", (string id, SaleorApp service) =>
            service.Resolve(id) is { } registration ? Results.Ok(service.Manifest(registration)) : Results.NotFound());
        app.MapGet("/saleor/{id}/app", (string id, HttpContext context, SaleorApp service) =>
        {
            if (service.Resolve(id) is not { } registration) return Results.NotFound();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors " + registration.DashboardOrigin;
            var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SaleorApp.html"));
            return Results.Content(html.Replace("{{API_URL}}", HtmlEncoder.Default.Encode(registration.ApiUrl), StringComparison.Ordinal), "text/html");
        });
        app.MapGet("/saleor/app.js", () => Results.File(Path.Combine(AppContext.BaseDirectory, "SaleorApp.bundle.js"), "text/javascript"));
        app.MapGet("/saleor/app.css", () => Results.File(Path.Combine(AppContext.BaseDirectory, "SaleorApp.css"), "text/css"));
        app.MapGet("/saleor/notices.txt", () => Results.File(Path.Combine(AppContext.BaseDirectory, "SaleorApp.NOTICES.txt"), "text/plain"));
        app.MapPost("/saleor/{id}/register", (string id, HttpRequest request, SaleorApp service, CancellationToken cancellationToken) =>
            HandleAsync(id, request, service, "register", cancellationToken));
        app.MapPost("/saleor/{id}/events", (string id, HttpRequest request, SaleorApp service, CancellationToken cancellationToken) =>
            HandleAsync(id, request, service, "events", cancellationToken));
        app.MapGet("/saleor/{id}/reviews", (string id, HttpRequest request, SaleorApp service, CancellationToken cancellationToken) =>
            HandleAsync(id, request, service, "reviews", cancellationToken));
    }

    private static async Task<IResult> HandleAsync(string id, HttpRequest request, SaleorApp service, string action, CancellationToken cancellationToken)
    {
        if (service.Resolve(id) is not { } registration) return Results.NotFound();
        try
        {
            return action switch
            {
                "register" => await service.RegisterAsync(request, registration, cancellationToken),
                "events" => await service.ReceiveAsync(request, registration, cancellationToken),
                _ => await service.ReviewsAsync(request, registration, cancellationToken)
            };
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException or NullReferenceException)
        { return Results.BadRequest(new { error = "invalid_saleor_app_request" }); }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException or CryptographicException)
        { return Results.Problem("Saleor App dependency or protected state unavailable.", statusCode: 503); }
    }
}
