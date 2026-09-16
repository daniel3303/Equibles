using Equibles.CommonStocks.Data.Models;
using Equibles.DelayedTrades.BusinessLogic.Bars;
using Equibles.DelayedTrades.BusinessLogic.Sessions;
using Equibles.Yahoo.Data.Prices;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>
/// Contract: a settled, valid session bar is inserted as a venue-owned row; a feed row on the same
/// basis is overwritten exactly and its ownership flips, keeping a rebased adjusted close; a feed row
/// on another split basis is left to the reconcile; a venue row is re-derived in place and an equal
/// one is untouched; an invalid, unsettled or identity-stale bar never reaches the store.
/// </summary>
public class DelayedTradeBarWriterTests
{
    private const string Isin = "PTPTC0AM0009";
    private static readonly DateOnly Session = new(2026, 9, 15);
    private static readonly DateOnly LocalToday = new(2026, 9, 16);

    private static DelayedTradeSessionBar Bar(
        decimal open = 0.0891m,
        decimal high = 0.0899m,
        decimal low = 0.0890m,
        decimal close = 0.0890m,
        long volume = 677_437,
        DateOnly? date = null,
        string venue = "XLIS",
        string isin = Isin
    ) => new(isin, venue, date ?? Session, open, high, low, close, volume, volume, 51, 0, null);

    private static DelayedTradeBarWriter Writer(DelayedTradeDbFixture db) =>
        new(db.ScopeFactory, NullLogger<DelayedTradeBarWriter>.Instance);

    [Fact]
    public async Task ANewSession_IsInsertedAsAVenueOwnedRow()
    {
        var db = new DelayedTradeDbFixture();
        var issuer = db.SeedListing("PHR", Isin);

        var outcome = await Writer(db)
            .Write(db.Reference(issuer), Bar(), "EUR", LocalToday, CancellationToken.None);

        outcome.Should().Be(DelayedTradeBarOutcome.Inserted);
        var row = db.Prices(db.ListingOf(issuer).Id).Single();
        row.SourceTicker.Should().Be("XLIS:PTPTC0AM0009");
        row.Open.Should().Be(0.0891m);
        row.Close.Should().Be(0.0890m);
        row.AdjustedClose.Should().Be(0.0890m);
        row.Volume.Should().Be(677_437L);
        VenuePriceSource.IsVenueOwned(row).Should().BeTrue();
    }

    [Fact]
    public async Task ASameBasisFeedRow_IsOverwrittenAndFlipsOwnership_KeepingARebasedAdjustedClose()
    {
        var db = new DelayedTradeDbFixture();
        var issuer = db.SeedListing("PHR", Isin);
        var listing = db.ListingOf(issuer);
        db.SeedPrice(
            listing,
            Session,
            close: 0.0892m,
            sourceTicker: "PHR",
            volume: 600_000,
            adjustedClose: 0.0800m
        );

        var outcome = await Writer(db)
            .Write(db.Reference(issuer), Bar(), "EUR", LocalToday, CancellationToken.None);

        outcome.Should().Be(DelayedTradeBarOutcome.OverwroteYahoo);
        var row = db.Prices(listing.Id).Single();
        row.SourceTicker.Should().Be("XLIS:PTPTC0AM0009");
        row.Close.Should().Be(0.0890m);
        row.Volume.Should().Be(677_437L);
        row.AdjustedClose.Should()
            .Be(0.0800m, "an adjusted close the feed rebased is the feed's to keep");
    }

    [Fact]
    public async Task AnUnadjustedFeedRow_TakesTheVenueCloseAsItsAdjustedClose()
    {
        var db = new DelayedTradeDbFixture();
        var issuer = db.SeedListing("PHR", Isin);
        var listing = db.ListingOf(issuer);
        db.SeedPrice(listing, Session, close: 0.0892m, sourceTicker: "PHR");

        await Writer(db)
            .Write(db.Reference(issuer), Bar(), "EUR", LocalToday, CancellationToken.None);

        db.Prices(listing.Id).Single().AdjustedClose.Should().Be(0.0890m);
    }

    [Fact]
    public async Task AFeedRowOnAnotherSplitBasis_IsLeftToTheReconcile()
    {
        var db = new DelayedTradeDbFixture();
        var issuer = db.SeedListing("PHR", Isin);
        var listing = db.ListingOf(issuer);
        db.SeedPrice(listing, Session, close: 0.89m, sourceTicker: "PHR", volume: 60_000);

        var outcome = await Writer(db)
            .Write(db.Reference(issuer), Bar(), "EUR", LocalToday, CancellationToken.None);

        outcome.Should().Be(DelayedTradeBarOutcome.SkippedBasis);
        var row = db.Prices(listing.Id).Single();
        row.Close.Should().Be(0.89m);
        row.SourceTicker.Should().Be("PHR");
    }

    [Fact]
    public async Task AVenueRow_IsRederivedInPlace_AndAnEqualBarIsUnchanged()
    {
        var db = new DelayedTradeDbFixture();
        var issuer = db.SeedListing("PHR", Isin);
        var listing = db.ListingOf(issuer);
        var writer = Writer(db);
        await writer.Write(db.Reference(issuer), Bar(), "EUR", LocalToday, CancellationToken.None);

        (await writer.Write(db.Reference(issuer), Bar(), "EUR", LocalToday, CancellationToken.None))
            .Should()
            .Be(DelayedTradeBarOutcome.Unchanged);
        (
            await writer.Write(
                db.Reference(issuer),
                Bar(volume: 677_000),
                "EUR",
                LocalToday,
                CancellationToken.None
            )
        )
            .Should()
            .Be(DelayedTradeBarOutcome.Rederived, "a next-day cancellation lowers the volume");

        db.Prices(listing.Id).Single().Volume.Should().Be(677_000L);
    }

    [Fact]
    public async Task AnInvalidOrUnsettledBar_NeverReachesTheStore()
    {
        var db = new DelayedTradeDbFixture();
        var issuer = db.SeedListing("PHR", Isin);
        var writer = Writer(db);

        (
            await writer.Write(
                db.Reference(issuer),
                Bar(high: 0.0880m),
                "EUR",
                LocalToday,
                CancellationToken.None
            )
        )
            .Should()
            .Be(DelayedTradeBarOutcome.SkippedInvalid);
        (await writer.Write(db.Reference(issuer), Bar(), "EUR", Session, CancellationToken.None))
            .Should()
            .Be(DelayedTradeBarOutcome.Unsettled, "the session's own local date is not settled");

        db.Prices(db.ListingOf(issuer).Id).Should().BeEmpty();
    }

    [Fact]
    public async Task AStaleIdentity_IsSkipped()
    {
        var db = new DelayedTradeDbFixture();
        var issuer = db.SeedListing("PHR", Isin);
        var reference = db.Reference(issuer);
        using (var context = db.NewContext())
        {
            var listing = context
                .Set<EquityListing>()
                .Single(row => row.Id == reference.EquityListingId);
            listing.Active = false;
            context.SaveChanges();
        }

        (await Writer(db).Write(reference, Bar(), "EUR", LocalToday, CancellationToken.None))
            .Should()
            .Be(DelayedTradeBarOutcome.SkippedIdentity);
        (await Writer(db).Write(reference, Bar(), "GBX", LocalToday, CancellationToken.None))
            .Should()
            .Be(DelayedTradeBarOutcome.SkippedIdentity, "a pence file never writes a euro listing");
    }

    [Fact]
    public async Task APenceFile_WritesAPenceListingUnconverted()
    {
        var db = new DelayedTradeDbFixture();
        var issuer = db.SeedListing(
            "LLOY",
            "GB0008706128",
            mic: "XLON",
            country: "GB",
            currency: "GBP",
            multiplier: 0.01m
        );

        var outcome = await Writer(db)
            .Write(
                db.Reference(issuer),
                Bar(
                    open: 60m,
                    high: 61m,
                    low: 59m,
                    close: 60.5m,
                    isin: "GB0008706128",
                    venue: "XLON"
                ),
                "GBX",
                LocalToday,
                CancellationToken.None
            );

        outcome.Should().Be(DelayedTradeBarOutcome.Inserted);
        db.Prices(db.ListingOf(issuer).Id)
            .Single()
            .Close.Should()
            .Be(60.5m, "the store keeps the quotation unit; display multiplies");
    }
}
