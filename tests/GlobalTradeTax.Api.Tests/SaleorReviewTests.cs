using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GlobalTradeTax.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class SaleorReviewTests
{
    private const string ApiUrl = "https://shop.example/graphql/";
    private const string Tenant = "merchant-a";
    private const string KeyId = "sandbox-key";

    private static byte[] Payload(DateTimeOffset? issuedAt = null)
    {
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            __typename = "CalculateTaxes", issuedAt = issuedAt ?? DateTimeOffset.UtcNow, version = "3.x",
            taxBase = new
            {
                sourceObject = new { __typename = "Checkout", id = "checkout-1" },
                channel = new { slug = "uk" }, currency = "GBP", pricesEnteredWithTax = false,
                shippingPrice = new { amount = 5.25m }, address = new { country = new { code = "GB" } },
                lines = new[]
                {
                    new { sourceLine = new { id = "line-1", variant = new { sku = "sample-sku", product = new { hsCode = "6109100000", countryOfOrigin = "CN" } } },
                        quantity = 2, unitPrice = new { amount = 12.50m }, totalPrice = new { amount = 25m } }
                }
            }
        });
    }

    private static byte[] Mutate(byte[] body, Action<JsonObject> change)
    {
        var root = JsonNode.Parse(body)!.AsObject();
        change(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Sign(RSA rsa, byte[] body, string? headerJson = null)
    {
        var header = Encode(Encoding.UTF8.GetBytes(headerJson ?? """{"alg":"RS256","kid":"sandbox-key","b64":false,"crit":["b64"]}"""));
        var signedBytes = Encoding.ASCII.GetBytes(header + ".").Concat(body).ToArray();
        return header + ".." + Encode(rsa.SignData(signedBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static IConfiguration Configuration(RSA rsa, bool enabled = true, string tenant = Tenant)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Saleor:ReviewEnabled"] = enabled.ToString(), ["Saleor:Stores:0:Enabled"] = "true",
            ["Saleor:Stores:0:ApiUrl"] = ApiUrl, ["Saleor:Stores:0:TenantId"] = tenant,
            ["Saleor:Stores:0:KeyId"] = KeyId, ["Saleor:Stores:0:PublicKeyPem"] = rsa.ExportSubjectPublicKeyInfoPem()
        }).Build();
    }

    private static DefaultHttpContext Context(byte[] body, string signature, string apiUrl = ApiUrl, bool knownLength = true)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = knownLength ? body.Length : null;
        context.Request.Body = new MemoryStream(body);
        context.Request.Headers["Saleor-Api-Url"] = apiUrl;
        context.Request.Headers["Saleor-Signature"] = signature;
        return context;
    }

    private static int Status(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode!.Value;

    [Fact]
    public void ReviewPreservesSourceValuesWithoutAuthorizingPayment()
    {
        var review = SaleorReview.Analyze(Payload(), Tenant, ApiUrl, DateTimeOffset.UtcNow);
        Assert.Equal("requires_review", review.Status);
        Assert.Equal("GBP", review.Currency);
        Assert.Equal("GB", review.DestinationCountry);
        var line = Assert.Single(review.Lines);
        Assert.Equal("6109100000", line.HsCode);
        Assert.Equal("CN", line.CountryOfOrigin);
        Assert.Equal(25m, line.SourceTotalAmount);
        Assert.Equal(5.25m, review.SourceShippingAmount);
        Assert.False(review.ApprovedForPayment);
        Assert.Null(review.ImportDuty);
        Assert.Null(review.ImportVat);
        Assert.Null(review.ClearanceFee);
        Assert.Null(review.LandedTotal);
        Assert.Contains("classification_and_origin_unverified:line-1", review.UnresolvedChecks);
        Assert.Equal(review.SourceIssuedAt.AddMinutes(5), review.ExpiresAt);
        var response = JsonSerializer.SerializeToElement(review, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(response.TryGetProperty("shipping_tax_rate", out _));
        Assert.False(response.GetProperty("lines")[0].TryGetProperty("tax_rate", out _));
    }

    [Fact]
    public void RetriesHaveStableReferenceAndTenantStoreOrPayloadChangesHaveDifferentReferences()
    {
        var body = Payload();
        var now = DateTimeOffset.UtcNow;
        var original = SaleorReview.Analyze(body, Tenant, ApiUrl, now);
        Assert.Equal(original, SaleorReview.Analyze(body, Tenant, ApiUrl, now) with { Lines = original.Lines, UnresolvedChecks = original.UnresolvedChecks });
        Assert.Equal(original.Reference, SaleorReview.Analyze(body, Tenant, ApiUrl, now.AddSeconds(5)).Reference);
        Assert.NotEqual(original.Reference, SaleorReview.Analyze(body, "merchant-b", ApiUrl, now).Reference);
        Assert.NotEqual(original.Reference, SaleorReview.Analyze(body, Tenant, "https://other.example/graphql/", now).Reference);
        var changed = Mutate(body, root => root["taxBase"]!["sourceObject"]!["id"] = "checkout-2");
        Assert.NotEqual(original.Reference, SaleorReview.Analyze(changed, Tenant, ApiUrl, now).Reference);
    }

    [Fact]
    public void MissingCustomsMetadataAndDestinationRemainUnresolved()
    {
        var body = Mutate(Payload(), root =>
        {
            root["taxBase"]!["address"] = null;
            root["taxBase"]!["lines"]![0]!["sourceLine"]!["variant"] = null;
        });
        var result = SaleorReview.Analyze(body, Tenant, ApiUrl, DateTimeOffset.UtcNow);
        Assert.Contains("destination_country_missing", result.UnresolvedChecks);
        Assert.Contains("hs_code_missing_or_invalid:line-1", result.UnresolvedChecks);
        Assert.Contains("origin_missing_or_invalid:line-1", result.UnresolvedChecks);
        Assert.False(result.ApprovedForPayment);
    }

    [Theory]
    [InlineData("Checkout", "checkoutProductVariant")]
    [InlineData("Order", "orderProductVariant")]
    public void SaleorSchemaCompatibleVariantAliasesPreserveCustomsMetadata(string sourceType, string variantAlias)
    {
        var body = Mutate(Payload(), root =>
        {
            root["taxBase"]!["sourceObject"]!["__typename"] = sourceType;
            var sourceLine = root["taxBase"]!["lines"]![0]!["sourceLine"]!.AsObject();
            var variant = sourceLine["variant"]!.DeepClone();
            sourceLine.Remove("variant");
            sourceLine[variantAlias] = variant;
        });
        var review = SaleorReview.Analyze(body, Tenant, ApiUrl, DateTimeOffset.UtcNow);
        var line = Assert.Single(review.Lines);
        Assert.Equal("sample-sku", line.Sku);
        Assert.Equal("6109100000", line.HsCode);
        Assert.Equal("CN", line.CountryOfOrigin);
        Assert.False(review.ApprovedForPayment);
    }

    [Fact]
    public void AmbiguousLegacyAndTypedVariantFieldsAreRejected()
    {
        var body = Mutate(Payload(), root =>
        {
            var sourceLine = root["taxBase"]!["lines"]![0]!["sourceLine"]!.AsObject();
            sourceLine["checkoutProductVariant"] = sourceLine["variant"]!.DeepClone();
        });
        Assert.Throws<FormatException>(() => SaleorReview.Analyze(body, Tenant, ApiUrl, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(61)]
    public void OldAndFuturePayloadsAreRejected(int offsetSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<FormatException>(() => SaleorReview.Analyze(Payload(now.AddSeconds(offsetSeconds)), Tenant, ApiUrl, now));
    }

    [Theory]
    [InlineData("currency")]
    [InlineData("quantity")]
    [InlineData("amount")]
    [InlineData("duplicate-line")]
    [InlineData("empty-lines")]
    [InlineData("type")]
    public void InvalidPayloadsAreRejected(string scenario)
    {
        var body = Mutate(Payload(), root =>
        {
            var taxBase = root["taxBase"]!;
            if (scenario == "currency") taxBase["currency"] = "gbp";
            if (scenario == "quantity") taxBase["lines"]![0]!["quantity"] = 0;
            if (scenario == "amount") taxBase["shippingPrice"]!["amount"] = -1;
            if (scenario == "duplicate-line") taxBase["lines"]!.AsArray().Add(taxBase["lines"]![0]!.DeepClone());
            if (scenario == "empty-lines") taxBase["lines"] = new JsonArray();
            if (scenario == "type") root["__typename"] = "CheckoutUpdated";
        });
        Assert.Throws<FormatException>(() => SaleorReview.Analyze(body, Tenant, ApiUrl, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MultipleLinesKeepTheirIdentityOrderAndNoPayableAmounts()
    {
        var body = Mutate(Payload(), root =>
        {
            var lines = root["taxBase"]!["lines"]!.AsArray();
            var second = lines[0]!.DeepClone();
            second["sourceLine"]!["id"] = "line-2";
            lines.Add(second);
        });
        var review = SaleorReview.Analyze(body, Tenant, ApiUrl, DateTimeOffset.UtcNow);
        Assert.Equal(new[] { "line-1", "line-2" }, review.Lines.Select(line => line.SourceLineId));
        Assert.Null(review.LandedTotal);
    }

    [Fact]
    public void DuplicateJsonPropertiesAreRejected()
    {
        var body = Encoding.UTF8.GetString(Payload()).Replace("\"currency\":\"GBP\"", "\"currency\":\"GBP\",\"currency\":\"USD\"", StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => SaleorReview.Analyze(Encoding.UTF8.GetBytes(body), Tenant, ApiUrl, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DetachedSignatureVerifiesExactRawBytesAndRejectsTamperingOrWrongKey()
    {
        using var rsa = RSA.Create(2048);
        using var other = RSA.Create(2048);
        var body = Payload();
        var signature = Sign(rsa, body);
        Assert.True(SaleorSignature.Verify(body, signature, KeyId, rsa.ExportSubjectPublicKeyInfoPem()));
        Assert.False(SaleorSignature.Verify(body.Concat(new byte[] { 32 }).ToArray(), signature, KeyId, rsa.ExportSubjectPublicKeyInfoPem()));
        Assert.False(SaleorSignature.Verify(body, signature, KeyId, other.ExportSubjectPublicKeyInfoPem()));
        Assert.False(SaleorSignature.Verify(body, signature, "different-key", rsa.ExportSubjectPublicKeyInfoPem()));
    }

    [Theory]
    [InlineData("{\"alg\":\"HS256\",\"kid\":\"sandbox-key\",\"b64\":false,\"crit\":[\"b64\"]}")]
    [InlineData("{\"alg\":\"none\",\"kid\":\"sandbox-key\",\"b64\":false,\"crit\":[\"b64\"]}")]
    [InlineData("{\"alg\":\"RS256\",\"kid\":\"sandbox-key\",\"b64\":true,\"crit\":[\"b64\"]}")]
    [InlineData("{\"alg\":\"RS256\",\"kid\":\"sandbox-key\",\"b64\":false,\"crit\":[\"unknown\"]}")]
    [InlineData("{\"alg\":\"RS256\",\"kid\":\"sandbox-key\",\"b64\":false}")]
    [InlineData("{\"alg\":\"RS256\",\"kid\":\"sandbox-key\",\"kid\":\"sandbox-key\",\"b64\":false,\"crit\":[\"b64\"]}")]
    [InlineData("{\"alg\":\"RS256\",\"kid\":\"sandbox-key\",\"b64\":false,\"crit\":[\"b64\"],\"jku\":\"https://evil.example/keys\"}")]
    public void UnsupportedOrAmbiguousSignatureHeadersAreRejected(string header)
    {
        using var rsa = RSA.Create(2048);
        var body = Payload();
        Assert.False(SaleorSignature.Verify(body, Sign(rsa, body, header), KeyId, rsa.ExportSubjectPublicKeyInfoPem()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("a.payload.b")]
    [InlineData("!..a")]
    [InlineData("e30=..abcd")]
    public void MalformedSignaturesFailClosed(string signature)
    {
        using var rsa = RSA.Create(2048);
        Assert.False(SaleorSignature.Verify(Payload(), signature, KeyId, rsa.ExportSubjectPublicKeyInfoPem()));
    }

    [Theory]
    [InlineData("http://shop.example/graphql/")]
    [InlineData("https://shop.example:444/graphql/")]
    [InlineData("https://user@shop.example/graphql/")]
    [InlineData("https://shop.example/graphql/?key=value")]
    [InlineData("https://shop.example/graphql/#fragment")]
    [InlineData("https://shop.example/other/")]
    public void OnlyExplicitCanonicalHttpsApiUrlsAreAccepted(string url)
    {
        Assert.False(SaleorReviewEndpoint.IsApprovedApiUrl(url));
        Assert.True(SaleorReviewEndpoint.IsApprovedApiUrl(ApiUrl));
    }

    [Fact]
    public async Task SignedEndpointReturnsReviewAndNoStoreCacheHeader()
    {
        using var rsa = RSA.Create(2048);
        var body = Payload();
        var context = Context(body, Sign(rsa, body));
        var result = await SaleorReviewEndpoint.HandleAsync(context.Request, Tenant, Configuration(rsa), default);
        Assert.Equal(200, Status(result));
        var review = Assert.IsType<SaleorReviewResult>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.False(review.ApprovedForPayment);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Theory]
    [InlineData("wrong-tenant", 401)]
    [InlineData("wrong-store", 401)]
    [InlineData("missing-signature", 401)]
    [InlineData("duplicate-header", 401)]
    [InlineData("tampered-body", 401)]
    [InlineData("disabled", 503)]
    [InlineData("content-type", 415)]
    [InlineData("expired", 400)]
    public async Task EndpointRejectsUnauthorizedOrInvalidRequests(string scenario, int expected)
    {
        using var rsa = RSA.Create(2048);
        var body = Payload(scenario == "expired" ? DateTimeOffset.UtcNow.AddHours(-1) : null);
        var context = Context(body, Sign(rsa, body), scenario == "wrong-store" ? "https://other.example/graphql/" : ApiUrl);
        if (scenario == "missing-signature") context.Request.Headers.Remove("Saleor-Signature");
        if (scenario == "duplicate-header") context.Request.Headers.Append("Saleor-Api-Url", ApiUrl);
        if (scenario == "content-type") context.Request.ContentType = "text/plain";
        if (scenario == "tampered-body") context.Request.Body = new MemoryStream(Payload(DateTimeOffset.UtcNow.AddMinutes(-1)));
        var result = await SaleorReviewEndpoint.HandleAsync(context.Request, Tenant,
            Configuration(rsa, scenario != "disabled", scenario == "wrong-tenant" ? "merchant-b" : Tenant), default);
        Assert.Equal(expected, Status(result));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BodyLimitAppliesWithAndWithoutContentLength(bool knownLength)
    {
        using var rsa = RSA.Create(2048);
        var body = new byte[SaleorReviewEndpoint.MaxBodyBytes + 1];
        var context = Context(body, "not-parsed", knownLength: knownLength);
        Assert.Equal(413, Status(await SaleorReviewEndpoint.HandleAsync(context.Request, Tenant, Configuration(rsa), default)));
    }

    [Fact]
    public async Task RealHttpRoundTripAcceptsSignedReviewAndRejectsAnotherTenant()
    {
        using var rsa = RSA.Create(2048);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var configuration = Configuration(rsa);
        app.MapPost("/review", (HttpRequest request) =>
            SaleorReviewEndpoint.HandleAsync(request, request.Headers["Test-Tenant"].ToString(), configuration, default));
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
            var body = Payload();
            foreach (var tenant in new[] { Tenant, "merchant-b" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/review");
                request.Headers.Add("Test-Tenant", tenant);
                request.Headers.Add("Saleor-Api-Url", ApiUrl);
                request.Headers.Add("Saleor-Signature", Sign(rsa, body));
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new("application/json");
                using var response = await client.SendAsync(request);
                Assert.Equal(tenant == Tenant ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
                if (response.IsSuccessStatusCode)
                {
                    var review = await response.Content.ReadFromJsonAsync<SaleorReviewResult>();
                    Assert.False(review!.ApprovedForPayment);
                    Assert.Null(review.LandedTotal);
                }
            }
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
