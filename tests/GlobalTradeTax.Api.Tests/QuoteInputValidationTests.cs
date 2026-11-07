using GlobalTradeTax.Api;
using Xunit;

public sealed class QuoteInputValidationTests
{
    private static QuoteRequest Request() => new("US","GB","GBP","DDP",[new("synthetic","0101299000","CN",1,1000m)]);

    [Fact]
    public void Missing_request_is_invalid_instead_of_throwing() => Assert.Contains("request",QuoteService.Validate(null).Keys);

    [Theory]
    [InlineData("null_line")]
    [InlineData("missing_lines")]
    [InlineData("too_many_lines")]
    [InlineData("invalid_origin")]
    [InlineData("invalid_country")]
    [InlineData("invalid_currency")]
    [InlineData("line_total_overflow")]
    [InlineData("shipment_total_overflow")]
    [InlineData("long_sku")]
    [InlineData("control_sku")]
    [InlineData("long_hs")]
    [InlineData("zero_quantity")]
    public void Invalid_nested_and_overflow_inputs_are_rejected_before_calculation(string gap)
    {
        var r = Request();
        var invalid = gap switch {
            "null_line" => r with { Lines=[null!] },
            "missing_lines" => r with { Lines=null! },
            "too_many_lines" => r with { Lines=Enumerable.Repeat(r.Lines[0],101).ToList() },
            "invalid_origin" => r with { Lines=[r.Lines[0] with { CountryOfOrigin="12" }] },
            "invalid_country" => r with { ShipFrom="éS" },
            "invalid_currency" => r with { Currency="G1P" },
            "line_total_overflow" => r with { Lines=[r.Lines[0] with { UnitPrice=decimal.MaxValue,Quantity=2 }] },
            "shipment_total_overflow" => r with { ShippingCost=decimal.MaxValue },
            "long_sku" => r with { Lines=[r.Lines[0] with { Sku=new string('x',129) }] },
            "control_sku" => r with { Lines=[r.Lines[0] with { Sku="x\ny" }] },
            "long_hs" => r with { Lines=[r.Lines[0] with { HsCode=new string('1',33) }] },
            _ => r with { Lines=[r.Lines[0] with { Quantity=0 }] }
        };
        Assert.NotEmpty(QuoteService.Validate(invalid));
    }

    [Fact]
    public void Existing_case_insensitive_country_and_currency_inputs_remain_accepted()
    {
        var request = Request() with { ShipFrom="us",ShipTo="gb",Currency="gbp",Lines=[Request().Lines[0] with { CountryOfOrigin="cn" }] };
        Assert.Empty(QuoteService.Validate(request));
        var quote = new QuoteService().Calculate(request);
        Assert.Equal("US->GB",quote.Compliance!.Route); Assert.False(quote.Compliance.ShipmentReleaseApproved);
    }

    [Fact]
    public void Accepted_lowercase_destination_does_not_skip_existing_compliance_checks()
    {
        var result = ShipmentCompliance.Evaluate(Request() with { ShipTo="de" });
        Assert.Contains(result.RequiredChecks,x=>x.Contains("EU VAT/IOSS",StringComparison.Ordinal));
        Assert.False(result.ShipmentReleaseApproved);
    }
}
