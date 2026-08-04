using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.CommonStocks;

public class SecondaryTickerPolicyTests
{
    private readonly CommonStock _berkshire = new()
    {
        Ticker = "BRK-B",
        Name = "Berkshire Hathaway Inc.",
        SecondaryTickers = ["BRK-A"],
    };

    [Theory]
    [InlineData("BRK-B", "BRK-B")]
    [InlineData("brk-b", "BRK-B")]
    [InlineData("BRK.B", "BRK-B")]
    [InlineData("BRK-A", "BRK-A")]
    [InlineData("brk.a", "BRK-A")]
    public void ResolveListedTicker_ReturnsTheCanonicalRequestedListing(
        string requested,
        string expected
    )
    {
        SecondaryTickerPolicy.ResolveListedTicker(_berkshire, requested).Should().Be(expected);
    }

    [Fact]
    public void ResolveListedTicker_UnknownSymbol_ReturnsNull()
    {
        SecondaryTickerPolicy.ResolveListedTicker(_berkshire, "BRK-C").Should().BeNull();
    }

    [Fact]
    public void ResolveListedTicker_NullSecondaryCollection_DoesNotThrow()
    {
        var stock = new CommonStock { Ticker = "GOOGL", SecondaryTickers = null };

        SecondaryTickerPolicy.ResolveListedTicker(stock, "GOOG").Should().BeNull();
    }
}
