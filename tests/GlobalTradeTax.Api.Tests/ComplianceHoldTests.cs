using GlobalTradeTax.Api;
using Xunit;

public sealed class ComplianceHoldTests
{
    private static QuoteRequest Request(string shipFrom, string shipTo, string currency, bool licence, bool itar) =>
        new(shipFrom,shipTo,currency,"DDP",[new("synthetic","0101299000","US",1,1000m)],
            RequiresExportLicense:licence,ItarControlled:itar);

    private sealed class Provider : IImportRateProvider
    {
        public int Calls { get; private set; }
        public Task<RateResult> GetRatesAsync(QuoteRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RateResult(100m,220m,10m,"verified",
                new("synthetic-broker","synthetic-reference",DateTimeOffset.UtcNow.AddMinutes(-1),
                    DateTimeOffset.UtcNow.AddMinutes(10),request.Currency,"0101299000","US",request.ShipTo),[]));
        }
    }

    [Theory]
    [InlineData("US","GB","GBP",true,false)]
    [InlineData("US","XI","USD",false,true)]
    [InlineData("CN","GB","EUR",true,true)]
    [InlineData("CN","DE","EUR",false,true)]
    public async Task Declared_control_hold_blocks_verified_provider_completion(string from,string to,string currency,bool licence,bool itar)
    {
        var provider = new Provider();
        var result = await new LandedCostService(provider).CalculateAsync(Request(from,to,currency,licence,itar),default);
        Assert.Equal("manual_review_required",result.Status);
        Assert.Null(result.ImportDuty); Assert.Null(result.ImportTax); Assert.Null(result.ClearanceFee); Assert.Null(result.Total);
        Assert.Null(result.Evidence); Assert.Equal(0,provider.Calls);
        Assert.True(result.Compliance!.SpecialistHoldRequired);
        Assert.False(result.Compliance.ShipmentReleaseApproved);
        Assert.Equal(licence,result.Compliance.HoldReasons.Contains("declared_export_licence"));
        Assert.Equal(itar,result.Compliance.HoldReasons.Contains("declared_itar_control"));
    }

    [Theory]
    [InlineData("US","GB","GBP",true,false)]
    [InlineData("US","XI","USD",false,true)]
    [InlineData("CN","GB","EUR",true,true)]
    [InlineData("CN","DE","EUR",false,true)]
    public void Stored_quote_surfaces_declared_hold_outside_US_currency_screen(string from,string to,string currency,bool licence,bool itar)
    {
        var result = new QuoteService().Calculate(Request(from,to,currency,licence,itar));
        Assert.Equal("manual_review_required",result.Status);
        Assert.Contains(result.RequiredActions,x=>x.Contains("specialist",StringComparison.OrdinalIgnoreCase));
        Assert.Null(result.LandedTotal);
        Assert.True(result.Compliance!.SpecialistHoldRequired);
        Assert.False(result.Compliance.ShipmentReleaseApproved);
    }

    /// <summary>Proves that a valid financial estimate cannot become shipment-release approval.</summary>
    [Theory]
    [InlineData("GB")]
    [InlineData("XI")]
    [InlineData("DE")]
    public async Task An_unflagged_financial_estimate_never_approves_shipment_release(string destination)
    {
        var provider = new Provider();
        var result = await new LandedCostService(provider).CalculateAsync(Request("US",destination,"GBP",false,false),default);
        Assert.Equal("verified",result.Status); Assert.Equal(1330m,result.Total); Assert.Equal(1,provider.Calls);
        Assert.False(result.Compliance!.SpecialistHoldRequired); Assert.Empty(result.Compliance.HoldReasons);
        Assert.False(result.Compliance.ShipmentReleaseApproved);
        if (destination is "GB" or "XI") Assert.Equal("manual_review_required",result.Compliance.Decision);
    }

    [Fact]
    public void Declared_hold_is_preserved_by_tenant_scoped_idempotency_and_serialization()
    {
        var store = new QuoteStore(); var request = Request("CN","GB","GBP",true,true);
        var first = store.Create("merchant-a","hold",request,new QuoteService()).Quote!;
        var replay = store.Create("merchant-a","hold",request,new QuoteService());
        Assert.Equal(first,replay.Quote); Assert.Null(replay.Error);
        Assert.Null(store.Find("merchant-b",first.QuoteId));
        Assert.NotNull(store.Create("merchant-a","hold",request with { ItarControlled=false },new QuoteService()).Error);
        var restored = System.Text.Json.JsonSerializer.Deserialize<QuoteResponse>(System.Text.Json.JsonSerializer.Serialize(first))!;
        Assert.True(restored.Compliance!.SpecialistHoldRequired); Assert.False(restored.Compliance.ShipmentReleaseApproved);
        Assert.Equal(first.Compliance!.HoldReasons,restored.Compliance.HoldReasons);
    }

    [Fact]
    public void A_historical_quote_without_compliance_evidence_is_not_retroactively_cleared()
    {
        var historical = System.Text.Json.JsonSerializer.Deserialize<QuoteResponse>("{\"QuoteId\":\"old\",\"Status\":\"requires_provider_quote\"}")!;
        Assert.Null(historical.Compliance); Assert.Equal("requires_provider_quote",historical.Status);
    }
}
