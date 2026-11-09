using System.Text.Json.Serialization;
using System.Text.Json;
using GlobalTradeTax.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.saleor.json", optional: true).AddEnvironmentVariables();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<QuoteService>();
builder.Services.AddMemoryCache(options => options.SizeLimit = 1000);
builder.Services.AddHttpClient<IImportRateProvider, ExternalImportRateProvider>(client => client.Timeout = TimeSpan.FromSeconds(8))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddSingleton<LandedCostService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TenantRateLimit>();
builder.Services.AddHttpClient(UkTariffAccessTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(8))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddSingleton<UkTariffAccessTokenProvider>();
builder.Services.AddHttpClient<UkTariffReferenceClient>(client => { client.Timeout = TimeSpan.FromSeconds(8); client.DefaultRequestHeaders.Accept.ParseAdd("application/json"); })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddSingleton<QuoteStore>();
builder.Services.AddSingleton<SaleorAppStore>();
builder.Services.AddSingleton<SaleorApp>();
builder.Services.AddHttpClient("SaleorApp", client => client.Timeout = TimeSpan.FromSeconds(8))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
builder.Services.AddScoped<UkImportAssessment>();
builder.Services.AddScoped<UkShipmentReview>();
var pgConnection = builder.Configuration.GetConnectionString("Postgres") ?? Environment.GetEnvironmentVariable("TRADE_POSTGRES_CONNECTION");
if (!string.IsNullOrWhiteSpace(pgConnection))
{
    builder.Services.AddNpgsqlDataSource(pgConnection);
    builder.Services.AddScoped<PostgresQuoteStore>();
    builder.Services.AddScoped<TenantAuth>();
    builder.Services.AddScoped<AuditStore>();
    builder.Services.AddScoped<TariffStagingStore>();
}
var app = builder.Build();
_ = app.Services.GetRequiredService<TenantRateLimit>();
app.UseRouting();
app.Use(async (context, next) =>
{
    var route = (context.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText;
    if (route is not null && ((route.StartsWith("/v1/references/uk/", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/v1/references/national/", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/v1/references/jp-kr-sg/", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/v1/references/north-america/", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/v1/references/gcc/", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/v1/references/latam/", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/v1/references/eu/", StringComparison.OrdinalIgnoreCase))
        || route.StartsWith("/v1/quotes", StringComparison.OrdinalIgnoreCase)
        || route.StartsWith("/v1/destinations/", StringComparison.OrdinalIgnoreCase)
        || route.Equals("/v1/landed-cost/estimate", StringComparison.OrdinalIgnoreCase)
        || route.Equals("/v1/compliance/check", StringComparison.OrdinalIgnoreCase)
        || route.Equals("/v1/tariff-datasets/validate", StringComparison.OrdinalIgnoreCase)
        || route.Equals("/v1/integrations/saleor/reviews", StringComparison.OrdinalIgnoreCase)))
    {
        var tenant = await AuthenticateAsync(context.Request, context.RequestServices, app.Configuration, context.RequestAborted);
        if (tenant is null) { context.Response.StatusCode = 401; return; }
        context.Items["AuthenticatedTenant"] = tenant;
        await context.RequestServices.GetRequiredService<TenantRateLimit>().InvokeAsync(context, next, tenant);
        return;
    }
    await next(context);
});
SaleorApp.Map(app);
app.MapGet("/health", () => Results.Ok(new { status = "healthy", version = "2.3.0" }));
app.MapPost("/v1/integrations/saleor/reviews", async (HttpRequest request, IServiceProvider services,
    IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var tenant = await AuthenticateAsync(request, services, configuration, cancellationToken);
    return tenant is null ? Results.Unauthorized()
        : await SaleorReviewEndpoint.HandleAsync(request, tenant, configuration, cancellationToken);
});
app.MapPost("/v1/quotes", async (HttpRequest http, QuoteService service, QuoteStore store, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    var tenant = await AuthenticateAsync(http, services, config, ct);
    if (tenant is null) return Results.Unauthorized();
    if (!http.Headers.TryGetValue("Idempotency-Key", out var key) || string.IsNullOrWhiteSpace(key.ToString()) || key.ToString().Length > 128)
        return Results.BadRequest(new { error = "Idempotency-Key header required (1-128 characters)." });
    var (request,bodyError) = await ReadQuoteRequestAsync(http,ct);
    if (request is null) return bodyError!;
    var errors = QuoteService.Validate(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var (quote, error) = services.GetService<PostgresQuoteStore>() is { } postgres
        ? await postgres.CreateAsync(tenant, key.ToString(), request, service, ct)
        : store.Create(tenant, key.ToString(), request, service);
    return error is not null ? Results.Conflict(new { error }) : Results.Ok(quote);
});
// Standalone landed-cost endpoint: no monetary total without verified provider evidence.
app.MapPost("/v1/landed-cost/estimate", async (HttpRequest http,
    LandedCostService calculator, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    var tenant = await AuthenticateAsync(http, services, config, ct);
    if (tenant is null) return Results.Unauthorized();
    var (request,bodyError) = await ReadQuoteRequestAsync(http,ct);
    if (request is null) return bodyError!;
    var errors = QuoteService.Validate(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var result = await calculator.CalculateAsync(request, ct);
    if (services.GetService<AuditStore>() is { } audit)
        await audit.WriteAsync(tenant, "landed_cost_estimate", result.Status,
            new { request.ShipFrom, request.ShipTo, request.Currency, result.Total, result.Evidence,
                specialistHoldRequired = result.Compliance!.SpecialistHoldRequired, holdReasons = result.Compliance.HoldReasons,
                shipmentReleaseApproved = result.Compliance.ShipmentReleaseApproved }, ct);
    return Results.Ok(result);
});
app.MapGet("/v1/quotes/{id}", async (string id, HttpRequest http, QuoteStore store, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    var tenant = await AuthenticateAsync(http, services, config, ct);
    if (tenant is null) return Results.Unauthorized();
    var quote = services.GetService<PostgresQuoteStore>() is { } postgres
        ? await postgres.FindAsync(tenant, id, ct)
        : store.Find(tenant, id);
    return quote is null ? Results.NotFound() : Results.Ok(quote);
});
app.MapGet("/v1/destinations/{country}", async (string country, HttpRequest http, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    if (country.Length != 2 || !country.All(char.IsLetter)) return Results.BadRequest(new { error = "ISO alpha-2 country required" });
    return Results.Ok(DestinationPolicies.Resolve(country));
});
app.MapGet("/v1/references/jp-kr-sg/{country}", (string country) =>
{
    var reference = JpKrSgDestinationReference.Find(country);
    return reference is null ? Results.BadRequest(new { error = "supported JP, KR or SG ISO alpha-2 country required" }) : Results.Ok(reference);
});
app.MapGet("/v1/references/north-america/{country}", (string country) =>
{
    var reference = NorthAmericaDestinationReference.Find(country);
    return reference is null ? Results.BadRequest(new { error = "supported US, CA or MX ISO alpha-2 country required" }) : Results.Ok(reference);
});

app.MapGet("/v1/references/gcc/{country}", (string country) =>
{
    var reference = GccDestinationReference.Find(country);
    return reference is null ? Results.BadRequest(new { error = "supported GCC ISO alpha-2 country required" }) : Results.Ok(reference);
});
app.MapGet("/v1/references/latam/{country}", (string country) =>
{
    var reference = LatamDestinationReference.Find(country);
    return reference is null ? Results.BadRequest(new { error = "supported LATAM reference ISO alpha-2 country required" }) : Results.Ok(reference);
});
app.MapGet("/v1/references/national/{country}", (string country) =>
{
    if (country.Length != 2 || !country.All(char.IsAsciiLetter)) return Results.BadRequest(new { error = "Two ASCII country letters required" });
    var reference = IndianChineseDestinationReference.Find(country);
    return reference is null ? Results.NotFound() : Results.Ok(reference);
});
// Source guidance only: no tariff, VAT or territorial eligibility is inferred.
app.MapGet("/v1/references/eu/destinations/{country}", async (string country, HttpRequest http, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    if (country.Length != 2 || !country.All(char.IsAsciiLetter)) return Results.BadRequest(new { error = "Two ASCII country letters required" });
    var reference = EuDestinationReferences.Find(country);
    return reference is null ? Results.NotFound() : Results.Ok(reference);
});
app.MapPost("/v1/compliance/check", async (HttpRequest http, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    var (request,bodyError) = await ReadQuoteRequestAsync(http,ct);
    if (request is null) return bodyError!;
    var errors = QuoteService.Validate(request);
    return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(ShipmentCompliance.Evaluate(request));
});
// Offline validation endpoint: accepts curated CSV text; never writes rates to production or issues payable quotes.
app.MapPost("/v1/tariff-datasets/validate", async (HttpRequest http, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    if (http.ContentLength is > 1_048_576) return Results.StatusCode(413);
    var result = await TariffDataset.ReadAsync(http.Body, ct);
    if (result.Status == "too_large") return Results.StatusCode(413);
    return result.Status == "rejected"
        ? Results.BadRequest(new { result.Status, result.Error, result.Sha256 })
        : Results.Ok(new { result.Status, result.Rows, result.Sha256,
            warning = "Reference data only. No tariff rates are activated and no payable landed-cost quote is generated." });
});
// Operator-only staging: independent of merchant API keys. Never enables customs calculations.
app.MapPost("/v1/admin/tariff-datasets/stage", async (HttpRequest http, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (!IsOperator(http, config)) return Results.Unauthorized();
    var staging = services.GetService<TariffStagingStore>();
    if (staging is null) return Results.Problem("PostgreSQL required", statusCode: 503);
    if (http.ContentLength is > 1_048_576) return Results.StatusCode(413);
    var parsed = await TariffDataset.ReadAsync(http.Body, ct);
    if (parsed.Status == "too_large") return Results.StatusCode(413);
    if (parsed.Status == "rejected") return Results.BadRequest(new { parsed.Error, parsed.Sha256 });
    if (parsed.Records.Select(x => x.SourceUrl).Distinct().Count() != 1 ||
        parsed.Records.Select(x => x.SourceVersion).Distinct().Count() != 1)
        return Results.BadRequest(new { error = "A batch must have one source URL and version." });
    var staged = await staging.StageAsync(parsed, ct);
    if (staged.Error is not null) return Results.Conflict(new { error = staged.Error });
    return Results.Ok(new { batchId = staged.BatchId, alreadyExists = staged.AlreadyExists, parsed.Rows, parsed.Sha256,
        status = "staged_reference_only", warning = "Not connected to payable landed-cost calculation." });
});
app.MapGet("/v1/admin/tariff-datasets/{id:guid}", async (Guid id, HttpRequest http,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (!IsOperator(http, config)) return Results.Unauthorized();
    var staging = services.GetService<TariffStagingStore>();
    if (staging is null) return Results.Problem("PostgreSQL required", statusCode: 503);
    var batch = await staging.GetBatchAsync(id, ct);
    return batch is null ? Results.NotFound() : Results.Ok(batch);
});
// Append operator attestations only; a review never activates a payable rate.
app.MapPost("/v1/admin/tariff-datasets/{id:guid}/reviews", async (Guid id, HttpRequest http,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (!IsOperator(http, config)) return Results.Unauthorized();
    var staging = services.GetService<TariffStagingStore>();
    if (staging is null) return Results.Problem("PostgreSQL required", statusCode: 503);
    if (http.ContentLength is > 32_768) return Results.StatusCode(413);
    var bytes = await TariffDataset.ReadBytesAsync(http.Body, 32_768, ct);
    if (bytes is null) return Results.StatusCode(413);
    TariffReviewRequest? review;
    try { review = JsonSerializer.Deserialize<TariffReviewRequest>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
    catch (JsonException) { return Results.BadRequest(new { error = "invalid_review_json" }); }
    if (review is null) return Results.BadRequest(new { error = "review_required" });
    var result = await staging.ReviewAsync(id, review, ct);
    return result.Status switch
    {
        "not_found" => Results.NotFound(),
        "invalid_request" => Results.BadRequest(result),
        "conflict" => Results.Conflict(result),
        _ => Results.Ok(result)
    };
});
app.MapGet("/v1/admin/tariff-datasets/{id:guid}/reviews", async (Guid id, int? afterRevision, int? limit,
    HttpRequest http, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (!IsOperator(http, config)) return Results.Unauthorized();
    if (afterRevision is < 0 || limit is < 1 or > 100) return Results.BadRequest(new { error = "invalid_review_page" });
    var staging = services.GetService<TariffStagingStore>();
    if (staging is null) return Results.Problem("PostgreSQL required", statusCode: 503);
    var page = await staging.GetReviewsAsync(id, afterRevision ?? 0, limit ?? 50, ct);
    return page is null ? Results.NotFound() : Results.Ok(page);
});
// Read-only UK commodity lookup. Requires merchant authentication; never returns payable amounts.
app.MapGet("/v1/references/uk/commodities/{code}", async (string code, string? territory, HttpRequest http,
    UkTariffReferenceClient uk, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    var result = await uk.GetCommodityAsync(code, null, territory ?? "GB", ct);
    return result.Status switch
    {
        "invalid_code" or "invalid_territory" => Results.BadRequest(result),
        "not_found" => Results.NotFound(result),
        "not_configured" => Results.Problem(result.Warning, statusCode: 503),
        "upstream_error" => Results.Problem(result.Warning, statusCode: 502),
        _ => Results.Ok(result)
    };
});
// Normalized, read-only UK reference; never feeds duty/VAT calculations.
app.MapGet("/v1/references/uk/commodities/{code}/normalized", async (string code, string? territory, HttpRequest http,
    UkTariffReferenceClient uk, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    var result = await uk.GetCommodityAsync(code, null, territory ?? "GB", ct);
    var normalized = UkTariffNormalizer.Normalize(code, result);
    return result.Status switch
    {
        "invalid_code" or "invalid_territory" => Results.BadRequest(normalized),
        "not_found" => Results.NotFound(normalized),
        "not_configured" => Results.Problem(result.Warning, statusCode: 503),
        "upstream_error" => Results.Problem(result.Warning, statusCode: 502),
        _ => Results.Ok(normalized)
    };
});
// Conservative UK measure screening: reference only, never a payable duty quote.
app.MapGet("/v1/references/uk/commodities/{code}/screen", async (string code, string origin,
    DateOnly importDate, string? territory, HttpRequest http, UkTariffReferenceClient uk,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    if (origin is null || origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z'))
        return Results.BadRequest(new { error = "origin must be uppercase ISO alpha-2" });
    var result = await uk.GetCommodityAsync(code, importDate, territory ?? "GB", ct);
    if (result.Status is "invalid_code" or "invalid_import_date" or "invalid_territory") return Results.BadRequest(result);
    if (result.Status == "not_found") return Results.NotFound(result);
    if (result.Status == "not_configured") return Results.Problem(result.Warning, statusCode: 503);
    if (result.Status == "upstream_error") return Results.Problem(result.Warning, statusCode: 502);
    return Results.Ok(UkMeasureScreening.Screen(result, code, origin, importDate));
});
// Detailed evidence-only measure inspection; never computes payable amounts.
app.MapGet("/v1/references/uk/commodities/{code}/evidence", async (string code, string origin,
    DateOnly importDate, string? territory, HttpRequest http, UkTariffReferenceClient uk,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    if (origin is null || origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z'))
        return Results.BadRequest(new { error = "origin must be uppercase ISO alpha-2" });
    var result = await uk.GetCommodityAsync(code, importDate, territory ?? "GB", ct);
    if (result.Status is "invalid_code" or "invalid_import_date" or "invalid_territory") return Results.BadRequest(result);
    if (result.Status == "not_found") return Results.NotFound(result);
    if (result.Status == "not_configured") return Results.Problem(result.Warning, statusCode: 503);
    if (result.Status == "upstream_error") return Results.Problem(result.Warning, statusCode: 502);
    return Results.Ok(UkMeasureEvidence.Analyze(result, code, origin, importDate));
});
// Geographical evidence analysis: deliberately non-payable and fail-closed.
app.MapGet("/v1/references/uk/commodities/{code}/geography", async (string code, string origin,
    DateOnly importDate, string? territory, HttpRequest http, UkTariffReferenceClient uk,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    if (origin is null || origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z'))
        return Results.BadRequest(new { error = "origin must be uppercase ISO alpha-2" });
    var result = await uk.GetCommodityAsync(code, importDate, territory ?? "GB", ct);
    if (result.Status is "invalid_code" or "invalid_import_date" or "invalid_territory") return Results.BadRequest(result);
    if (result.Status == "not_found") return Results.NotFound(result);
    if (result.Status == "not_configured") return Results.Problem(result.Warning, statusCode: 503);
    if (result.Status == "upstream_error") return Results.Problem(result.Warning, statusCode: 502);
    return Results.Ok(UkGeographyResolver.Analyze(result, code, origin, importDate));
});
// UK geography reference endpoint; read-only and deliberately non-payable.
app.MapGet("/v1/references/uk/geographical-areas/{id}", async (string id, string? territory, HttpRequest http,
    UkTariffReferenceClient uk, IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    var result = await uk.GetGeographicalAreaAsync(id, null, territory ?? "GB", ct);
    return result.Status switch
    {
        "invalid_code" or "invalid_territory" => Results.BadRequest(result),
        "not_found" => Results.NotFound(result),
        "not_configured" => Results.Problem(result.Warning, statusCode: 503),
        "upstream_error" => Results.Problem(result.Warning, statusCode: 502),
        _ => Results.Ok(result)
    };
});
// Dated area-membership evidence, never a legal tariff determination.
app.MapGet("/v1/references/uk/geographical-areas/{id}/membership", async (string id, string origin,
    DateOnly importDate, string? territory, HttpRequest http, UkTariffReferenceClient uk,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    if (origin is null || origin.Length != 2 || !origin.All(c => c is >= 'A' and <= 'Z'))
        return Results.BadRequest(new { error = "origin must be uppercase ISO alpha-2" });
    var result = await uk.GetGeographicalAreaAsync(id, importDate, territory ?? "GB", ct);
    if (result.Status is "invalid_code" or "invalid_import_date" or "invalid_territory") return Results.BadRequest(result);
    if (result.Status == "not_found") return Results.NotFound(result);
    if (result.Status == "not_configured") return Results.Problem(result.Warning, statusCode: 503);
    if (result.Status == "upstream_error") return Results.Problem(result.Warning, statusCode: 502);
    return Results.Ok(UkAreaMembership.Inspect(result, id, origin, importDate));
});
// Orchestrated UK assessment: evidence-only, not a payable customs quote.
app.MapGet("/v1/references/uk/assessments/{code}", async (string code, string origin,
    DateOnly importDate, string? territory, HttpRequest http, UkImportAssessment assessment,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    var result = await assessment.AssessAsync(code, origin, importDate, territory ?? "GB", ct);
    return result.Status switch
    {
        "invalid_origin" or "invalid_code" or "invalid_import_date" or "invalid_territory" => Results.BadRequest(result),
        "not_found" => Results.NotFound(result),
        "not_configured" => Results.Problem("UK tariff API is not configured", statusCode: 503),
        "upstream_error" => Results.Problem("UK tariff API lookup failed", statusCode: 502),
        _ => Results.Ok(result)
    };
});
// Reproducible, hash-addressed evidence for downstream audit systems. Not a payable quote.
app.MapGet("/v1/references/uk/assessments/{code}/evidence-package", async (string code, string origin,
    DateOnly importDate, string? territory, HttpRequest http, UkImportAssessment assessment,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    var result = await assessment.AssessAsync(code, origin, importDate, territory ?? "GB", ct);
    return result.Status switch
    {
        "invalid_origin" or "invalid_code" or "invalid_import_date" or "invalid_territory" => Results.BadRequest(result),
        "not_found" => Results.NotFound(result),
        "not_configured" => Results.Problem("UK tariff API is not configured", statusCode: 503),
        "upstream_error" => Results.Problem("UK tariff API lookup failed", statusCode: 502),
        _ => Results.Ok(UkAssessmentEvidencePackage.Build(result, DateTimeOffset.UtcNow))
    };
});
// UK VAT review joins classification and amount readiness without legal rate inference.
app.MapPost("/v1/references/uk/shipments/review", async (HttpRequest http, UkShipmentReview review,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    var tenant = await AuthenticateAsync(http, services, config, ct);
    if (tenant is null) return Results.Unauthorized();
    if (http.ContentLength is > 262_144) return Results.StatusCode(413);
    using var body = new StreamContent(http.Body);
    var bytes = await UkTariffHttp.ReadReferenceBytesAsync(body, 262_144, ct);
    if (bytes is null) return Results.StatusCode(413);
    UkShipmentReviewRequest? request;
    try { request = ExactJsonInput.Deserialize<UkShipmentReviewRequest>(bytes); }
    catch (JsonException) { return Results.BadRequest(new { error = "invalid_or_inexact_review_json" }); }
    if (request is null) return Results.BadRequest(new { error = "shipment_review_required" });
    var result = await review.ReviewAsync(request, ct);
    if (services.GetService<AuditStore>() is { } audit)
        await audit.WriteAsync(tenant, "uk_shipment_review", result.Status,
            new { evidenceSha256 = result.EvidenceSha256, destinationTerritory = result.DestinationTerritory,
                importDate = result.ImportDate, approvedForPayment = result.ApprovedForPayment }, ct);
    return result.Status == "invalid_input" ? Results.BadRequest(result) : Results.Ok(result);
});
app.MapPost("/v1/references/uk/vat/review", async (HttpRequest http,
    IServiceProvider services, IConfiguration config, CancellationToken ct) =>
{
    if (await AuthenticateAsync(http, services, config, ct) is null) return Results.Unauthorized();
    if (http.ContentLength is > 65_536) return Results.StatusCode(413);
    using var body = new StreamContent(http.Body);
    var bytes = await UkTariffHttp.ReadReferenceBytesAsync(body,65_536,ct);
    if (bytes is null) return Results.StatusCode(413);
    UkVatQuoteReviewRequest? request;
    try { request = ExactJsonInput.Deserialize<UkVatQuoteReviewRequest>(bytes); }
    catch (JsonException) { return Results.BadRequest(new { error = "invalid_or_inexact_vat_review_json" }); }
    if (request?.Classification is null || request.Amounts is null)
        return Results.BadRequest(new { error = "classification_and_amounts_required" });
    var result = UkVatQuoteReview.Evaluate(request);
    return result.Status == "invalid_input" ? Results.BadRequest(result) : Results.Ok(result);
});
app.MapGet("/v1/data-sources", () => Results.Ok(new { status = "reference_registry_only", sources = TradeDataCatalog.Sources }));
app.MapGet("/v1/data-sources/{id}", (string id) =>
{
    var source = TradeDataCatalog.Find(id);
    return source is null ? Results.NotFound() : Results.Ok(source);
});
app.MapGet("/v1/capabilities", () => Results.Ok(new {
    routes = new[] { "US->Worldwide", "Worldwide->US" },
    calculation = "provider-required", destinationPolicies = "EU/GCC/LATAM/OTHER", settlement = "not-implemented", landedCost = "external-provider-adapter-with-evidence-validation",
    persistence = string.IsNullOrWhiteSpace(pgConnection) ? "in-memory-development-only" : "postgresql",
    tenantAuthentication = string.IsNullOrWhiteSpace(pgConnection) ? "development-key" : "hashed-postgres-api-keys",
    saleorIntegration = "review-only-pilot-no-tax-response",
    disclaimer = "No authoritative tariff dataset configured. Quotes are incomplete."
}));
app.Run();

// Called only after authentication; quote creation also validates its idempotency header.
// The byte limit also covers chunked bodies; exact JSON rejects duplicate control flags.
static async Task<(QuoteRequest? Request,IResult? Error)> ReadQuoteRequestAsync(HttpRequest http,CancellationToken ct)
{
    if (http.ContentLength is > 65_536) return (null,Results.StatusCode(413));
    using var body = new StreamContent(http.Body);
    var bytes = await UkTariffHttp.ReadReferenceBytesAsync(body,65_536,ct);
    if (bytes is null) return (null,Results.StatusCode(413));
    try
    {
        var request = ExactJsonInput.Deserialize<QuoteRequest>(bytes);
        return request is null ? (null,Results.BadRequest(new { error = "quote_request_required" })) : (request,null);
    }
    catch (JsonException) { return (null,Results.BadRequest(new { error = "invalid_or_inexact_quote_json" })); }
}

static async Task<string?> AuthenticateAsync(HttpRequest request, IServiceProvider services, IConfiguration config, CancellationToken ct)
{
    if (request.HttpContext.Items.TryGetValue("AuthenticatedTenant", out var cached)) return cached as string;
    if (services.GetService<TenantAuth>() is { } auth) return await auth.AuthenticateAsync(request, ct);
    // Explicitly development-only; never enable fallback when PostgreSQL is configured.
    if (!string.Equals(config["ASPNETCORE_ENVIRONMENT"], "Development", StringComparison.OrdinalIgnoreCase)) return null;
    var expected = config["TRADE_API_KEY"];
    var tenant = config["TRADE_TENANT_ID"];
    if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(tenant)) return null;
    if (!request.Headers.TryGetValue("X-API-Key", out var supplied)) return null;
    var a = System.Text.Encoding.UTF8.GetBytes(expected);
    var b = System.Text.Encoding.UTF8.GetBytes(supplied.ToString());
    return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b) ? tenant : null;
}
static bool IsOperator(HttpRequest request, IConfiguration config)
{
    var expected = config["TRADE_OPERATOR_KEY"];
    if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32 ||
        !request.Headers.TryGetValue("X-Operator-Key", out var supplied)) return false;
    var a = System.Text.Encoding.UTF8.GetBytes(expected);
    var b = System.Text.Encoding.UTF8.GetBytes(supplied.ToString());
    return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
}

public partial class Program { }
