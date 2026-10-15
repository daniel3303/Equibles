using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

public class ShareCountSplitScopeTests
{
    private static readonly Guid Listing = Guid.NewGuid();
    private static readonly Guid Sibling = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 6, 29);

    private static StockSplit Split(
        Guid? listingId,
        StockSplitShareCountEvidence? evidence,
        decimal numerator = 1m,
        decimal denominator = 2m,
        int dayOffset = 0
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityListingId = listingId,
            EffectiveDate = Day.AddDays(dayOffset),
            Numerator = numerator,
            Denominator = denominator,
            ShareCountEvidence = evidence,
        };

    [Theory]
    [InlineData(null)]
    [InlineData(StockSplitShareCountEvidence.Confirmed)]
    public void ForListing_TheListingsOwnUncontradictedRow_Applies(
        StockSplitShareCountEvidence? evidence
    )
    {
        var own = Split(Listing, evidence);

        ShareCountSplitScope.ForListing([own], Listing).Should().Equal(own);
    }

    [Fact]
    public void ForListing_AContradictedRow_NeverApplies()
    {
        ShareCountSplitScope
            .ForListing([Split(Listing, StockSplitShareCountEvidence.Contradicted)], Listing)
            .Should()
            .BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForListing_AConfirmedRowElsewhere_IsAdoptedOnce(bool attributed)
    {
        var listingId = attributed ? Sibling : (Guid?)null;
        var confirmed = Split(listingId, StockSplitShareCountEvidence.Confirmed);
        var echo = Split(listingId, StockSplitShareCountEvidence.Confirmed, dayOffset: 3);

        ShareCountSplitScope.ForListing([confirmed, echo], Listing).Should().Equal(confirmed);
    }

    [Fact]
    public void ForListing_TheListingsOwnRowOfTheEvent_WinsOverAConfirmedSibling()
    {
        var own = Split(Listing, null);
        var sibling = Split(Sibling, StockSplitShareCountEvidence.Confirmed, dayOffset: 2);

        ShareCountSplitScope.ForListing([own, sibling], Listing).Should().Equal(own);
    }

    [Fact]
    public void ForListing_AContradictedRowLinkingTheEvent_StillMakesItOneEvent()
    {
        // The dropped factor at D+8 chains the own 2:1 and the sibling 2:1 at D+15 into one event.
        var own = Split(Listing, StockSplitShareCountEvidence.Confirmed, 2m, 1m);
        var factor = Split(Listing, StockSplitShareCountEvidence.Contradicted, 1061m, 1000m, 8);
        var sibling = Split(Sibling, StockSplitShareCountEvidence.Confirmed, 2m, 1m, 15);

        ShareCountSplitScope.ForListing([own, factor, sibling], Listing).Should().Equal(own);
    }

    [Fact]
    public void ForListing_AnUnjudgedSiblingRow_StaysOnItsListing()
    {
        ShareCountSplitScope.ForListing([Split(Sibling, null)], Listing).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(StockSplitShareCountEvidence.Confirmed, false)]
    [InlineData(StockSplitShareCountEvidence.Contradicted, false)]
    public void HasUnresolvedBasis_AnUnattributedRow_IsUnresolvedOnlyUntilJudged(
        StockSplitShareCountEvidence? evidence,
        bool unresolved
    )
    {
        ShareCountSplitScope
            .HasUnresolvedBasis([Split(null, evidence)], Listing, Day.AddDays(-1))
            .Should()
            .Be(unresolved);
    }

    private static EquityListing Venue(string country, string ticker) =>
        new() { Ticker = ticker, MarketCountryCode = country };

    private static StockSplit Listed(
        EquityListing listing,
        StockSplitShareCountEvidence? evidence,
        decimal numerator,
        decimal denominator,
        int dayOffset = 0
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Listing = listing,
            EquityListingId = listing.Id,
            PriceSeriesTicker = listing.Ticker,
            EffectiveDate = Day.AddDays(dayOffset),
            Numerator = numerator,
            Denominator = denominator,
            ShareCountEvidence = evidence,
        };

    [Fact]
    public void ForListedSeries_PrimaryWithOnlyAContradictedRow_AdoptsTheConfirmedSiblingEvent()
    {
        // HON 2026-06-29: the US series carries the spin-off price factor, the Euronext mirror the 1:2.
        var spinOff = Listed(
            Venue("US", "HON"),
            StockSplitShareCountEvidence.Contradicted,
            1907m,
            2000m
        );
        var reverse = Listed(Venue("IT", "1HON"), StockSplitShareCountEvidence.Confirmed, 1m, 2m);

        var scoped = ShareCountSplitScope.ForListedSeries([spinOff, reverse], "HON", "HON");

        scoped.Should().Equal(reverse);
        SplitAdjustment.ShareCountFactor(Day.AddDays(-1), scoped).Should().Be(0.5m);
    }

    [Fact]
    public void ForListedSeries_PrimaryWithNoRowOfTheEvent_AdoptsNothing()
    {
        // A receipt-ratio change: the ordinaries consolidate while the US receipt count stays put.
        var ordinary = Listed(Venue("JP", "8411"), StockSplitShareCountEvidence.Confirmed, 1m, 10m);
        var earlier = Listed(Venue("US", "MFG"), null, 2m, 1m, dayOffset: -400);

        ShareCountSplitScope
            .ForListedSeries([earlier, ordinary], "MFG", "MFG")
            .Should()
            .Equal(earlier);
    }

    [Fact]
    public void ForListedSeries_SiblingClass_NeverAdoptsAnotherListingsEvent()
    {
        var preferredFactor = Listed(
            Venue("US", "PW-PA"),
            StockSplitShareCountEvidence.Contradicted,
            1061m,
            1000m
        );
        var common = Listed(Venue("US", "PW"), StockSplitShareCountEvidence.Confirmed, 1m, 10m);

        ShareCountSplitScope
            .ForListedSeries([preferredFactor, common], "PW", "PW-PA")
            .Should()
            .Equal(preferredFactor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(StockSplitShareCountEvidence.Confirmed)]
    public void ForListedSeries_PrimaryWithAStandingRowOfTheEvent_KeepsItsOwnRows(
        StockSplitShareCountEvidence? evidence
    )
    {
        var us = Venue("US", "HON");
        var own = Listed(us, evidence, 2m, 1m);
        var factor = Listed(us, StockSplitShareCountEvidence.Contradicted, 1061m, 1000m, 3);
        var sibling = Listed(
            Venue("IT", "1HON"),
            StockSplitShareCountEvidence.Confirmed,
            2m,
            1m,
            1
        );

        ShareCountSplitScope
            .ForListedSeries([own, factor, sibling], "HON", "HON")
            .Should()
            .Equal(own, factor);
    }

    [Fact]
    public void ForListedSeries_AnEarlierPriceOnlyEvent_StaysAContradictedRowThatMovesNothing()
    {
        var us = Venue("US", "HON");
        var solstice = Listed(us, StockSplitShareCountEvidence.Contradicted, 1061m, 1000m, -242);
        var spinOff = Listed(us, StockSplitShareCountEvidence.Contradicted, 1907m, 2000m);
        var reverse = Listed(Venue("IT", "1HON"), StockSplitShareCountEvidence.Confirmed, 1m, 2m);

        var scoped = ShareCountSplitScope.ForListedSeries(
            [solstice, spinOff, reverse],
            "HON",
            "HON"
        );

        scoped.Should().Equal(solstice, reverse);
        SplitAdjustment.ShareCountFactor(Day.AddDays(-300), scoped).Should().Be(0.5m);
    }

    [Fact]
    public void ForListedSeries_AnotherIssuersConfirmedEvent_IsNeverAdopted()
    {
        var spinOff = Listed(
            Venue("US", "HON"),
            StockSplitShareCountEvidence.Contradicted,
            1907m,
            2000m
        );
        var otherIssuer = Listed(
            Venue("US", "XYZ"),
            StockSplitShareCountEvidence.Confirmed,
            1m,
            2m
        );
        otherIssuer.EquityIssuerId = Guid.NewGuid();

        ShareCountSplitScope
            .ForListedSeries([spinOff, otherIssuer], "HON", "HON")
            .Should()
            .Equal(spinOff);
    }

    [Theory]
    [InlineData(true, 0.5)]
    [InlineData(false, 1)]
    public void ForListedSeries_ByListingId_AdoptsOnlyForThePrimary(bool primary, double factor)
    {
        var us = Venue("US", "HON");
        var spinOff = Listed(us, StockSplitShareCountEvidence.Contradicted, 1907m, 2000m);
        var reverse = Listed(Venue("IT", "1HON"), StockSplitShareCountEvidence.Confirmed, 1m, 2m);

        var scoped = ShareCountSplitScope.ForListedSeries([spinOff, reverse], us.Id, primary);

        SplitAdjustment.ShareCountFactor(Day.AddDays(-1), scoped).Should().Be((decimal)factor);
    }
}
