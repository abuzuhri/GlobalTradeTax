using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public static class SaleorSignature
{
    public static bool Verify(ReadOnlySpan<byte> body, string signature, string keyId, string publicKeyPem)
    {
        if (signature.Length > 4096 || string.IsNullOrWhiteSpace(keyId) ||
            string.IsNullOrWhiteSpace(publicKeyPem) || publicKeyPem.Contains("PRIVATE", StringComparison.Ordinal)) return false;
        var parts = signature.Split('.');
        if (parts.Length != 3 || parts[1].Length != 0) return false;
        try
        {
            using var header = JsonDocument.Parse(Decode(parts[0]));
            var root = header.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var properties = root.EnumerateObject().ToArray();
            if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length ||
                properties.Any(property => property.Name is not ("alg" or "kid" or "b64" or "crit" or "typ"))) return false;
            if (!root.TryGetProperty("alg", out var algorithm) || algorithm.GetString() != "RS256" ||
                !root.TryGetProperty("kid", out var id) || id.GetString() != keyId ||
                !root.TryGetProperty("b64", out var encoded) || encoded.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("crit", out var critical) || critical.ValueKind != JsonValueKind.Array ||
                critical.GetArrayLength() != 1 || critical[0].GetString() != "b64") return false;
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            if (rsa.KeySize is < 2048 or > 8192) return false;
            var signatureBytes = Decode(parts[2]);
            if (signatureBytes.Length != rsa.KeySize / 8) return false;
            var prefix = Encoding.ASCII.GetBytes(parts[0] + ".");
            var signedBytes = new byte[prefix.Length + body.Length];
            prefix.CopyTo(signedBytes, 0);
            body.CopyTo(signedBytes.AsSpan(prefix.Length));
            return rsa.VerifyData(signedBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception error) when (error is JsonException or FormatException or CryptographicException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new FormatException("Invalid base64url value.");
        var decoded = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
        if (Convert.ToBase64String(decoded).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value)
            throw new FormatException("Non-canonical base64url value.");
        return decoded;
    }
}
