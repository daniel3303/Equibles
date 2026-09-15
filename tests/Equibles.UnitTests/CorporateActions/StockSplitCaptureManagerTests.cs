using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.UnitTests.CorporateActions;

public class StockSplitCaptureManagerTests
{
    [Theory]
    [InlineData(StockSplitSource.Yahoo, StockSplitSource.External, true)]
    [InlineData(StockSplitSource.External, StockSplitSource.Yahoo, false)]
    [InlineData(StockSplitSource.SecFiling, StockSplitSource.External, false)]
    [InlineData(StockSplitSource.Manual, StockSplitSource.SecFiling, false)]
    [InlineData(StockSplitSource.Yahoo, StockSplitSource.Manual, true)]
    public async Task Capture_SourcePrecedence_PreservesAuthoritativeRatio(
        StockSplitSource storedSource,
        StockSplitSource incomingSource,
        bool replaces
    )
    {
        await using var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "SPLT"
        );
        var applied = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        var existing = new StockSplit
        {
            EquityIssuerId = stock.Id,
            PriceSeriesTicker = stock.Presentation.Listing.Ticker,
            EffectiveDate = new DateOnly(2025, 1, 29),
            Numerator = 1m,
            Denominator = 2m,
            Source = storedSource,
            PriceAdjustmentAppliedTime = applied,
        };
        context.AddRange(stock, existing);
        await context.SaveChangesAsync();
        var incoming = new CapturedSplit
        {
            EffectiveDate = existing.EffectiveDate,
            Numerator = 1m,
            Denominator = 60000m,
            Source = incomingSource,
        };

        var changed = await NewManager(context)
            .Capture(stock.Id, stock.Presentation.Listing.Ticker, [incoming]);

        changed.Should().Be(replaces ? 1 : 0);
        context.ChangeTracker.Clear();
        var actual = await context.Set<StockSplit>().SingleAsync();
        actual.Denominator.Should().Be(replaces ? 60000m : 2m);
        actual.Source.Should().Be(replaces ? incomingSource : storedSource);
        actual.PriceAdjustmentAppliedTime.Should().Be(replaces ? null : applied);
    }

    [Fact]
    public async Task Capture_SourceUpgradeWithoutRatioChange_RevalidatesAppliedMarker()
    {
        await using var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "SPLT"
        );
        context.Add(stock);
        await context.SaveChangesAsync();
        var manager = NewManager(context);
        await manager.Capture(stock.Id, stock.Presentation.Listing.Ticker, [Split()]);
        var existing = await context.Set<StockSplit>().SingleAsync();
        var applied = DateTime.UtcNow;
        existing.PriceAdjustmentAppliedTime = applied;
        await context.SaveChangesAsync();
        var incoming = Split();
        incoming.Source = StockSplitSource.External;

        (await manager.Capture(stock.Id, stock.Presentation.Listing.Ticker, [incoming]))
            .Should()
            .Be(1);
        context.ChangeTracker.Clear();
        existing = await context.Set<StockSplit>().SingleAsync();
        existing.Source.Should().Be(StockSplitSource.External);
        existing.PriceAdjustmentAppliedTime.Should().BeNull();
        (await manager.Capture(stock.Id, stock.Presentation.Listing.Ticker, [Split(20m)]))
            .Should()
            .Be(0);
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
        context.Database.EnsureCreated();
        return context;
    }

    private static StockSplitCaptureManager NewManager(EquiblesFinancialDbContext context) =>
        new(new StockSplitRepository(context), new EquityIssuerRepository(context));

    private static CapturedSplit Split(decimal numerator = 2m) =>
        new()
        {
            EffectiveDate = new DateOnly(2024, 2, 1),
            Numerator = numerator,
            Denominator = 1m,
            Source = StockSplitSource.Yahoo,
        };

    [Fact]
    public async Task Capture_CurrentSecondaryTarget_WritesExactSeriesAction()
    {
        await using var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "GOOG",
            SecondaryTickers: ["GOOGL"]
        );
        context.Add(stock);
        await context.SaveChangesAsync();

        var secondaryWrite = await NewManager(context).Capture(stock.Id, "GOOGL", [Split()]);

        secondaryWrite.Should().Be(1);
        var secondary = await context.Set<StockSplit>().SingleAsync();
        secondary.PriceSeriesTicker.Should().Be("GOOGL");

        var currentWrite = await NewManager(context).Capture(stock.Id, "GOOG", [Split()]);

        currentWrite.Should().Be(1);
        (await context.Set<StockSplit>().OrderBy(split => split.PriceSeriesTicker).ToListAsync())
            .Select(split => split.PriceSeriesTicker)
            .Should()
            .Equal("GOOG", "GOOGL");
    }

    [Fact]
    public async Task Capture_SameDateAlreadyAttributedToSibling_AddsIndependentPrimaryAction()
    {
        await using var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "GOOG",
            SecondaryTickers: ["GOOGL"]
        );
        context.Add(stock);
        context.Add(
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                PriceSeriesTicker = "GOOGL",
                EffectiveDate = new DateOnly(2024, 2, 1),
                Numerator = 2m,
                Denominator = 1m,
                Source = StockSplitSource.Yahoo,
            }
        );
        await context.SaveChangesAsync();

        var changes = await NewManager(context).Capture(stock.Id, "GOOG", [Split(20m)]);

        changes.Should().Be(1);
        context.ChangeTracker.Clear();
        var stored = await context
            .Set<StockSplit>()
            .OrderBy(split => split.PriceSeriesTicker)
            .ToListAsync();
        stored.Should().HaveCount(2);
        stored[0].PriceSeriesTicker.Should().Be("GOOG");
        stored[0].Numerator.Should().Be(20m);
        stored[1].PriceSeriesTicker.Should().Be("GOOGL");
        stored[1].Numerator.Should().Be(2m);
    }

    [Fact]
    public async Task Capture_UnattributedLegacyRow_RemainsIntactBesideNewSourceObservations()
    {
        await using var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "GOOG",
            SecondaryTickers: ["GOOGL"]
        );
        context.Add(stock);
        context.Add(
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                PriceSeriesTicker = null,
                EffectiveDate = new DateOnly(2024, 2, 1),
                Numerator = 2m,
                Denominator = 1m,
                Source = StockSplitSource.Yahoo,
                PriceAdjustmentAppliedTime = DateTime.UtcNow,
            }
        );
        await context.SaveChangesAsync();

        var secondaryObservation = await NewManager(context)
            .Capture(stock.Id, "GOOGL", [Split(20m)]);

        secondaryObservation.Should().Be(1);
        context.ChangeTracker.Clear();
        var stillUnattributed = await context
            .Set<StockSplit>()
            .SingleAsync(split => split.PriceSeriesTicker == null);
        stillUnattributed.PriceSeriesTicker.Should().BeNull();
        stillUnattributed.Numerator.Should().Be(2m);
        stillUnattributed.PriceAdjustmentAppliedTime.Should().NotBeNull();
        var secondary = await context
            .Set<StockSplit>()
            .SingleAsync(split => split.PriceSeriesTicker == "GOOGL");
        secondary.Numerator.Should().Be(20m);

        var primaryObservation = await NewManager(context).Capture(stock.Id, "GOOG", [Split(20m)]);

        primaryObservation.Should().Be(1);
        context.ChangeTracker.Clear();
        var attributed = await context
            .Set<StockSplit>()
            .SingleAsync(split => split.PriceSeriesTicker == "GOOG");
        attributed.PriceSeriesTicker.Should().Be("GOOG");
        attributed.Numerator.Should().Be(20m);
        attributed.PriceAdjustmentAppliedTime.Should().BeNull();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(3);
        (await context.Set<StockSplit>().SingleAsync(row => row.PriceSeriesTicker == null))
            .Numerator.Should()
            .Be(2m);
    }

    private static CapturedSplit At(
        DateOnly date,
        decimal numerator = 1m,
        decimal denominator = 50m,
        StockSplitSource source = StockSplitSource.Yahoo
    ) =>
        new()
        {
            EffectiveDate = date,
            Numerator = numerator,
            Denominator = denominator,
            Source = source,
        };

    private static async Task<(EquiblesFinancialDbContext Context, EquityIssuer Stock)> SeedIssuer(
        string ticker = "DUPE",
        List<string> secondaryTickers = null
    )
    {
        var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            SecondaryTickers: secondaryTickers
        );
        context.Add(stock);
        await context.SaveChangesAsync();
        return (context, stock);
    }

    private static readonly DateOnly Day = new(2026, 1, 1);

    [Fact]
    public async Task Capture_SameRatioWithinTheWindow_IsOneEventAtTheLaterDate()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var manager = NewManager(context);
        var payload = new[] { At(Day), At(Day.AddDays(1)) };

        (await manager.Capture(stock.Id, "DUPE", payload)).Should().Be(2);
        (await manager.Capture(stock.Id, "DUPE", payload)).Should().Be(0);

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.EffectiveDate.Should().Be(Day.AddDays(1));
        stored.Denominator.Should().Be(50m);
    }

    [Fact]
    public async Task Capture_SameEventArrivingInEitherOrder_EndsIdentical()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var manager = NewManager(context);

        await manager.Capture(stock.Id, "DUPE", [At(Day.AddDays(1)), At(Day)]);

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.EffectiveDate.Should().Be(Day.AddDays(1));
    }

    [Fact]
    public async Task Capture_LowerSourceWithinTheWindow_WritesNothing()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var manager = NewManager(context);
        await manager.Capture(
            stock.Id,
            "DUPE",
            [At(Day.AddDays(3), source: StockSplitSource.External)]
        );
        var applied = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        (await context.Set<StockSplit>().SingleAsync()).PriceAdjustmentAppliedTime = applied;
        await context.SaveChangesAsync();

        (await manager.Capture(stock.Id, "DUPE", [At(Day)])).Should().Be(0);

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.EffectiveDate.Should().Be(Day.AddDays(3));
        stored.Source.Should().Be(StockSplitSource.External);
        stored.PriceAdjustmentAppliedTime.Should().Be(applied);
    }

    [Fact]
    public async Task Capture_HigherSourceWithinTheWindow_AdoptsItsDateAndReopensReconciliation()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var manager = NewManager(context);
        await manager.Capture(stock.Id, "DUPE", [At(Day)]);
        (await context.Set<StockSplit>().SingleAsync()).PriceAdjustmentAppliedTime =
            DateTime.UtcNow;
        await context.SaveChangesAsync();

        (
            await manager.Capture(
                stock.Id,
                "DUPE",
                [At(Day.AddDays(3), source: StockSplitSource.External)]
            )
        )
            .Should()
            .Be(1);

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.EffectiveDate.Should().Be(Day.AddDays(3));
        stored.Source.Should().Be(StockSplitSource.External);
        stored.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Fact]
    public async Task Capture_SameRatioOutsideTheWindow_StaysTwoEvents()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;

        await NewManager(context)
            .Capture(
                stock.Id,
                "DUPE",
                [At(Day), At(Day.AddDays(StockSplitCaptureManager.SameEventWindowDays + 1))]
            );

        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Capture_DifferentRatioWithinTheWindow_StaysTwoEvents()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;

        await NewManager(context)
            .Capture(stock.Id, "DUPE", [At(Day), At(Day.AddDays(1), denominator: 10m)]);

        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Capture_SameRatioOnASiblingListing_StaysSeparate()
    {
        var (context, stock) = await SeedIssuer(secondaryTickers: ["DUPE-B"]);
        await using var _ = context;
        var manager = NewManager(context);

        await manager.Capture(stock.Id, "DUPE", [At(Day)]);
        await manager.Capture(stock.Id, "DUPE-B", [At(Day.AddDays(1))]);

        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Capture_CollapsesStoredDuplicatesOfTheEventItObserves()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var listingId = stock.Presentation.EquityListingId;
        foreach (var date in new[] { Day, Day.AddDays(1) })
            context.Add(
                new StockSplit
                {
                    EquityIssuerId = stock.Id,
                    EquityListingId = listingId,
                    PriceSeriesTicker = "DUPE",
                    EffectiveDate = date,
                    Numerator = 1m,
                    Denominator = 50m,
                    Source = StockSplitSource.Yahoo,
                    PriceAdjustmentAppliedTime = DateTime.UtcNow,
                }
            );
        await context.SaveChangesAsync();

        (await NewManager(context).Capture(stock.Id, "DUPE", [At(Day.AddDays(1))])).Should().Be(1);

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.EffectiveDate.Should().Be(Day.AddDays(1));
        stored.PriceAdjustmentAppliedTime.Should().NotBeNull();
    }

    [Fact]
    public async Task Capture_DoesNotMoveASurvivorOntoADateAnotherRatioHolds()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var manager = NewManager(context);
        await manager.Capture(stock.Id, "DUPE", [At(Day), At(Day.AddDays(3), denominator: 10m)]);

        await manager.Capture(
            stock.Id,
            "DUPE",
            [At(Day.AddDays(3), source: StockSplitSource.External)]
        );

        context.ChangeTracker.Clear();
        var stored = await context
            .Set<StockSplit>()
            .OrderBy(row => row.EffectiveDate)
            .ToListAsync();
        stored.Select(row => row.EffectiveDate).Should().Equal(Day, Day.AddDays(3));
        stored[1].Denominator.Should().Be(50m, "the exact-date row is replaced by precedence");
        stored[1].Source.Should().Be(StockSplitSource.External);
    }

    [Fact]
    public async Task Capture_AttributesALegacyRowBeforeCollapsingItsTwin()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        context.Add(
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                PriceSeriesTicker = "DUPE",
                EffectiveDate = Day,
                Numerator = 1m,
                Denominator = 50m,
                Source = StockSplitSource.Yahoo,
            }
        );
        context.Add(
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EquityListingId = stock.Presentation.EquityListingId,
                PriceSeriesTicker = "DUPE",
                EffectiveDate = Day.AddDays(1),
                Numerator = 1m,
                Denominator = 50m,
                Source = StockSplitSource.Yahoo,
            }
        );
        await context.SaveChangesAsync();
        var manager = NewManager(context);

        (await manager.Capture(stock.Id, "DUPE", [At(Day)])).Should().Be(1);
        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync(row => row.EquityListingId == null))
            .Should()
            .Be(0, "the legacy row is attributed first");

        (await manager.Capture(stock.Id, "DUPE", [At(Day)])).Should().Be(1);
        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().SingleAsync()).EffectiveDate.Should().Be(Day.AddDays(1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public async Task Capture_NonPositiveRatioArm_DoesNotPersist(
        decimal numerator,
        decimal denominator
    )
    {
        await using var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "SAFE"
        );
        context.Add(stock);
        await context.SaveChangesAsync();
        var split = Split(numerator);
        split.Denominator = denominator;

        var changes = await NewManager(context)
            .Capture(stock.Id, stock.Presentation.Listing.Ticker, [split]);

        changes.Should().Be(0);
        (await context.Set<StockSplit>().ToListAsync()).Should().BeEmpty();
    }
}
