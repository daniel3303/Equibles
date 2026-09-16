using Equibles.CommonStocks.Data.Models;
using Equibles.DelayedTrades.BusinessLogic.Listings;
using Equibles.DelayedTrades.Repositories;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>Contract: one listing per (ISIN, venue); a key claimed twice is refused, a listing without a unit contract is ignored.</summary>
public class DelayedTradeListingMapTests
{
    private static DelayedTradeListingReference Listing(
        string isin,
        string mic,
        decimal? multiplier = 1m,
        string currency = "EUR"
    ) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            isin,
            mic,
            "T",
            currency,
            multiplier,
            EquityIdentityState.Verified,
            true
        );

    [Fact]
    public void AClaimedTwiceKey_IsAmbiguous_AndResolvesToNothing()
    {
        var map = DelayedTradeListingMap.Build([
            Listing("PTEDP0AM0009", "XLIS"),
            Listing("PTEDP0AM0009", "XLIS"),
            Listing("PTGAL0AM0009", "XLIS"),
        ]);

        map.TryResolve("PTEDP0AM0009", "XLIS", out _).Should().BeFalse();
        map.IsAmbiguous("PTEDP0AM0009", "XLIS").Should().BeTrue();
        map.TryResolve("PTGAL0AM0009", "XLIS", out var galp).Should().BeTrue();
        galp.Isin.Should().Be("PTGAL0AM0009");
        map.AmbiguousCount.Should().Be(1);
        map.Count.Should().Be(1);
    }

    [Fact]
    public void AListingWithoutAUnitContract_IsNotMatched()
    {
        var map = DelayedTradeListingMap.Build([Listing("PTEDP0AM0009", "XLIS", multiplier: null)]);
        map.TryResolve("PTEDP0AM0009", "XLIS", out _).Should().BeFalse();
        map.IsAmbiguous("PTEDP0AM0009", "XLIS").Should().BeFalse();
    }

    [Fact]
    public void TheSameIsinOnTwoVenues_AreTwoKeys()
    {
        var map = DelayedTradeListingMap.Build([
            Listing("PTRIZ0AM0009", "XLIS"),
            Listing("PTRIZ0AM0009", "ENXL"),
        ]);
        map.Count.Should().Be(2);
        map.TryResolve("PTRIZ0AM0009", "ENXL", out var enxl).Should().BeTrue();
        enxl.MarketIdentifierCode.Should().Be("ENXL");
    }
}
