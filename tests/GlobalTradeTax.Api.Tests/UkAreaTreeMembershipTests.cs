using System.Text.Json.Nodes;
using GlobalTradeTax.Api;
using Xunit;
using static UkApplicabilityTestData;

public sealed class UkAreaTreeMembershipTests
{
    private static UkAreaMembershipReport Inspect(JsonObject document) => UkAreaMembership.Inspect(Dated(document), "1011", "US", Date);

    [Fact]
    public void Typed_dated_tree_records_a_candidate_membership_path()
    {
        var result = Inspect(AreaDocument("1011", "DE", "US"));
        Assert.Equal("candidate_membership_evidence", result.EvidenceStatus);
        Assert.Equal(new[] { "1011", "US" }, result.MembershipPath);
        Assert.False(result.VerifiedForCalculation);
    }

    [Fact]
    public void Nested_explicit_relationships_are_followed_without_label_inference()
    {
        var root = AreaDocument("1011", "1000");
        root["included"] = new JsonArray(Area("1000", "US"), Area("US"));
        Assert.Equal(new[] { "1011", "1000", "US" }, Inspect(root).MembershipPath);
    }

    [Theory]
    [InlineData("missing-children")]
    [InlineData("wrong-child-type")]
    [InlineData("missing-child-resource")]
    [InlineData("duplicate-resource")]
    [InlineData("cycle")]
    [InlineData("wrong-root")]
    [InlineData("wrong-root-type")]
    [InlineData("children-null")]
    [InlineData("numeric-child-id")]
    public void Corrupted_trees_do_not_prove_membership(string corruption)
    {
        var root = AreaDocument("1011", "US");
        switch (corruption)
        {
            case "missing-children": root["data"]!["relationships"]!.AsObject().Remove("children_geographical_areas"); break;
            case "wrong-child-type": root["data"]!["relationships"]!["children_geographical_areas"]!["data"]![0]!["type"] = "measure"; break;
            case "missing-child-resource": root["included"] = new JsonArray(); break;
            case "duplicate-resource": root["included"]!.AsArray().Add(Area("US")); break;
            case "cycle": root["included"] = new JsonArray(Area("US", "1011")); break;
            case "wrong-root": root["data"]!["id"] = "1000"; break;
            case "wrong-root-type": root["data"]!["type"] = "commodity"; break;
            case "children-null": root["data"]!["relationships"]!["children_geographical_areas"]!["data"] = null; break;
            case "numeric-child-id": root["data"]!["relationships"]!["children_geographical_areas"]!["data"]![0]!["id"] = 1; break;
        }
        var result = Inspect(root);
        Assert.Equal("unknown", result.EvidenceStatus);
        Assert.Empty(result.MembershipPath);
        Assert.False(result.VerifiedForCalculation);
    }

    [Fact]
    public void Incomplete_other_branches_are_not_ignored_after_a_match()
    {
        var root = AreaDocument("1011", "US", "ZZ");
        root["included"] = new JsonArray(Area("US"));
        Assert.Equal("unknown", Inspect(root).EvidenceStatus);
    }

    [Fact]
    public void An_unrelated_embedded_country_does_not_prove_membership()
    {
        var root = AreaDocument("1011");
        root["included"] = new JsonArray(Area("US"));
        Assert.Equal("unknown", Inspect(root).EvidenceStatus);
    }

    [Fact]
    public void Excessively_deep_trees_stay_unknown()
    {
        var root = AreaDocument("1011", "g0");
        var included = new JsonArray();
        for (var i = 0; i < 17; i++) included.Add(Area("g" + i, i == 16 ? "US" : "g" + (i + 1)));
        included.Add(Area("US"));
        root["included"] = included;
        Assert.Contains("membership_depth_limit_exceeded", Inspect(root).UnresolvedChecks);
    }

    [Fact]
    public void Resource_budget_is_reported_instead_of_silently_truncating()
    {
        var root = AreaDocument("1011", "US");
        for (var i = 0; i < 1024; i++) root["included"]!.AsArray().Add(Area("g" + i));
        Assert.Contains("membership_resource_limit_exceeded", Inspect(root).UnresolvedChecks);
    }
}
