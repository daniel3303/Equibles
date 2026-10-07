using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.CommonStocks;

public class SecondaryTickerPolicyTests
{
    private readonly EquityIssuer _berkshire = Equibles.TestSupport.EquityIssuerSeed.Create(
        Ticker: "BRK-B",
        Name: "Berkshire Hathaway Inc.",
        SecondaryTickers: ["BRK-A"]
    );

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
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GOOGL",
            SecondaryTickers: null
        );

        SecondaryTickerPolicy.ResolveListedTicker(stock, "GOOG").Should().BeNull();
    }

    [Theory]
    [InlineData("BRK-A")]
    [InlineData("brk.a")]
    [InlineData("BRK-B")]
    [InlineData("BRK.B")]
    public void ResolveExactUsListing_ShareClassResolvesOnlyItsOwnListing(string requested)
    {
        var listing = SecondaryTickerPolicy.ResolveExactUsListing(_berkshire, requested);

        listing.Should().NotBeNull();
        listing.Ticker.Should().Be(requested.ToUpperInvariant().Replace('.', '-'));
    }

    [Fact]
    public void ResolveExactUsListing_SecondaryFundSeriesResolvesItsOwnReferenceListing()
    {
        EquityIssuer trust = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "FNDA",
            SecondaryTickers: ["SCHD"],
            ReferenceTickers: ["FNDA", "SCHD"]
        );

        var listing = SecondaryTickerPolicy.ResolveExactUsListing(trust, "SCHD");

        listing.Should().NotBeNull();
        listing.Ticker.Should().Be("SCHD");
        listing.Id.Should().NotBe(trust.Presentation.EquityListingId);
    }

    [Fact]
    public void ResolveExactUsListing_RetiredOrUnlistedSymbol_ReturnsNull()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GOOGL",
            SecondaryTickers: ["GOOG"]
        );
        stock
            .Securities.SelectMany(security => security.Listings)
            .Single(row => row.Ticker == "GOOG")
            .Active = false;
        Equibles.CommonStocks.Data.Helpers.UsEquityDirectory.GetOrAddListing(stock, "GOOGW");

        SecondaryTickerPolicy.ResolveExactUsListing(stock, "GOOG").Should().BeNull();
        SecondaryTickerPolicy.ResolveExactUsListing(stock, "GOOGW").Should().BeNull();
        SecondaryTickerPolicy
            .ResolveExactUsListing(stock, "GOOGL")
            .Should()
            .BeSameAs(stock.Presentation.Listing);
    }
}
