using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

public class PriceSeriesSplitScopeTests
{
    private static readonly DateOnly LegacyDate = new(2025, 1, 2);
    private static readonly DateOnly PrimaryDate = new(2025, 2, 3);
    private static readonly DateOnly SecondaryDate = new(2025, 3, 4);

    [Fact]
    public void ForListing_Primary_IncludesOnlyExactAttribution()
    {
        var result = PriceSeriesSplitScope.ForListing(
            Splits(),
            primaryTicker: "BRK-A",
            listedTicker: "brk-a"
        );

        result.Select(split => split.EffectiveDate).Should().Equal(PrimaryDate);
    }

    [Fact]
    public void ForListing_Secondary_IncludesOnlyItsExactAttribution()
    {
        var result = PriceSeriesSplitScope.ForListing(
            Splits(),
            primaryTicker: "BRK-A",
            listedTicker: "BRK-B"
        );

        result.Select(split => split.EffectiveDate).Should().Equal(SecondaryDate);
    }

    private static StockSplit[] Splits() =>
        [
            new StockSplit { PriceSeriesTicker = null, EffectiveDate = LegacyDate },
            new StockSplit { PriceSeriesTicker = "BRK-A", EffectiveDate = PrimaryDate },
            new StockSplit { PriceSeriesTicker = "BRK-B", EffectiveDate = SecondaryDate },
            new StockSplit
            {
                PriceSeriesTicker = "BRK-C",
                EffectiveDate = new DateOnly(2025, 4, 5),
            },
        ];

    [Fact]
    public void NativeListing_RetainsRenameIdentityAndSeparatesEqualVenueSymbols()
    {
        var domestic = NativeSplit("US", "CURRENT");
        domestic.PriceSeriesTicker = "OLD";
        var foreign = NativeSplit("PT", "CURRENT");
        StockSplit[] events = [domestic, foreign, new() { PriceSeriesTicker = null }];

        PriceSeriesSplitScope.ForListing(events, "CURRENT", "CURRENT").Should().Equal(domestic);
        PriceSeriesSplitScope
            .ForListing(events, foreign.EquityListingId.Value)
            .Should()
            .Equal(foreign);
        SplitBasisResolver
            .TryResolveFactor(
                new DateOnly(2024, 1, 1),
                events[..2],
                null,
                "CURRENT",
                [],
                out var factor
            )
            .Should()
            .BeTrue();
        factor.Should().Be(2);
    }

    [Fact]
    public void EqualUsSymbols_RefuseUnqualifiedScopeAndBasis()
    {
        StockSplit[] events = [NativeSplit("US", "SAME"), NativeSplit("US", "SAME")];

        PriceSeriesSplitScope.ForListing(events, "SAME", "SAME").Should().BeEmpty();
        SplitBasisResolver
            .TryResolveFactor(new DateOnly(2024, 1, 1), events, null, "SAME", [], out var factor)
            .Should()
            .BeFalse();
        factor.Should().Be(1);
        PriceSeriesSplitScope
            .ForListing(events, events[0].EquityListingId.Value)
            .Should()
            .Equal(events[0]);
    }

    private static StockSplit NativeSplit(string country, string ticker)
    {
        var listing = new EquityListing { Ticker = ticker, MarketCountryCode = country };
        return new StockSplit
        {
            Listing = listing,
            EquityListingId = listing.Id,
            PriceSeriesTicker = ticker,
            EffectiveDate = new DateOnly(2025, 1, 1),
            Numerator = 2,
            Denominator = 1,
            PriceAdjustmentAppliedTime = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    [Fact]
    public void UnknownAttribution_BoundsPricesWithoutSupplyingARestatementRatio()
    {
        var unknown = new StockSplit
        {
            EffectiveDate = LegacyDate,
            Numerator = 20,
            Denominator = 1,
        };
        var own = NativeSplit("US", "CURRENT");
        var foreign = NativeSplit("PT", "CURRENT");
        StockSplit[] events = [unknown, own, foreign];

        PriceSeriesSplitScope.ForPriceComparison(events, "CURRENT").Should().Equal(unknown, own);
        PriceSeriesSplitScope.ForPriceComparison(events, "SIBLING").Should().Equal(unknown);
        PriceSeriesSplitScope.ForListing(events, own.EquityListingId.Value).Should().Equal(own);
    }

    [Fact]
    public void DuplicateBeforeObservation_DoesNotSuppressAValidLaterSplit()
    {
        var oldNative = NativeSplit("US", "CURRENT");
        oldNative.EffectiveDate = new DateOnly(2024, 1, 1);
        var oldUnresolved = new StockSplit
        {
            PriceSeriesTicker = "CURRENT",
            EffectiveDate = oldNative.EffectiveDate,
            Numerator = 2,
            Denominator = 1,
        };
        var later = NativeSplit("US", "CURRENT");
        later.Listing = oldNative.Listing;
        later.EquityListingId = oldNative.EquityListingId;
        later.EffectiveDate = new DateOnly(2026, 1, 1);
        StockSplit[] events = [oldNative, oldUnresolved, later];
        var observedOn = new DateOnly(2025, 1, 1);
        PriceSeriesSplitScope.HasUnresolvedBasis(events, "CURRENT", observedOn).Should().BeFalse();
        var scoped = PriceSeriesSplitScope.ForListing(events, "CURRENT", "CURRENT");
        scoped.Should().Equal(later);
        SplitAdjustment.ShareCountFactor(observedOn, scoped).Should().Be(2);
        PriceSeriesSplitScope
            .HasUnresolvedBasis(events, "CURRENT", new DateOnly(2023, 1, 1))
            .Should()
            .BeTrue();
    }
}
