using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public sealed record SaleorReviewLine(string SourceLineId, string? Sku, int Quantity,
    decimal SourceUnitAmount, decimal SourceTotalAmount, string? HsCode, string? CountryOfOrigin);

public sealed record SaleorReviewResult(string SchemaVersion, string Reference, string Status,
    string SourceType, string SourceId, string Channel, string Currency, string? DestinationCountry,
    bool SourcePricesIncludeTax, decimal SourceShippingAmount, SaleorReviewLine[] Lines,
    DateTimeOffset SourceIssuedAt, DateTimeOffset ExpiresAt, decimal? ImportDuty, decimal? ImportVat,
    decimal? ClearanceFee, decimal? LandedTotal, bool ApprovedForPayment, string[] UnresolvedChecks);

public static class SaleorReview
{
    public static SaleorReviewResult Analyze(ReadOnlyMemory<byte> body, string tenant, string apiUrl, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        RejectDuplicateProperties(root);
        if (Text(root, "__typename") != "CalculateTaxes") throw new FormatException("calculate_taxes_payload_required");
        var issuedAtText = Text(root, "issuedAt");
        if (!DateTimeOffset.TryParse(issuedAtText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var issuedAt) ||
            issuedAt < now.AddMinutes(-5) || issuedAt > now.AddMinutes(1)) throw new FormatException("payload_expired_or_invalid_timestamp");
        var taxBase = Object(root, "taxBase");
        var source = Object(taxBase, "sourceObject");
        var sourceType = RequiredText(source, "__typename");
        if (sourceType is not ("Checkout" or "Order")) throw new FormatException("unsupported_source_type");
        var sourceId = RequiredText(source, "id");
        var channel = RequiredText(Object(taxBase, "channel"), "slug");
        var currency = RequiredText(taxBase, "currency");
        if (currency.Length != 3 || currency.Any(character => character is < 'A' or > 'Z')) throw new FormatException("invalid_currency");
        if (!taxBase.TryGetProperty("pricesEnteredWithTax", out var pricesIncludeTax) ||
            pricesIncludeTax.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new FormatException("price_tax_mode_required");
        var shipping = Amount(Object(taxBase, "shippingPrice"), "amount");
        var destination = Text(Object(Object(taxBase, "address"), "country"), "code");
        if (destination is not null && !IsCountry(destination)) throw new FormatException("invalid_destination_country");
        if (!taxBase.TryGetProperty("lines", out var sourceLines) || sourceLines.ValueKind != JsonValueKind.Array ||
            sourceLines.GetArrayLength() is < 1 or > 100) throw new FormatException("one_to_100_lines_required");
        var checks = new List<string> { "review_only_not_a_saleor_tax_response", "authoritative_duty_and_vat_not_verified",
            "ship_from_and_incoterm_require_mapping", "discount_shipping_and_rounding_allocation_not_verified" };
        if (destination is null) checks.Add("destination_country_missing");
        var lines = new List<SaleorReviewLine>();
        var lineIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in sourceLines.EnumerateArray())
        {
            var sourceLine = Object(line, "sourceLine");
            var lineId = RequiredText(sourceLine, "id");
            if (!lineIds.Add(lineId)) throw new FormatException("duplicate_source_line_id");
            if (!line.TryGetProperty("quantity", out var quantity) || !quantity.TryGetInt32(out var count) || count is < 1 or > 100_000)
                throw new FormatException("invalid_quantity");
            var variant = Object(sourceLine, "variant");
            var typedVariant = Object(sourceLine, sourceType == "Checkout" ? "checkoutProductVariant" : "orderProductVariant");
            if (typedVariant.ValueKind == JsonValueKind.Object)
            {
                if (variant.ValueKind == JsonValueKind.Object) throw new FormatException("ambiguous_product_variant");
                variant = typedVariant;
            }
            var product = Object(variant, "product");
            var sku = Text(variant, "sku");
            var hsCode = Text(product, "hsCode");
            var origin = Text(product, "countryOfOrigin");
            if (string.IsNullOrWhiteSpace(sku)) checks.Add("sku_missing:" + lineId);
            if (hsCode is null || hsCode.Length is < 6 or > 10 || !hsCode.All(char.IsAsciiDigit)) checks.Add("hs_code_missing_or_invalid:" + lineId);
            if (origin is null || !IsCountry(origin)) checks.Add("origin_missing_or_invalid:" + lineId);
            checks.Add("classification_and_origin_unverified:" + lineId);
            lines.Add(new(lineId, sku, count, Amount(Object(line, "unitPrice"), "amount"),
                Amount(Object(line, "totalPrice"), "amount"), hsCode, origin));
        }
        var fingerprint = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = "saleor-review/v1", Tenant = tenant, ApiUrl = apiUrl,
            Payload = Convert.ToHexString(SHA256.HashData(body.Span))
        });
        var reference = "sr_" + Convert.ToHexString(SHA256.HashData(fingerprint)).ToLowerInvariant();
        return new("saleor-review/v1", reference, "requires_review", sourceType, sourceId, channel, currency,
            destination, pricesIncludeTax.GetBoolean(), shipping, lines.ToArray(), issuedAt, issuedAt.AddMinutes(5),
            null, null, null, null, false, checks.ToArray());
    }

    private static bool IsCountry(string value) => value.Length == 2 && value.All(character => character is >= 'A' and <= 'Z');

    private static JsonElement Object(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? Text(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 256) throw new FormatException("invalid_text_field");
        return value.GetString();
    }

    private static string RequiredText(JsonElement parent, string name) =>
        Text(parent, name) is { } value && !string.IsNullOrWhiteSpace(value) ? value : throw new FormatException("missing_" + name);

    private static decimal Amount(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount) || amount is < 0 or >= 1_000_000_000)
            throw new FormatException("invalid_source_amount");
        return amount;
    }

    internal static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new FormatException("duplicate_json_property");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }
}
