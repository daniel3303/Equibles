using System.Globalization;
using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpFormatPriceTests
{
    [Theory]
    [InlineData("123.456", "123.46")] // at or above $1 keeps the classic two decimals
    [InlineData("1", "1.00")]
    [InlineData("0.99", "0.99")] // two decimals still carry two significant digits here
    [InlineData("0.0072", "0.0072")] // the DPLS close that rendered as 0.01 (39% overstated)
    [InlineData("0.0060", "0.0060")] // must stay distinguishable from 0.0072 in one table
    [InlineData("0.06", "0.060")]
    [InlineData("0.000012", "0.000012")]
    [InlineData("0.000000004", "0.00000000")] // decimals cap at 8 — never an unbounded tail
    [InlineData("0", "0.00")]
    public void Price_AdaptsDecimalsToMagnitude(string value, string expected)
    {
        McpFormat.Price(decimal.Parse(value, CultureInfo.InvariantCulture)).Should().Be(expected);
    }

    [Fact]
    public void Price_SubDollarRange_NeverCollapsesHighAndLow()
    {
        // The guard the bug report asked for: a rendered OHLC row may not report
        // high == low when the source row has high != low (0.0060–0.0072 was a 20% range
        // served as flat).
        McpFormat.Price(0.0072m).Should().NotBe(McpFormat.Price(0.0060m));
    }

    [Fact]
    public void PriceOrDash_Null_ReturnsDash()
    {
        McpFormat.PriceOrDash(null).Should().Be("—");
        McpFormat.PriceOrDash(0.0072m).Should().Be("0.0072");
    }

    [Theory]
    [InlineData("0.0012", "+0.0012")] // a sub-dollar move must not round to a signless 0.00
    [InlineData("-0.0012", "-0.0012")]
    [InlineData("2.5", "+2.50")]
    [InlineData("-2.5", "-2.50")]
    [InlineData("0", "0.00")]
    public void SignedPrice_KeepsSignAndSubDollarPrecision(string value, string expected)
    {
        McpFormat
            .SignedPrice(decimal.Parse(value, CultureInfo.InvariantCulture))
            .Should()
            .Be(expected);
    }
}
