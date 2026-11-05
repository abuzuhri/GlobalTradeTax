using System.Security.Cryptography;
using System.Text.Json;

namespace GlobalTradeTax.Api;

public sealed record TariffReviewRequest(string? ExpectedDatasetSha256, string? Disposition, string? ReviewedBy,
    string? LicenceUrl, DateTimeOffset? ExpiresAt, string? Note, Guid? PreviousReviewId = null);
public sealed record TariffReviewDraft(string DatasetSha256, string Disposition, string ReviewedBy,
    string? LicenceUrl, DateTimeOffset? ExpiresAt, string Note, Guid? PreviousReviewId);

// Records an operator attestation. It does not verify a licence or publish a rate.
public static class TariffReviewPolicy
{
    public static (TariffReviewDraft? Draft, string? Error) Normalize(TariffReviewRequest request)
    {
        var sha = request.ExpectedDatasetSha256?.ToLowerInvariant();
        if (sha is not { Length: 64 } || !sha.All(char.IsAsciiHexDigit)) return (null, "expected_dataset_sha256_required");
        if (request.Disposition is not ("reviewed_reference_only" or "rejected")) return (null, "invalid_reference_review_disposition");
        var reviewer = request.ReviewedBy?.Trim();
        var note = request.Note?.Trim();
        if (!TariffDataset.CleanText(reviewer, 128) || note is null || note.Length is < 1 or > 2000 ||
            note.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) return (null, "reviewer_and_note_required_within_limits");
        string? licence = null;
        if (request.LicenceUrl is not null && !TariffDataset.TryProvenanceUrl(request.LicenceUrl.Trim(), out licence))
            return (null, "invalid_licence_url");
        if (request.Disposition == "reviewed_reference_only" && (licence is null || request.ExpiresAt is null))
            return (null, "licence_and_expiry_required_for_reference_review");
        if (request.Disposition == "rejected" && request.ExpiresAt is not null) return (null, "rejection_has_no_expiry");
        var expiry = request.ExpiresAt?.ToUniversalTime();
        // PostgreSQL timestamps have microsecond precision; never silently round an attested expiry.
        if (expiry is { } time && time.Ticks % 10 != 0) return (null, "expiry_requires_microsecond_precision");
        return (new(sha, request.Disposition, reviewer!, licence, expiry, note, request.PreviousReviewId), null);
    }

    public static string Fingerprint(TariffReviewDraft review, string sourceUrl, string sourceVersion) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { Schema = "tariff-reference-review/v1", Review = review, SourceUrl = sourceUrl, SourceVersion = sourceVersion }))).ToLowerInvariant();

    public static string EvidenceStatus(string disposition, DateTimeOffset? expiresAt, DateTimeOffset now) =>
        disposition == "rejected" ? "rejected" : disposition == "reviewed_reference_only" && expiresAt is not null
            ? expiresAt > now ? "reviewed_reference_only" : "expired_reference_review" : "unverified_reference_review";
}

public sealed record TariffReviewView(Guid ReviewId, string DatasetSha256, string Disposition, string ReviewedBy,
    string? LicenceUrl, DateTimeOffset? ExpiresAt, string Note, DateTimeOffset ReviewedAt,
    string SourceUrl, string SourceVersion, string EvidenceStatus, string RequestSha256, int Revision, Guid? PreviousReviewId)
{
    public bool ApprovedForCalculation => false;
    public string LicenceVerification => "operator_attestation_only";
}

public sealed record TariffReviewResult(string Status, TariffReviewView? Review = null, string? Error = null);
public sealed record TariffStageResult(Guid? BatchId, bool AlreadyExists, string? Error = null);
