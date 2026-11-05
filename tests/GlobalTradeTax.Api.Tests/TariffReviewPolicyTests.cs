using GlobalTradeTax.Api;
using Xunit;

public sealed class TariffReviewPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static TariffReviewRequest Request() => new(new string('a', 64), "reviewed_reference_only", "operator-reviewer",
        "https://example.test/licence", Now.AddDays(30), "Synthetic reference source reviewed; no legal determination.");

    [Fact]
    public void Review_normalizes_hash_case_whitespace_and_timezone_without_licence_verification()
    {
        var (draft, error) = TariffReviewPolicy.Normalize(Request() with
            { ExpectedDatasetSha256 = new string('A', 64), ReviewedBy = " operator-reviewer ", ExpiresAt = Now.AddDays(30).ToOffset(TimeSpan.FromHours(2)) });
        Assert.Null(error);
        Assert.Equal(new string('a', 64), draft!.DatasetSha256);
        Assert.Equal("operator-reviewer", draft.ReviewedBy);
        Assert.Equal(TimeSpan.Zero, draft.ExpiresAt!.Value.Offset);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("disposition")]
    [InlineData("reviewer")]
    [InlineData("note")]
    [InlineData("licence")]
    [InlineData("expiry")]
    [InlineData("licence-userinfo")]
    [InlineData("sub-microsecond")]
    public void Missing_or_invalid_attestation_fields_are_rejected(string corruption)
    {
        var request = Request();
        request = corruption switch
        {
            "digest" => request with { ExpectedDatasetSha256 = new string('g', 64) },
            "disposition" => request with { Disposition = "published" },
            "reviewer" => request with { ReviewedBy = "bad\u0000reviewer" },
            "note" => request with { Note = " " },
            "licence" => request with { LicenceUrl = null },
            "expiry" => request with { ExpiresAt = null },
            "licence-userinfo" => request with { LicenceUrl = "https://name:secret@example.test/terms" },
            _ => request with { ExpiresAt = Now.AddTicks(1) }
        };
        var (draft, error) = TariffReviewPolicy.Normalize(request);
        Assert.Null(draft);
        Assert.NotNull(error);
    }

    [Fact]
    public void Rejection_can_record_missing_rights_without_a_licence_or_expiry()
    {
        var (draft, error) = TariffReviewPolicy.Normalize(Request() with
            { Disposition = "rejected", LicenceUrl = null, ExpiresAt = null, Note = "No permission evidence supplied." });
        Assert.Null(error);
        Assert.Equal("rejected", draft!.Disposition);
        Assert.NotNull(TariffReviewPolicy.Normalize(Request() with { Disposition = "rejected" }).Error);
    }

    [Fact]
    public void URL_limits_cover_the_encoded_value_written_to_storage()
    {
        var url = "https://example.test/" + new string('é', 1000);
        Assert.True(url.Length < 2048);
        Assert.True(new Uri(url).IsWellFormedOriginalString());
        Assert.True(new Uri(url).AbsoluteUri.Length > 2048);
        Assert.False(TariffDataset.TryProvenanceUrl(url, out var canonical));
        Assert.Null(canonical);
        Assert.NotNull(TariffReviewPolicy.Normalize(Request() with { LicenceUrl = url }).Error);
    }

    [Fact]
    public void Expiry_is_exclusive_and_unknown_evidence_stays_unverified()
    {
        Assert.Equal("reviewed_reference_only", TariffReviewPolicy.EvidenceStatus("reviewed_reference_only", Now.AddTicks(10), Now));
        Assert.Equal("expired_reference_review", TariffReviewPolicy.EvidenceStatus("reviewed_reference_only", Now, Now));
        Assert.Equal("unverified_reference_review", TariffReviewPolicy.EvidenceStatus("reviewed_reference_only", null, Now));
        Assert.Equal("unverified_reference_review", TariffReviewPolicy.EvidenceStatus("published", Now.AddDays(30), Now));
        Assert.Equal("rejected", TariffReviewPolicy.EvidenceStatus("rejected", null, Now));
    }

    [Fact]
    public void Fingerprint_binds_the_document_source_and_previous_review()
    {
        var draft = TariffReviewPolicy.Normalize(Request()).Draft!;
        string Hash(TariffReviewDraft value, string url = "https://example.test/source", string version = "v1") => TariffReviewPolicy.Fingerprint(value, url, version);
        Assert.Equal(Hash(draft), Hash(draft));
        Assert.NotEqual(Hash(draft), Hash(draft with { DatasetSha256 = new string('b', 64) }));
        Assert.NotEqual(Hash(draft), Hash(draft with { PreviousReviewId = Guid.NewGuid() }));
        Assert.NotEqual(Hash(draft), Hash(draft, version: "v2"));
        Assert.NotEqual(Hash(draft), Hash(draft, url: "https://other.test/source"));
    }

    [Fact]
    public void Reviews_never_approve_calculation()
    {
        var view = new TariffReviewView(Guid.NewGuid(), new string('a', 64), "reviewed_reference_only", "reviewer",
            "https://example.test/licence", Now.AddDays(1), "Reference review only", Now,
            "https://example.test/source", "v1", "reviewed_reference_only", new string('b', 64), 1, null);
        Assert.False(view.ApprovedForCalculation);
        Assert.Equal("operator_attestation_only", view.LicenceVerification);
    }
}
