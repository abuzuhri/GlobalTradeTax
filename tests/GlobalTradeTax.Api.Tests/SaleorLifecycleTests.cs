using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlobalTradeTax.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class SaleorLifecycleTests : IDisposable
{
    private const string ApiUrl = "https://shop.example/graphql/";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "gtt-lifecycle-tests-" + Guid.NewGuid().ToString("N"));
    private readonly RSA signer = RSA.Create(2048);

    private IConfiguration Configuration(string key = "a", params string[] previous) => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["SaleorApp:StateDirectory"] = directory, ["SaleorApp:EncryptionKey"] = string.Concat(Enumerable.Repeat(key, 64)) }
            .Concat(previous.Select((value, index) => new KeyValuePair<string, string?>("SaleorApp:PreviousEncryptionKeys:" + index, string.Concat(Enumerable.Repeat(value, 64)))))).Build();

    private SaleorAppRegistration Registration() => new("shop", "tenant-a", ApiUrl, ApiUrl, "https://app.example", "https://app.example",
        "https://dashboard.example", "current", signer.ExportSubjectPublicKeyInfoPem());

    private static byte[] Lifecycle(string type = "AppStatusChanged", bool active = false, string appId = "app-1", string recipient = "app-1",
        DateTimeOffset? issuedAt = null, string identifier = SaleorApp.Identifier) => JsonSerializer.SerializeToUtf8Bytes(new
        { __typename = type, issuedAt = issuedAt ?? DateTimeOffset.UtcNow, recipient = new { id = recipient }, app = new { id = appId, identifier, isActive = active } });

    private static byte[] Checkout(string recipient = "app-1") => JsonSerializer.SerializeToUtf8Bytes(new
    {
        __typename = "CheckoutCreated", issuedAt = DateTimeOffset.UtcNow, recipient = new { id = recipient },
        checkout = new
        {
            id = "checkout-1", channel = new { slug = "uk", currencyCode = "GBP", taxConfiguration = new { pricesEnteredWithTax = false } },
            shippingAddress = new { country = new { code = "GB" } }, shippingPrice = new { net = new { amount = 0m } },
            lines = new[] { new { id = "line-1", quantity = 2, unitPrice = new { net = new { amount = 12.5m } },
                totalPrice = new { net = new { amount = 25m } }, variant = new { sku = "shirt", product = new { hsCode = "6109100000", countryOfOrigin = "CN" } } } }
        }
    });

    private string Sign(byte[] bytes, string keyId = "current", RSA? signingKey = null)
    {
        static string Encode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = keyId, b64 = false, crit = new[] { "b64" } }));
        return header + ".." + Encode((signingKey ?? signer).SignData([.. Encoding.ASCII.GetBytes(header + "."), .. bytes], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private HttpRequest Request(byte[]? body = null, string eventType = "app_status_changed")
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["Saleor-Api-Url"] = ApiUrl;
        context.Request.Headers.Authorization = "Bearer staff-token";
        if (body is not null)
        {
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(body);
            context.Request.Headers["Saleor-Event"] = eventType;
            context.Request.Headers["Saleor-Signature"] = Sign(body);
        }
        return context.Request;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class IdentityServer : HttpMessageHandler, IHttpClientFactory
    {
        public bool Active { get; set; } = true;
        public string AppId { get; set; } = "app-1";
        public string Permission { get; set; } = "MANAGE_CHECKOUTS";
        public bool Unavailable { get; set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(ApiUrl, request.RequestUri!.AbsoluteUri);
            if (Unavailable) throw new HttpRequestException("Unavailable");
            var identity = request.Headers.Authorization!.Parameter == "staff-token"
                ? JsonSerializer.Serialize(new { data = new { me = new { isStaff = true }, checkouts = new { totalCount = 1 } } })
                : JsonSerializer.Serialize(new { data = new { app = new { id = AppId, identifier = SaleorApp.Identifier, isActive = Active, permissions = new[] { new { code = Permission } } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(identity) });
        }
    }

    [Theory]
    [InlineData("AppInstalled", "app_installed")]
    [InlineData("AppUpdated", "app_updated")]
    [InlineData("AppStatusChanged", "app_status_changed")]
    [InlineData("AppDeleted", "app_deleted")]
    public void SelfLifecycleContractChecksSignedIdentityAndEvent(string type, string header)
    {
        using var document = JsonDocument.Parse(Lifecycle(type));
        var change = SaleorAppLifecycle.Analyze(document.RootElement, header, "app-1", DateTimeOffset.UtcNow);
        Assert.Equal("app-1", change.AppId);
        Assert.Equal(type == "AppDeleted", change.Deleted);
    }

    [Theory]
    [InlineData("app-2", "app-1", "app_status_changed", SaleorApp.Identifier, 0)]
    [InlineData("app-1", "app-2", "app_status_changed", SaleorApp.Identifier, 0)]
    [InlineData("app-1", "app-1", "app_deleted", SaleorApp.Identifier, 0)]
    [InlineData("app-1", "app-1", "app_status_changed", "other-app", 0)]
    [InlineData("app-1", "app-1", "app_status_changed", SaleorApp.Identifier, -25)]
    [InlineData("app-1", "app-1", "app_status_changed", SaleorApp.Identifier, 1)]
    public void WrongIdentityTypeOrTimestampCannotChangeLifecycle(string appId, string recipient, string header, string identifier, int hours)
    {
        using var document = JsonDocument.Parse(Lifecycle(appId: appId, recipient: recipient, identifier: identifier, issuedAt: DateTimeOffset.UtcNow.AddHours(hours)));
        Assert.Throws<FormatException>(() => SaleorAppLifecycle.Analyze(document.RootElement, header, "app-1", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task DeletionErasesTokenAndVerifiedNewInstallationRetainsHistoricalReviews()
    {
        var store = new SaleorAppStore(Configuration());
        await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-a", default);
        var now = DateTimeOffset.UtcNow;
        var review = SaleorCheckoutEvent.Analyze(Checkout(), "checkout_created", "tenant-a", ApiUrl, now);
        var entry = new SaleorAppEvent("hash-1", "checkout_created", now, review);
        Assert.True(await store.AppendAsync("tenant-a", ApiUrl, entry, default, "app-1", "secret-token-a"));
        Assert.True(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", false, true, now), default));
        Assert.True(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", false, true, now), default));
        var deleted = (await new SaleorAppStore(Configuration()).ReadAsync("tenant-a", ApiUrl, default))!;
        Assert.True(deleted.Revoked);
        Assert.False(deleted.Active);
        Assert.Empty(deleted.Token);
        Assert.Single(deleted.Events);
        Assert.False(await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-a", default));
        Assert.True(await store.RegisterAsync("tenant-a", ApiUrl, "app-2", "secret-token-b", default));
        Assert.False(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", false, true, now.AddSeconds(1)), default));
        Assert.False(await store.AppendAsync("tenant-a", ApiUrl, entry with { Hash = "hash-2" }, default, "app-1", "secret-token-a"));
        var restored = (await store.ReadAsync("tenant-a", ApiUrl, default))!;
        Assert.True(restored.Active);
        Assert.False(restored.Revoked);
        Assert.Equal("app-2", restored.AppId);
        Assert.Equal("secret-token-b", restored.Token);
        Assert.Equal(review.Reference, Assert.Single(restored.Events).Review.Reference);
    }

    [Fact]
    public async Task OutOfOrderLifecycleCannotReactivateDeletedOrSuspendedInstallation()
    {
        var store = new SaleorAppStore(Configuration());
        await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-a", default);
        var now = DateTimeOffset.UtcNow;
        Assert.True(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", false, false, now), default));
        Assert.True(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", true, false, now.AddSeconds(-1)), default));
        Assert.True(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", true, false, now), default));
        Assert.False((await store.ReadAsync("tenant-a", ApiUrl, default))!.Active);
        Assert.True(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", true, false, now.AddSeconds(1)), default));
        Assert.True((await store.ReadAsync("tenant-a", ApiUrl, default))!.Active);
        Assert.True(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", false, true, now), default));
        Assert.True(await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", true, false, now.AddSeconds(2)), default));
        Assert.True((await store.ReadAsync("tenant-a", ApiUrl, default))!.Revoked);
    }

    [Fact]
    public async Task SignedDeactivationBlocksReadsAndUnverifiedReactivationFailsClosed()
    {
        var configuration = Configuration();
        var store = new SaleorAppStore(configuration);
        await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-a", default);
        using var identity = new IdentityServer();
        var service = new SaleorApp(configuration, new TestEnvironment(), store, identity);
        Assert.Equal(200, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(Lifecycle()), Registration(), default)).StatusCode);
        Assert.Equal(403, ((IStatusCodeHttpResult)await service.ReviewsAsync(Request(), Registration(), default)).StatusCode);
        identity.Active = false;
        Assert.Equal(503, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(Lifecycle(active: true)), Registration(), default)).StatusCode);
        Assert.False((await store.ReadAsync("tenant-a", ApiUrl, default))!.Active);
        identity.Active = true;
        Assert.Equal(200, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(Lifecycle(active: true)), Registration(), default)).StatusCode);
        Assert.Equal(200, ((IStatusCodeHttpResult)await service.ReviewsAsync(Request(), Registration(), default)).StatusCode);
        identity.Active = false;
        Assert.Equal(503, ((IStatusCodeHttpResult)await service.ReviewsAsync(Request(), Registration(), default)).StatusCode);
    }

    [Fact]
    public async Task PermissionLossOrDependencyFailurePreventsReadingReviews()
    {
        var configuration = Configuration();
        var store = new SaleorAppStore(configuration);
        await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-a", default);
        using var identity = new IdentityServer { Permission = "MANAGE_ORDERS" };
        var service = new SaleorApp(configuration, new TestEnvironment(), store, identity);
        Assert.Equal(503, ((IStatusCodeHttpResult)await service.ReviewsAsync(Request(), Registration(), default)).StatusCode);
        identity.Unavailable = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => service.ReviewsAsync(Request(), Registration(), default));
        Assert.False((await store.ReadAsync("tenant-a", ApiUrl, default))!.Revoked);
    }

    [Fact]
    public async Task SignedCheckoutRetriesAreBoundToCurrentInstallationAndValidatedBeforeDeduplication()
    {
        var configuration = Configuration();
        var store = new SaleorAppStore(configuration);
        await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-a", default);
        using var identity = new IdentityServer();
        var service = new SaleorApp(configuration, new TestEnvironment(), store, identity);
        var body = Checkout();
        Assert.Equal(200, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(body, "checkout_created"), Registration(), default)).StatusCode);
        Assert.Equal(200, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(body, "checkout_created"), Registration(), default)).StatusCode);
        await Assert.ThrowsAsync<FormatException>(() => service.ReceiveAsync(Request(body, "checkout_updated"), Registration(), default));
        Assert.Single((await store.ReadAsync("tenant-a", ApiUrl, default))!.Events);
        await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", false, true, DateTimeOffset.UtcNow), default);
        Assert.Equal(403, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(body, "checkout_created"), Registration(), default)).StatusCode);
        await store.RegisterAsync("tenant-a", ApiUrl, "app-2", "secret-token-b", default);
        identity.AppId = "app-2";
        Assert.Equal(401, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(body, "checkout_created"), Registration(), default)).StatusCode);
        Assert.Equal(401, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(Lifecycle("AppDeleted"), "app_deleted"), Registration(), default)).StatusCode);
        Assert.Equal(200, ((IStatusCodeHttpResult)await service.ReceiveAsync(Request(Checkout("app-2"), "checkout_created"), Registration(), default)).StatusCode);
    }

    [Fact]
    public async Task RegistrationRequiresFreshVerifiedInstallationAfterDeletion()
    {
        var configuration = Configuration();
        var store = new SaleorAppStore(configuration);
        await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-old", default);
        await store.ApplyLifecycleAsync("tenant-a", ApiUrl, new("app-1", false, true, DateTimeOffset.UtcNow), default);
        using var identity = new IdentityServer { Active = false };
        var service = new SaleorApp(configuration, new TestEnvironment(), store, identity);
        var body = JsonSerializer.SerializeToUtf8Bytes(new { auth_token = "candidate-token-new" });
        Assert.Equal(401, ((IStatusCodeHttpResult)await service.RegisterAsync(Request(body), Registration(), default)).StatusCode);
        identity.Active = true;
        Assert.Equal(409, ((IStatusCodeHttpResult)await service.RegisterAsync(Request(body), Registration(), default)).StatusCode);
        identity.AppId = "app-2";
        Assert.Equal(200, ((IStatusCodeHttpResult)await service.RegisterAsync(Request(body), Registration(), default)).StatusCode);
        Assert.Equal("candidate-token-new", (await store.ReadAsync("tenant-a", ApiUrl, default))!.Token);
    }

    [Fact]
    public async Task EncryptionKeyOverlapRewrapsStateAndAllowsRetiringOldKey()
    {
        var original = new SaleorAppStore(Configuration());
        await original.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-a", default);
        var rotating = new SaleorAppStore(Configuration("b", "a"));
        Assert.Equal("secret-token-a", (await rotating.ReadAsync("tenant-a", ApiUrl, default))!.Token);
        Assert.Equal("secret-token-a", (await new SaleorAppStore(Configuration("b")).ReadAsync("tenant-a", ApiUrl, default))!.Token);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => original.ReadAsync("tenant-a", ApiUrl, default));
        Assert.Null(await rotating.ReadAsync("tenant-b", ApiUrl, default));
    }

    [Fact]
    public async Task RotationCannotRepairTamperedCiphertextOrAcceptInvalidKeyRing()
    {
        var original = new SaleorAppStore(Configuration());
        await original.RegisterAsync("tenant-a", ApiUrl, "app-1", "secret-token-a", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SaleorAppStore(Configuration("b", "b")).ReadAsync("tenant-a", ApiUrl, default));
        var path = Assert.Single(Directory.GetFiles(directory, "*.enc"));
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => new SaleorAppStore(Configuration("b", "a")).ReadAsync("tenant-a", ApiUrl, default));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public void SigningKeyOverlapAcceptsBothPinsAndRetirementRejectsOldOrUnknownKeys()
    {
        using var next = RSA.Create(2048);
        var body = Lifecycle();
        var oldKey = new SaleorAppVerificationKey("current", signer.ExportSubjectPublicKeyInfoPem());
        var newKey = new SaleorAppVerificationKey("next", next.ExportSubjectPublicKeyInfoPem());
        var registration = Registration() with { VerificationKeys = [oldKey, newKey] };
        Assert.True(SaleorApp.VerifySignature(body, Sign(body), registration));
        Assert.True(SaleorApp.VerifySignature(body, Sign(body, "next", next), registration));
        Assert.False(SaleorApp.VerifySignature(body, Sign(body, "unknown", next), registration));
        Assert.False(SaleorApp.VerifySignature(body, Sign(body, "next"), registration));
        Assert.False(SaleorApp.VerifySignature(body, Sign(body), registration with { VerificationKeys = [newKey] }));
    }

    public void Dispose()
    {
        signer.Dispose();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
