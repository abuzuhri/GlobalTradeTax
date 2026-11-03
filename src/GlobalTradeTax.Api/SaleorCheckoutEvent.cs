using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GlobalTradeTax.Api;

public static class SaleorCheckoutEvent
{
    public const string Query = """
        subscription GlobalTradeTaxCheckoutReview {
          event {
            __typename issuedAt version recipient { id }
            ... on CheckoutCreated { checkout { ...ReviewCheckout } }
            ... on CheckoutUpdated { checkout { ...ReviewCheckout } }
          }
        }
        fragment ReviewCheckout on Checkout {
          id
          channel { slug currencyCode taxConfiguration { pricesEnteredWithTax } }
          shippingAddress { country { code } }
          shippingPrice { net { amount } }
          lines {
            id quantity unitPrice { net { amount } } totalPrice { net { amount } }
            variant { sku product { hsCode: metafield(key: "gtt.hsCode") countryOfOrigin: metafield(key: "gtt.countryOfOrigin") } }
          }
        }
        """;

    public static SaleorReviewResult Analyze(ReadOnlyMemory<byte> body, string eventHeader, string tenant, string apiUrl, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
        SaleorReview.RejectDuplicateProperties(document.RootElement);
        var root = JsonNode.Parse(body.Span)!.AsObject();
        var type = root["__typename"]?.GetValue<string>();
        var expected = type switch { "CheckoutCreated" => "checkout_created", "CheckoutUpdated" => "checkout_updated", _ => null };
        if (expected is null || expected != eventHeader) throw new FormatException("unexpected_checkout_event");
        if (!DateTimeOffset.TryParse(root["issuedAt"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var issuedAt) ||
            issuedAt < now.AddHours(-24) || issuedAt > now.AddMinutes(1)) throw new FormatException("checkout_event_timestamp_invalid");
        var checkout = root["checkout"]?.AsObject() ?? throw new FormatException("checkout_required");
        var channel = checkout["channel"]!.AsObject();
        var sourceLines = checkout["lines"]!.AsArray();
        var lines = new JsonArray();
        foreach (var source in sourceLines)
        {
            var line = source!.AsObject();
            lines.Add(new JsonObject
            {
                ["sourceLine"] = new JsonObject { ["id"] = line["id"]!.DeepClone(), ["variant"] = line["variant"]?.DeepClone() },
                ["quantity"] = line["quantity"]!.DeepClone(),
                ["unitPrice"] = line["unitPrice"]!["net"]!.DeepClone(),
                ["totalPrice"] = line["totalPrice"]!["net"]!.DeepClone()
            });
        }
        var normalized = new JsonObject
        {
            ["__typename"] = "CalculateTaxes", ["issuedAt"] = root["issuedAt"]!.DeepClone(),
            ["taxBase"] = new JsonObject
            {
                ["sourceObject"] = new JsonObject { ["__typename"] = "Checkout", ["id"] = checkout["id"]!.DeepClone() },
                ["channel"] = new JsonObject { ["slug"] = channel["slug"]!.DeepClone() },
                ["currency"] = channel["currencyCode"]!.DeepClone(),
                ["pricesEnteredWithTax"] = channel["taxConfiguration"]!["pricesEnteredWithTax"]!.DeepClone(),
                ["shippingPrice"] = checkout["shippingPrice"]!["net"]!.DeepClone(),
                ["address"] = checkout["shippingAddress"]?.DeepClone(), ["lines"] = lines
            }
        };
        var review = SaleorReview.Analyze(JsonSerializer.SerializeToUtf8Bytes(normalized), tenant, apiUrl, issuedAt);
        return review with { UnresolvedChecks = [.. review.UnresolvedChecks, "asynchronous_event_not_a_checkout_quote"] };
    }
}
