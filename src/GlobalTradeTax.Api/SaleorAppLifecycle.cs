using System.Globalization;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public sealed record SaleorLifecycleChange(string AppId, bool Active, bool Deleted, DateTimeOffset IssuedAt);

public static class SaleorAppLifecycle
{
    public const string Query = """
        subscription GlobalTradeTaxAppLifecycle {
          event {
            __typename issuedAt version recipient { id }
            ... on AppInstalled { app { id identifier isActive } }
            ... on AppUpdated { app { id identifier isActive } }
            ... on AppStatusChanged { app { id identifier isActive } }
            ... on AppDeleted { app { id identifier isActive } }
          }
        }
        """;

    public static SaleorLifecycleChange Analyze(JsonElement root, string header, string installedAppId, DateTimeOffset now)
    {
        var expected = root.GetProperty("__typename").GetString() switch
        {
            "AppInstalled" => "app_installed", "AppUpdated" => "app_updated",
            "AppStatusChanged" => "app_status_changed", "AppDeleted" => "app_deleted", _ => null
        };
        if (expected is null || expected != header) throw new FormatException("unexpected_app_lifecycle_event");
        var target = root.GetProperty("app");
        if (root.GetProperty("recipient").GetProperty("id").GetString() != installedAppId ||
            target.GetProperty("id").GetString() != installedAppId || target.GetProperty("identifier").GetString() != SaleorApp.Identifier)
            throw new FormatException("app_lifecycle_installation_mismatch");
        if (!DateTimeOffset.TryParse(root.GetProperty("issuedAt").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var issuedAt) ||
            issuedAt < now.AddHours(-24) || issuedAt > now.AddMinutes(1)) throw new FormatException("app_lifecycle_timestamp_invalid");
        return new(installedAppId, target.GetProperty("isActive").GetBoolean(), expected == "app_deleted", issuedAt);
    }
}
