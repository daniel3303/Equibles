using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CorporateActions;

[Collection(ParadeDbCollection.Name)]
public class NativeSplitCaptureTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private StockSplitCaptureManager Capture() =>
        new(new StockSplitRepository(DbContext), new EquityIssuerRepository(DbContext));

    private CorporateActionPriceReconciliationManager Reconcile() =>
        new(
            new StockSplitRepository(DbContext),
            new CashDividendRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new CorporateActionPriceReconciliationCursorRepository(DbContext)
        );

    private static CapturedSplit Event(decimal numerator = 2m) =>
        new()
        {
            EffectiveDate = new DateOnly(2025, 1, 1),
            Numerator = numerator,
            Denominator = 1,
            Source = StockSplitSource.Yahoo,
        };

    [Fact]
    public async Task RecycledRecordedSymbol_RetainsBothEventsWithoutCombiningTheirRatios()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        var former = new EquityListing
        {
            Ticker = "FORMER",
            MarketCountryCode = "US",
            MarketIdentifierCode = "XNAS",
            Active = false,
            Security = issuer.Presentation.Listing.Security,
        };
        former.TickerAliases.Add(
            new EquityListingTickerAlias
            {
                Listing = former,
                Ticker = "SAME",
                EvidenceSource = "recorded-listing",
            }
        );
        var original = new StockSplit
        {
            EquityIssuerId = issuer.Id,
            PriceSeriesTicker = "SAME",
            EffectiveDate = Event().EffectiveDate,
            Numerator = 2,
            Denominator = 1,
            PriceAdjustmentAppliedTime = new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc),
        };
        DbContext.AddRange(issuer, former, original);
        await DbContext.SaveChangesAsync();
        (await Capture().Capture(issuer.Id, "SAME", [Event()])).Should().Be(1);
        var rows = await DbContext.Set<StockSplit>().ToListAsync();
        rows.Should().HaveCount(2);
        original.EquityListingId.Should().BeNull();
        var asOf = new DateOnly(2024, 12, 31);
        PriceSeriesSplitScope.HasUnresolvedBasis(rows, "SAME", asOf).Should().BeTrue();
        PriceSeriesSplitScope.ForListing(rows, "SAME", "SAME").Should().BeEmpty();
        SplitBasisResolver
            .TryResolveFactor(asOf, rows, null, "SAME", [], out var factor)
            .Should()
            .BeFalse();
        factor.Should().Be(1);
    }

    [Fact]
    public async Task HistoricalCapture_RequiresTheUnchangedCutoffAndExcludesLaterActions()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "GONE");
        var listing = issuer.Presentation.Listing;
        listing.Active = false;
        listing.DelistedOn = new DateOnly(2025, 1, 2);
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        var manager = Capture();
        var later = Event();
        later.EffectiveDate = listing.DelistedOn.Value.AddDays(1);
        (
            await manager.CaptureForHistoricalListing(
                issuer.Id,
                listing.Id,
                "GONE",
                listing.DelistedOn.Value,
                [Event(), later]
            )
        )
            .Should()
            .Be(1);
        (
            await manager.CaptureForHistoricalListing(
                issuer.Id,
                listing.Id,
                "GONE",
                listing.DelistedOn.Value.AddDays(1),
                [Event(3)]
            )
        )
            .Should()
            .Be(0);
        listing.Active = true;
        var retainedCutoff = listing.DelistedOn.Value;
        listing.DelistedOn = null;
        await DbContext.SaveChangesAsync();
        (
            await manager.CaptureForHistoricalListing(
                issuer.Id,
                listing.Id,
                "GONE",
                retainedCutoff,
                [Event(4)]
            )
        )
            .Should()
            .Be(0);
        (await DbContext.Set<StockSplit>().SingleAsync()).Numerator.Should().Be(2);
    }

    [Fact]
    public async Task Rename_PreservesSourceSymbolAndListing_AndReconcilesTheCurrentSeries()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OLD");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        var listingId = issuer.Presentation.EquityListingId;
        (await Capture().Capture(issuer.Id, "OLD", [Event()])).Should().Be(1);
        DbContext.ChangeTracker.Clear();
        var listing = await DbContext.Set<EquityListing>().SingleAsync();
        listing.Ticker = "CURRENT";
        await DbContext.SaveChangesAsync();
        (await Capture().CaptureForListing(issuer.Id, listingId, "CURRENT", [Event(3)]))
            .Should()
            .Be(1);
        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.Set<StockSplit>().SingleAsync();
        stored.PriceSeriesTicker.Should().Be("OLD");
        stored.EquityListingId.Should().Be(listingId);
        stored.Numerator.Should().Be(3);
        var selected = (
            await Reconcile().SelectPendingSeries(10, new DateOnly(2025, 1, 3))
        ).Series.Single();
        selected.ListedTicker.Should().Be("CURRENT");
        selected.Splits.Single().EquityListingId.Should().Be(listingId);
        (
            await Reconcile()
                .StampApplied(selected, new DateTime(2025, 1, 3, 12, 0, 0, DateTimeKind.Utc))
        )
            .Should()
            .Be(1);
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<StockSplit>().SingleAsync())
            .PriceAdjustmentAppliedTime.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task UnknownOriginalEvent_RemainsUnchangedBesideAnExactPrimaryObservation()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "CURRENT");
        var original = new StockSplit
        {
            EquityIssuerId = issuer.Id,
            EffectiveDate = Event().EffectiveDate,
            Numerator = 20,
            Denominator = 1,
            Source = StockSplitSource.Yahoo,
            PriceAdjustmentAppliedTime = DateTime.UtcNow,
        };
        DbContext.AddRange(issuer, original);
        await DbContext.SaveChangesAsync();
        var originalId = original.Id;
        var marker = original.PriceAdjustmentAppliedTime;
        (await Capture().Capture(issuer.Id, "CURRENT", [Event()])).Should().Be(1);
        DbContext.ChangeTracker.Clear();
        var retained = await DbContext.Set<StockSplit>().SingleAsync(row => row.Id == originalId);
        retained.EquityListingId.Should().BeNull();
        retained.PriceSeriesTicker.Should().BeNull();
        retained.Numerator.Should().Be(20);
        retained.PriceAdjustmentAppliedTime.Should().Be(marker);
        (await DbContext.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ExactSourceAttribution_AlonePreservesAnAppliedDefinition()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "CURRENT");
        var applied = new DateTime(2025, 1, 3, 12, 0, 0, DateTimeKind.Utc);
        var original = new StockSplit
        {
            EquityIssuerId = issuer.Id,
            PriceSeriesTicker = "CURRENT",
            EffectiveDate = Event().EffectiveDate,
            Numerator = 2,
            Denominator = 1,
            Source = StockSplitSource.Yahoo,
            PriceAdjustmentAppliedTime = applied,
        };
        DbContext.AddRange(issuer, original);
        await DbContext.SaveChangesAsync();
        (await Capture().Capture(issuer.Id, "CURRENT", [Event()])).Should().Be(1);
        DbContext.ChangeTracker.Clear();
        var retained = await DbContext.Set<StockSplit>().SingleAsync();
        retained.Id.Should().Be(original.Id);
        retained.EquityListingId.Should().Be(issuer.Presentation.EquityListingId);
        retained.PriceAdjustmentAppliedTime.Should().Be(applied);
    }

    [Fact]
    public async Task EqualVenueSymbols_RequireExactListing_AndQueueSeparately()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        var foreign = new EquityListing
        {
            Ticker = "SAME",
            MarketCountryCode = "PT",
            MarketIdentifierCode = "XLIS",
            Security = new EquitySecurity { Issuer = issuer },
        };
        DbContext.AddRange(issuer, foreign);
        await DbContext.SaveChangesAsync();
        (await Capture().Capture(issuer.Id, "SAME", [Event()])).Should().Be(1);
        (await Capture().CaptureForListing(issuer.Id, foreign.Id, "SAME", [Event(3)]))
            .Should()
            .Be(1);
        (await Reconcile().SelectPendingSeries(10, new DateOnly(2025, 1, 3)))
            .Series.Select(series => series.EquityListingId)
            .Should()
            .BeEquivalentTo(new Guid[] { issuer.Presentation.EquityListingId, foreign.Id });
        DbContext.Add(
            new EquityListing
            {
                Ticker = "SAME",
                MarketCountryCode = "US",
                MarketIdentifierCode = "XNAS",
                Security = issuer.Presentation.Listing.Security,
            }
        );
        await DbContext.SaveChangesAsync();
        (await Capture().Capture(issuer.Id, "SAME", [Event(4)])).Should().Be(0);
        (await Capture().CaptureForListing(issuer.Id, foreign.Id, "WRONG", [Event(4)]))
            .Should()
            .Be(0);
        (await DbContext.Set<StockSplit>().CountAsync()).Should().Be(2);
    }
}
