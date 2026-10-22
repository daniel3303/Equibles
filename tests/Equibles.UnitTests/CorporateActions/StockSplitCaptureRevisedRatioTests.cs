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
using Microsoft.EntityFrameworkCore.Storage;

namespace Equibles.UnitTests.CorporateActions;

// A source that revises a split's ratio as well as its date must leave one row per event. The
// stored shapes are MGN's 2026 reverse split as production held it on 2026-10-22: the reference
// feed reported 1:40 for 21 August, then 1:40 for 8 September, then 1:30 for 17 September, and
// now lists only the last. Every row had been stamped applied.
public class StockSplitCaptureRevisedRatioTests
{
    private static readonly InMemoryDatabaseRoot Root = new();
    private static readonly DateOnly ReportedFrom = new(2025, 9, 26);
    private readonly string _databaseName = Guid.NewGuid().ToString();

    [Fact]
    public async Task SupersedeMovedDates_RecordedRevisedReverseSplit_KeepsOnlyTheReportedDefinitionAndReopensIt()
    {
        var (context, stock) = await SeedIssuer("MGN");
        await using var _ = context;
        var (first, second, reported) = MgnRows(stock);
        context.AddRange(first, second, reported);
        await context.SaveChangesAsync();
        var manager = NewManager(context);

        (await Supersede(manager, stock, "MGN", Reported(new DateOnly(2026, 9, 17), 1m, 30m)))
            .Should()
            .Be(2);
        (await Supersede(manager, stock, "MGN", Reported(new DateOnly(2026, 9, 17), 1m, 30m)))
            .Should()
            .Be(0, "the corrected rows are a fixed point");

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.Id.Should().Be(reported.Id);
        stored.EffectiveDate.Should().Be(new DateOnly(2026, 9, 17));
        stored.Denominator.Should().Be(30m);
        stored
            .PriceAdjustmentAppliedTime.Should()
            .BeNull("the stored closes were reconciled while the withdrawn 1:40 stood beside it");
    }

    [Fact]
    public async Task CaptureForListing_RevisedRatioBeforeTheNewDefinitionWasStored_MovesTheRowOntoIt()
    {
        var (context, stock) = await SeedIssuer("MGN");
        await using var _ = context;
        var (first, _, _) = MgnRows(stock);
        context.Add(first);
        await context.SaveChangesAsync();

        (
            await NewManager(context)
                .CaptureForListing(
                    stock.Id,
                    stock.Presentation.EquityListingId,
                    "MGN",
                    [Reported(new DateOnly(2026, 9, 17), 1m, 30m)],
                    reportedFrom: ReportedFrom
                )
        )
            .Should()
            .Be(1);

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.Id.Should().Be(first.Id);
        stored.EffectiveDate.Should().Be(new DateOnly(2026, 9, 17));
        (stored.Numerator, stored.Denominator).Should().Be((1m, 30m));
        stored.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    // WHLR's real sequence: three reverse splits a month apart, each still in the feed's report.
    [Fact]
    public async Task SupersedeMovedDates_LaterSplitWithADifferentRatioStillReported_KeepsBoth()
    {
        var (context, stock) = await SeedIssuer("WHLR");
        await using var _ = context;
        var applied = Utc(2026, 8, 28, 0, 22, 14);
        context.AddRange(
            Stored(
                stock,
                "WHLR",
                new DateOnly(2026, 8, 27),
                1m,
                4m,
                Utc(2026, 8, 24, 10, 19, 9),
                applied
            ),
            Stored(
                stock,
                "WHLR",
                new DateOnly(2026, 9, 22),
                1m,
                9m,
                Utc(2026, 9, 19, 0, 26, 9),
                null
            )
        );
        await context.SaveChangesAsync();

        (
            await Supersede(
                NewManager(context),
                stock,
                "WHLR",
                Reported(new DateOnly(2026, 7, 28), 1m, 5m),
                Reported(new DateOnly(2026, 8, 27), 1m, 4m),
                Reported(new DateOnly(2026, 9, 22), 1m, 9m)
            )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().OrderBy(row => row.EffectiveDate).ToListAsync())
            .Select(row => (row.Denominator, row.PriceAdjustmentAppliedTime))
            .Should()
            .Equal((4m, applied), (9m, null));
    }

    // JAGX's real rows: the feed dropped a 2:1 it reported a month after its 1:15. That 1:15 was
    // already history when the 2:1 was captured, so nothing in the report can be its revision.
    [Fact]
    public async Task SupersedeMovedDates_WithdrawnRowWithNoReportedEventStillAheadOfIt_IsLeftAlone()
    {
        var (context, stock) = await SeedIssuer("JAGX");
        await using var _ = context;
        var applied = Utc(2026, 10, 17, 20, 56, 55);
        context.AddRange(
            Stored(
                stock,
                "JAGX",
                new DateOnly(2026, 9, 17),
                1m,
                15m,
                Utc(2026, 9, 15, 21, 19, 8),
                null
            ),
            Stored(
                stock,
                "JAGX",
                new DateOnly(2026, 10, 16),
                2m,
                1m,
                Utc(2026, 10, 15, 8, 13, 35),
                applied
            )
        );
        await context.SaveChangesAsync();

        (
            await Supersede(
                NewManager(context),
                stock,
                "JAGX",
                Reported(new DateOnly(2026, 9, 17), 1m, 15m)
            )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task SupersedeMovedDates_ReportedEventMovesPricesTheOtherWay_IsNotARevision()
    {
        var (context, stock) = await SeedIssuer("MGN");
        await using var _ = context;
        var forward = Stored(
            stock,
            "MGN",
            new DateOnly(2026, 8, 21),
            2m,
            1m,
            Utc(2026, 8, 12, 16, 54, 29),
            Utc(2026, 8, 22, 0, 52, 47)
        );
        context.Add(forward);
        await context.SaveChangesAsync();

        (
            await Supersede(
                NewManager(context),
                stock,
                "MGN",
                Reported(new DateOnly(2026, 9, 17), 1m, 30m)
            )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().SingleAsync()).Id.Should().Be(forward.Id);
    }

    [Fact]
    public async Task SupersedeMovedDates_ReportedDateHeldByAThirdRatio_LeavesBothRows()
    {
        var (context, stock) = await SeedIssuer("MGN");
        await using var _ = context;
        var (first, _, _) = MgnRows(stock);
        var conflicting = Stored(
            stock,
            "MGN",
            new DateOnly(2026, 9, 17),
            1m,
            20m,
            Utc(2026, 9, 10, 16, 24, 39),
            Utc(2026, 9, 18, 0, 51, 9),
            StockSplitSource.Manual
        );
        context.AddRange(first, conflicting);
        await context.SaveChangesAsync();

        (
            await Supersede(
                NewManager(context),
                stock,
                "MGN",
                Reported(new DateOnly(2026, 9, 17), 1m, 30m)
            )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    // Yahoo's chart still lists the withdrawn 1:40 on 8 September beside the feed's 1:30.
    [Fact]
    public async Task CaptureForListing_LowerSourceStillServingTheRevisedRatio_IsNotCaptured()
    {
        var (context, stock) = await SeedIssuer("MGN");
        await using var _ = context;
        var (_, _, reported) = MgnRows(stock);
        context.Add(reported);
        await context.SaveChangesAsync();

        (
            await NewManager(context)
                .CaptureForListing(
                    stock.Id,
                    stock.Presentation.EquityListingId,
                    "MGN",
                    [
                        Reported(new DateOnly(2026, 9, 8), 1m, 40m, StockSplitSource.Yahoo),
                        Reported(new DateOnly(2026, 9, 17), 1m, 30m, StockSplitSource.Yahoo),
                    ]
                )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.Id.Should().Be(reported.Id);
        stored.PriceAdjustmentAppliedTime.Should().Be(reported.PriceAdjustmentAppliedTime);
    }

    [Fact]
    public async Task CaptureForListing_LowerSourceSplitTheOtherWayInsideTheWindow_IsCaptured()
    {
        var (context, stock) = await SeedIssuer("MGN");
        await using var _ = context;
        var (_, _, reported) = MgnRows(stock);
        context.Add(reported);
        await context.SaveChangesAsync();

        (
            await NewManager(context)
                .CaptureForListing(
                    stock.Id,
                    stock.Presentation.EquityListingId,
                    "MGN",
                    [Reported(new DateOnly(2026, 9, 14), 3m, 1m, StockSplitSource.Yahoo)]
                )
        )
            .Should()
            .Be(1);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    private static (StockSplit First, StockSplit Second, StockSplit Reported) MgnRows(
        EquityIssuer stock
    ) =>
        (
            Stored(
                stock,
                "MGN",
                new DateOnly(2026, 8, 21),
                1m,
                40m,
                Utc(2026, 8, 12, 16, 54, 29),
                Utc(2026, 8, 22, 0, 52, 47)
            ),
            Stored(
                stock,
                "MGN",
                new DateOnly(2026, 9, 8),
                1m,
                40m,
                Utc(2026, 8, 27, 20, 22, 52),
                Utc(2026, 9, 9, 13, 53, 38)
            ),
            Stored(
                stock,
                "MGN",
                new DateOnly(2026, 9, 17),
                1m,
                30m,
                Utc(2026, 9, 10, 16, 24, 39),
                Utc(2026, 9, 18, 0, 51, 9)
            )
        );

    private static Task<int> Supersede(
        StockSplitCaptureManager manager,
        EquityIssuer stock,
        string ticker,
        params CapturedSplit[] report
    ) =>
        manager.SupersedeMovedDates(
            stock.Id,
            stock.Presentation.EquityListingId,
            ticker,
            ReportedFrom,
            report
        );

    private static CapturedSplit Reported(
        DateOnly date,
        decimal numerator,
        decimal denominator,
        StockSplitSource source = StockSplitSource.External
    ) =>
        new()
        {
            EffectiveDate = date,
            Numerator = numerator,
            Denominator = denominator,
            Source = source,
        };

    private static StockSplit Stored(
        EquityIssuer stock,
        string ticker,
        DateOnly date,
        decimal numerator,
        decimal denominator,
        DateTime capturedAt,
        DateTime? applied,
        StockSplitSource source = StockSplitSource.External
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            EquityListingId = stock.Presentation.EquityListingId,
            PriceSeriesTicker = ticker,
            EffectiveDate = date,
            Numerator = numerator,
            Denominator = denominator,
            Source = source,
            CreationTime = capturedAt,
            PriceAdjustmentAppliedTime = applied,
        };

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private async Task<(EquiblesFinancialDbContext Context, EquityIssuer Stock)> SeedIssuer(
        string ticker
    )
    {
        var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker
        );
        context.Add(stock);
        await context.SaveChangesAsync();
        return (context, stock);
    }

    private EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(_databaseName, Root)
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
}
