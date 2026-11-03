using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public sealed record SaleorAppEvent(string Hash, string EventType, DateTimeOffset ReceivedAt, SaleorReviewResult Review);
public sealed record SaleorAppState(string AppId, string Token, SaleorAppEvent[] Events,
    bool Active = true, bool Revoked = false, DateTimeOffset? LifecycleIssuedAt = null);

public sealed class SaleorAppStore(IConfiguration configuration)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<SaleorAppState?> ReadAsync(string tenant, string apiUrl, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(tenant, apiUrl, cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<bool> RegisterAsync(string tenant, string apiUrl, string appId, string token, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadCoreAsync(tenant, apiUrl, cancellationToken);
            if (state is not null && !state.Revoked) return state.AppId == appId && SecretEquals(state.Token, token);
            if (state?.AppId == appId) return false;
            await WriteCoreAsync(tenant, apiUrl, new(appId, token, state?.Events ?? []), cancellationToken);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> AppendAsync(string tenant, string apiUrl, SaleorAppEvent entry, CancellationToken cancellationToken,
        string? expectedAppId = null, string? expectedToken = null)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadCoreAsync(tenant, apiUrl, cancellationToken);
            if (state is null || !state.Active || state.Revoked ||
                (expectedAppId is not null && state.AppId != expectedAppId) ||
                (expectedToken is not null && !SecretEquals(state.Token, expectedToken))) return false;
            if (state.Events.Any(existing => existing.Hash == entry.Hash)) return true;
            var events = state.Events.Append(entry).OrderByDescending(item => item.Review.SourceIssuedAt).Take(200).ToArray();
            await WriteCoreAsync(tenant, apiUrl, state with { Events = events }, cancellationToken);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> ApplyLifecycleAsync(string tenant, string apiUrl, SaleorLifecycleChange change, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadCoreAsync(tenant, apiUrl, cancellationToken);
            if (state is null || state.AppId != change.AppId) return false;
            if (state.Revoked) return true;
            if (!change.Deleted && state.LifecycleIssuedAt is { } previous &&
                (change.IssuedAt < previous || (change.IssuedAt == previous && !state.Active))) return true;
            await WriteCoreAsync(tenant, apiUrl, state with
            {
                Token = change.Deleted ? "" : state.Token,
                Active = change.Active && !change.Deleted, Revoked = change.Deleted,
                LifecycleIssuedAt = change.IssuedAt
            }, cancellationToken);
            return true;
        }
        finally { gate.Release(); }
    }

    private string FilePath(string tenant, string apiUrl)
    {
        var directory = configuration["SaleorApp:StateDirectory"] ?? throw new InvalidOperationException("Saleor App state directory is required.");
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidOperationException("Saleor App state directory must be absolute.");
        var digest = Convert.ToHexString(SHA256.HashData(Scope(tenant, apiUrl))).ToLowerInvariant();
        return Path.Combine(directory, digest + ".enc");
    }

    private byte[] EncryptionKey()
    {
        var key = Convert.FromHexString(configuration["SaleorApp:EncryptionKey"] ?? "");
        if (key.Length != 32) throw new InvalidOperationException("Saleor App encryption requires a 256-bit key.");
        return key;
    }

    private byte[][] DecryptionKeys()
    {
        var previous = configuration.GetSection("SaleorApp:PreviousEncryptionKeys").GetChildren().ToArray();
        if (previous.Length > 2) throw new InvalidOperationException("At most two previous Saleor App encryption keys are allowed.");
        var keys = new List<byte[]> { EncryptionKey() };
        foreach (var item in previous)
        {
            var key = Convert.FromHexString(item.Value ?? "");
            if (key.Length != 32 || keys.Any(existing => CryptographicOperations.FixedTimeEquals(existing, key)))
                throw new InvalidOperationException("Previous encryption keys must be distinct 256-bit keys.");
            keys.Add(key);
        }
        return keys.ToArray();
    }

    private static byte[] Scope(string tenant, string apiUrl) => JsonSerializer.SerializeToUtf8Bytes(new { Tenant = tenant, ApiUrl = apiUrl });

    private async Task<SaleorAppState?> ReadCoreAsync(string tenant, string apiUrl, CancellationToken cancellationToken)
    {
        var path = FilePath(tenant, apiUrl);
        if (!File.Exists(path)) return null;
        var envelope = await File.ReadAllBytesAsync(path, cancellationToken);
        if (envelope.Length < 29 || envelope[0] != 1) throw new CryptographicException("Invalid Saleor App state envelope.");
        var cleartext = new byte[envelope.Length - 29];
        var keys = DecryptionKeys();
        for (var index = 0; index < keys.Length; index++)
        {
            using var cipher = new AesGcm(keys[index], 16);
            try { cipher.Decrypt(envelope.AsSpan(1, 12), envelope.AsSpan(29), envelope.AsSpan(13, 16), cleartext, Scope(tenant, apiUrl)); }
            catch (AuthenticationTagMismatchException) when (index < keys.Length - 1) { continue; }
            var state = JsonSerializer.Deserialize<SaleorAppState>(cleartext) ?? throw new CryptographicException("Invalid Saleor App state.");
            if (index > 0) await WriteCoreAsync(tenant, apiUrl, state, cancellationToken);
            return state;
        }
        throw new CryptographicException("Saleor App state cannot be decrypted.");
    }

    private async Task WriteCoreAsync(string tenant, string apiUrl, SaleorAppState state, CancellationToken cancellationToken)
    {
        var cleartext = JsonSerializer.SerializeToUtf8Bytes(state);
        var envelope = new byte[cleartext.Length + 29];
        envelope[0] = 1;
        RandomNumberGenerator.Fill(envelope.AsSpan(1, 12));
        using var cipher = new AesGcm(EncryptionKey(), 16);
        cipher.Encrypt(envelope.AsSpan(1, 12), cleartext, envelope.AsSpan(29), envelope.AsSpan(13, 16), Scope(tenant, apiUrl));
        var path = FilePath(tenant, apiUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, envelope, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static bool SecretEquals(string expected, string supplied) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
}
