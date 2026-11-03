using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlobalTradeTax.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class SaleorAppTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "gtt-saleor-tests-" + Guid.NewGuid().ToString("N"));
    private const string ApiUrl = "https://shop.example/graphql/";

    private IConfiguration Configuration(string? key = null) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["SaleorApp:StateDirectory"] = directory,
        ["SaleorApp:EncryptionKey"] = key ?? new string('a', 64)
    }).Build();

    private static byte[] Event(string type = "CheckoutCreated", DateTimeOffset? issuedAt = null) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        __typename = type, issuedAt = issuedAt ?? DateTimeOffset.UtcNow,
        checkout = new
        {
            id = "checkout-1", channel = new { slug = "uk", currencyCode = "GBP", taxConfiguration = new { pricesEnteredWithTax = false } },
            shippingAddress = new { country = new { code = "GB" } }, shippingPrice = new { net = new { amount = 0m } },
            lines = new[] { new
            {
                id = "line-1", quantity = 2, unitPrice = new { net = new { amount = 12.5m } }, totalPrice = new { net = new { amount = 25m } },
                variant = new { sku = "shirt", product = new { hsCode = "6109100000", countryOfOrigin = "CN" } }
            } }
        }
    });

    [Theory]
    [InlineData("CheckoutCreated", "checkout_created")]
    [InlineData("CheckoutUpdated", "checkout_updated")]
    public void SignedEventProjectionKeepsSourceDataAndNeverAuthorizesPayment(string type, string header)
    {
        var review = SaleorCheckoutEvent.Analyze(Event(type), header, "tenant-a", ApiUrl, DateTimeOffset.UtcNow);
        Assert.Equal("checkout-1", review.SourceId);
        Assert.Equal("GBP", review.Currency);
        Assert.Equal("6109100000", Assert.Single(review.Lines).HsCode);
        Assert.Equal(25m, review.Lines[0].SourceTotalAmount);
        Assert.Null(review.LandedTotal);
        Assert.Null(review.ImportDuty);
        Assert.Null(review.ImportVat);
        Assert.False(review.ApprovedForPayment);
        Assert.Contains("asynchronous_event_not_a_checkout_quote", review.UnresolvedChecks);
    }

    [Fact]
    public void EventHeaderMustMatchSignedType()
    {
        Assert.Throws<FormatException>(() => SaleorCheckoutEvent.Analyze(Event(), "checkout_updated", "tenant-a", ApiUrl, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(-25)]
    [InlineData(1)]
    public void OldOrFutureEventsAreRejected(int hours)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<FormatException>(() => SaleorCheckoutEvent.Analyze(Event(issuedAt: now.AddHours(hours)), "checkout_created", "tenant-a", ApiUrl, now));
    }

    [Fact]
    public void DelayedEventKeepsItsOriginalReviewExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var issuedAt = now.AddHours(-2);
        var review = SaleorCheckoutEvent.Analyze(Event(issuedAt: issuedAt), "checkout_created", "tenant-a", ApiUrl, now);
        Assert.Equal(issuedAt.AddMinutes(5), review.ExpiresAt);
        Assert.True(review.ExpiresAt < now);
        Assert.False(review.ApprovedForPayment);
    }

    [Fact]
    public async Task TokensAndReviewsAreEncryptedAndSurviveRecreatingTheStore()
    {
        var configuration = Configuration();
        var store = new SaleorAppStore(configuration);
        Assert.True(await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "private-app-token", default));
        var review = SaleorCheckoutEvent.Analyze(Event(), "checkout_created", "tenant-a", ApiUrl, DateTimeOffset.UtcNow);
        Assert.True(await store.AppendAsync("tenant-a", ApiUrl, new("hash-1", "checkout_created", DateTimeOffset.UtcNow, review), default));
        var bytes = await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(directory, "*.enc")));
        Assert.DoesNotContain("private-app-token", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("checkout-1", Encoding.UTF8.GetString(bytes));
        var restored = await new SaleorAppStore(configuration).ReadAsync("tenant-a", ApiUrl, default);
        Assert.Equal("private-app-token", restored!.Token);
        Assert.Equal(review.Reference, Assert.Single(restored.Events).Review.Reference);
        Assert.Null(await store.ReadAsync("tenant-b", ApiUrl, default));
        Assert.Null(await store.ReadAsync("tenant-a", "https://other.example/graphql/", default));
    }

    [Fact]
    public async Task RegistrationIsIdempotentAndCannotReplaceAnotherBinding()
    {
        var store = new SaleorAppStore(Configuration());
        Assert.True(await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "token-a", default));
        Assert.True(await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "token-a", default));
        Assert.False(await store.RegisterAsync("tenant-a", ApiUrl, "app-2", "token-b", default));
        Assert.False(await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "token-b", default));
        Assert.Equal("token-a", (await store.ReadAsync("tenant-a", ApiUrl, default))!.Token);
    }

    [Fact]
    public async Task DuplicateDeliveryDoesNotCreateAnotherReview()
    {
        var store = new SaleorAppStore(Configuration());
        Assert.True(await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "token-a", default));
        var review = SaleorCheckoutEvent.Analyze(Event(), "checkout_created", "tenant-a", ApiUrl, DateTimeOffset.UtcNow);
        var entry = new SaleorAppEvent("hash-1", "checkout_created", DateTimeOffset.UtcNow, review);
        Assert.True(await store.AppendAsync("tenant-a", ApiUrl, entry, default));
        Assert.True(await store.AppendAsync("tenant-a", ApiUrl, entry, default));
        Assert.Single((await store.ReadAsync("tenant-a", ApiUrl, default))!.Events);
        Assert.False(await store.AppendAsync("tenant-b", ApiUrl, entry, default));
    }

    [Fact]
    public async Task WrongEncryptionKeyOrModifiedCiphertextFailsClosed()
    {
        var store = new SaleorAppStore(Configuration());
        Assert.True(await store.RegisterAsync("tenant-a", ApiUrl, "app-1", "token-a", default));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => new SaleorAppStore(Configuration(new string('b', 64))).ReadAsync("tenant-a", ApiUrl, default));
        var path = Assert.Single(Directory.GetFiles(directory, "*.enc"));
        var bytes = await File.ReadAllBytesAsync(path);
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => store.ReadAsync("tenant-a", ApiUrl, default));
    }

    private sealed class EnvironmentInfo(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NoNetwork : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Network must not be called.");
    }

    [Theory]
    [InlineData("Production", true, false)]
    [InlineData("Development", false, false)]
    [InlineData("Development", true, true)]
    public void HttpIsAllowedOnlyForExplicitLocalDevelopmentSandbox(string environment, bool allowHttp, bool expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SaleorApp:Enabled"] = "true", ["SaleorApp:AllowLocalSandboxHttp"] = allowHttp.ToString(),
            ["SaleorApp:Stores:0:Enabled"] = "true", ["SaleorApp:Stores:0:Id"] = "sandbox",
            ["SaleorApp:Stores:0:TenantId"] = "tenant-a", ["SaleorApp:Stores:0:ApiUrl"] = "http://localhost:18080/graphql/",
            ["SaleorApp:Stores:0:TransportUrl"] = "http://api:8000/graphql/", ["SaleorApp:Stores:0:PublicBaseUrl"] = "http://localhost:18081",
            ["SaleorApp:Stores:0:CallbackBaseUrl"] = "http://gtt.sandbox.test:8080", ["SaleorApp:Stores:0:DashboardOrigin"] = "http://localhost:19090",
            ["SaleorApp:Stores:0:KeyId"] = "pinned", ["SaleorApp:Stores:0:PublicKeyPem"] = "pinned-public-key"
        }).Build();
        var service = new SaleorApp(config, new EnvironmentInfo(environment), new SaleorAppStore(config), new NoNetwork());
        var registration = service.Resolve("sandbox");
        Assert.Equal(expected, registration is not null);
        if (registration is not null)
        {
            var manifest = JsonSerializer.SerializeToElement(service.Manifest(registration));
            Assert.Empty(manifest.GetProperty("webhooks")[0].GetProperty("syncEvents").EnumerateArray());
            Assert.Equal("MANAGE_CHECKOUTS", Assert.Single(manifest.GetProperty("permissions").EnumerateArray()).GetString());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
