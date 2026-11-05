using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GlobalTradeTax.Api;

// An immutable, reproducible evidence envelope for audits; never a payable quote.
public static class UkAssessmentEvidencePackage
{
    public static UkEvidencePackage Build(UkAssessmentReport report, DateTimeOffset capturedAt)
    {
        var canonical = new
        {
            report.CommodityCode,
            report.Origin,
            report.DestinationTerritory,
            report.ResponseSha256,
            report.ReferenceGraphComplete,
            ImportDate = report.ImportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            report.Status,
            report.VerifiedForCalculation,
            report.ImportDuty,
            report.ImportVat,
            Measures = report.Measures.OrderBy(m => m.MeasureId, StringComparer.Ordinal)
                .Select(m => new { m.MeasureId, m.Status, m.RawDutyExpression,
                    Areas = m.GeographicalAreaReferences.Order(StringComparer.Ordinal).ToArray(),
                    Checks = m.OutstandingChecks.Order(StringComparer.Ordinal).ToArray(),
                    m.Rule, Applicability = m.Applicability is { } applicability ? new { applicability.Status,
                        applicability.Direction, applicability.EffectiveWindow, applicability.GeographyEvidence,
                        applicability.ExclusionEvidence, applicability.VerifiedForCalculation,
                        Checks = applicability.EvidenceChecks.Order(StringComparer.Ordinal).ToArray() } : null }).ToArray(),
            Areas = report.Areas.OrderBy(a => a.AreaId, StringComparer.Ordinal)
                .Select(a => new { a.AreaId, a.LookupStatus, a.MembershipEvidence, a.SourceUrl, a.VerifiedForCalculation,
                    a.AsOfDate, a.DestinationTerritory, a.ResponseSha256, a.MembershipPath,
                    Checks = a.UnresolvedChecks.Order(StringComparer.Ordinal).ToArray() }).ToArray(),
            report.CommoditySourceUrl,
            Warnings = report.Warnings.Order(StringComparer.Ordinal).ToArray()
        };
        var json = JsonSerializer.Serialize(canonical);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new UkEvidencePackage("uk-reference-evidence/v3", capturedAt, digest,
            "sha256", report, false, "Evidence only; not a payable customs or VAT determination.");
    }
}

public sealed record UkEvidencePackage(string SchemaVersion, DateTimeOffset CapturedAt,
    string ContentHash, string HashAlgorithm, UkAssessmentReport Assessment,
    bool ApprovedForPayment, string Disclaimer);
