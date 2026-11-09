using GlobalTradeTax.Api;
using Xunit;

public sealed class FiveCountryImportReferenceTests
{
    [Theory]
    [InlineData("MY", "Malaysia", "sales tax")][InlineData("ID", "Indonesia", "agency restrictions")]
    [InlineData("TH", "Thailand", "permits")][InlineData("VN", "Vietnam", "goods-management")]
    [InlineData("PH", "Philippines", "accreditation")]
    public void National_guidance_flows_to_policy_and_compliance_without_financial_approval(string code, string name, string detail)
    {
        var reference = FiveCountryImportReference.Find(code.ToLowerInvariant())!;
        Assert.Equal(name, reference.Name); Assert.Equal(code, reference.Country); Assert.Equal("reference_only", reference.Status);
        Assert.Contains(reference.UnresolvedChecks, c => c.Contains(detail));
        Assert.Contains(reference.UnresolvedChecks, c => c.Contains("origin") && c.Contains("preference"));
        Assert.Contains(reference.UnresolvedChecks, c => c.Contains("territory"));
        Assert.Contains(reference.UnresolvedChecks, c => c.Contains("courier"));
        Assert.Null(reference.ImportDuty); Assert.Null(reference.ImportTax); Assert.Null(reference.LandedTotal);
        Assert.False(reference.VerifiedForCalculation); Assert.False(reference.PaymentApproved); Assert.False(reference.ShipmentReleaseApproved);
        var policy = DestinationPolicies.Resolve(code); Assert.Equal("provider_required", policy.Status);
        Assert.Equal(reference.Sources.Select(s => s.Url), policy.References);
        var request = new QuoteRequest("US", code, "USD", "DDP", [new("item", "0101299000", "US", 1, 100m)]);
        var compliance = ShipmentCompliance.Evaluate(request);
        Assert.Equal("manual_review_required", compliance.Decision); Assert.False(compliance.ShipmentReleaseApproved);
        Assert.All(reference.UnresolvedChecks, c => Assert.Contains(c, compliance.RequiredChecks));
        Assert.Null(new QuoteService().Calculate(request).LandedTotal);
    }
    [Theory]
    [InlineData(null)][InlineData("")][InlineData("SG")][InlineData("US")][InlineData("MYS")][InlineData(" MY")][InlineData("ÅY")]
    public void Invalid_and_unsupported_codes_are_rejected(string? code) => Assert.Null(FiveCountryImportReference.Find(code));
}
