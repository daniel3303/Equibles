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

// A source that re-dates a split it already reported must leave one row per event. The shapes
// are APH's 2026 two-for-one: first reported on its 17 August record date, then on its
// 3 September ex-date, 17 days apart and so outside SameEventWindowDays.
public class StockSplitCaptureMovedDateTests
{
    private static readonly InMemoryDatabaseRoot Root = new();
    private static readonly DateOnly RecordDate = new(2026, 8, 17);
    private static readonly DateOnly ExDate = new(2026, 9, 3);
    private readonly string _databaseName = Guid.NewGuid().ToString();

    [Fact]
    public async Task CaptureForListing_ReportMovedTheDateOntoAStoredRow_DropsTheWithdrawnRowAndKeepsTheSurvivor()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var withdrawn = Stored(stock, RecordDate, Utc(2026, 8, 6), applied: Utc(2026, 8, 18));
        var survivor = Stored(stock, ExDate, Utc(2026, 8, 7), applied: Utc(2026, 9, 4));
        context.AddRange(withdrawn, survivor);
        await context.SaveChangesAsync();
        var manager = NewManager(context);

        (await Report(manager, stock, new DateOnly(2026, 7, 24), Reported(ExDate))).Should().Be(1);
        (await Report(manager, stock, new DateOnly(2026, 7, 24), Reported(ExDate)))
            .Should()
            .Be(0, "the corrected rows are a fixed point");

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.Id.Should().Be(survivor.Id);
        stored.EffectiveDate.Should().Be(ExDate);
        stored
            .PriceAdjustmentAppliedTime.Should()
            .Be(Utc(2026, 9, 4), "the stored closes already carry this split once");
    }

    [Fact]
    public async Task CaptureForListing_ReportMovedTheDateBeforeItWasStored_MovesTheRowAndReopensReconciliation()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var withdrawn = Stored(stock, RecordDate, Utc(2026, 8, 6));
        context.Add(withdrawn);
        await context.SaveChangesAsync();
        var manager = NewManager(context);

        (await Report(manager, stock, new DateOnly(2026, 7, 24), Reported(ExDate))).Should().Be(1);

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.Id.Should().Be(withdrawn.Id);
        stored.EffectiveDate.Should().Be(ExDate);
        stored.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Fact]
    public async Task CaptureForListing_EventMovedTwice_LeavesOneRowAtTheReportedDate()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        context.AddRange(
            Stored(
                stock,
                new DateOnly(2026, 10, 15),
                Utc(2026, 9, 29),
                applied: Utc(2026, 10, 17),
                numerator: 1m,
                denominator: 15m
            ),
            Stored(
                stock,
                new DateOnly(2026, 10, 29),
                Utc(2026, 10, 5),
                numerator: 1m,
                denominator: 15m
            )
        );
        await context.SaveChangesAsync();

        await Report(
            NewManager(context),
            stock,
            new DateOnly(2026, 10, 7),
            Reported(new DateOnly(2026, 11, 13), numerator: 1m, denominator: 15m)
        );

        context.ChangeTracker.Clear();
        var stored = await context.Set<StockSplit>().SingleAsync();
        stored.EffectiveDate.Should().Be(new DateOnly(2026, 11, 13));
        stored.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Fact]
    public async Task SupersedeMovedDates_FullHistoryReport_RetiresOnlyTheWithdrawnRow()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var yahooApplied = Utc(2026, 8, 5);
        var earlier = Stored(
            stock,
            new DateOnly(2021, 3, 5),
            Utc(2026, 7, 7),
            yahooApplied,
            StockSplitSource.Yahoo
        );
        var previous = Stored(
            stock,
            new DateOnly(2024, 6, 12),
            Utc(2026, 7, 7),
            yahooApplied,
            StockSplitSource.Yahoo
        );
        var withdrawn = Stored(stock, RecordDate, Utc(2026, 8, 6), applied: Utc(2026, 8, 18));
        var survivor = Stored(stock, ExDate, Utc(2026, 8, 7), applied: Utc(2026, 9, 4));
        context.AddRange(earlier, previous, withdrawn, survivor);
        await context.SaveChangesAsync();
        CapturedSplit[] report =
        [
            Reported(new DateOnly(2004, 3, 30)),
            Reported(new DateOnly(2007, 4, 2)),
            Reported(new DateOnly(2014, 10, 10)),
            Reported(new DateOnly(2021, 3, 5)),
            Reported(new DateOnly(2024, 6, 12)),
            Reported(ExDate),
        ];

        (
            await NewManager(context)
                .SupersedeMovedDates(
                    stock.Id,
                    stock.Presentation.EquityListingId,
                    "APH",
                    new DateOnly(2020, 1, 2),
                    report
                )
        )
            .Should()
            .Be(1);

        context.ChangeTracker.Clear();
        var stored = await context
            .Set<StockSplit>()
            .OrderBy(row => row.EffectiveDate)
            .ToListAsync();
        stored.Select(row => row.Id).Should().Equal(earlier.Id, previous.Id, survivor.Id);
        stored.Take(2).Should().OnlyContain(row => row.Source == StockSplitSource.Yahoo);
        stored
            .Select(row => row.PriceAdjustmentAppliedTime)
            .Should()
            .Equal(yahooApplied, yahooApplied, Utc(2026, 9, 4));
    }

    [Fact]
    public async Task SupersedeMovedDates_SameRatioReportedOnBothDates_KeepsBothEvents()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var previous = Stored(
            stock,
            new DateOnly(2024, 6, 12),
            Utc(2024, 6, 1),
            applied: Utc(2024, 6, 13)
        );
        var later = Stored(stock, ExDate, Utc(2026, 8, 7), applied: Utc(2026, 9, 4));
        context.AddRange(previous, later);
        await context.SaveChangesAsync();

        (
            await NewManager(context)
                .SupersedeMovedDates(
                    stock.Id,
                    stock.Presentation.EquityListingId,
                    "APH",
                    new DateOnly(2020, 1, 2),
                    [Reported(new DateOnly(2024, 6, 12)), Reported(ExDate)]
                )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task SupersedeMovedDates_NearestReportPredatesTheCapture_NeverFallsBackToALaterSplit()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        // Captured two days late, then re-dated a day earlier: the nearest report was already
        // history at capture, so the row is not this event's withdrawn date, and a genuine later
        // split of the same ratio must not absorb it.
        var reported = Stored(stock, ExDate, Utc(2026, 9, 5), applied: Utc(2026, 9, 6));
        var later = Stored(
            stock,
            new DateOnly(2028, 6, 12),
            Utc(2028, 6, 1),
            applied: Utc(2028, 6, 13)
        );
        context.AddRange(reported, later);
        await context.SaveChangesAsync();

        (
            await NewManager(context)
                .SupersedeMovedDates(
                    stock.Id,
                    stock.Presentation.EquityListingId,
                    "APH",
                    new DateOnly(2020, 1, 2),
                    [Reported(ExDate.AddDays(-1)), Reported(new DateOnly(2028, 6, 12))]
                )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task SupersedeMovedDates_RowCapturedLongAfterItsDate_IsNotJudged()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        // A history row upgraded in place keeps a capture time years after its date; the report
        // dropping it is not a re-date onto the next split of that ratio.
        context.AddRange(
            Stored(stock, new DateOnly(2021, 3, 5), Utc(2026, 7, 7), applied: Utc(2026, 8, 5)),
            Stored(stock, ExDate, Utc(2026, 8, 7), applied: Utc(2026, 9, 4))
        );
        await context.SaveChangesAsync();

        (
            await NewManager(context)
                .SupersedeMovedDates(
                    stock.Id,
                    stock.Presentation.EquityListingId,
                    "APH",
                    new DateOnly(2020, 1, 2),
                    [Reported(ExDate)]
                )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CaptureForListing_RowBeforeTheReportFloor_IsNotJudged()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        context.Add(Stored(stock, RecordDate, Utc(2026, 8, 6)));
        await context.SaveChangesAsync();

        await Report(NewManager(context), stock, RecordDate.AddDays(1), Reported(ExDate));

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().AnyAsync(row => row.EffectiveDate == RecordDate))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task CaptureForListing_AnotherSourcesRow_IsNotWithdrawn()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        context.Add(Stored(stock, RecordDate, Utc(2026, 8, 6), source: StockSplitSource.Yahoo));
        await context.SaveChangesAsync();

        await Report(NewManager(context), stock, new DateOnly(2026, 7, 24), Reported(ExDate));

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().AnyAsync(row => row.EffectiveDate == RecordDate))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task CaptureForListing_RevisedRatio_IsNotTreatedAsAMovedDate()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        context.Add(Stored(stock, RecordDate, Utc(2026, 8, 6), numerator: 1m, denominator: 40m));
        await context.SaveChangesAsync();

        await Report(
            NewManager(context),
            stock,
            new DateOnly(2026, 7, 24),
            Reported(ExDate, numerator: 1m, denominator: 30m)
        );

        context.ChangeTracker.Clear();
        (
            await context
                .Set<StockSplit>()
                .AnyAsync(row => row.EffectiveDate == RecordDate && row.Denominator == 40m)
        )
            .Should()
            .BeTrue();
    }

    private static Task<int> Report(
        StockSplitCaptureManager manager,
        EquityIssuer stock,
        DateOnly reportedFrom,
        params CapturedSplit[] report
    ) =>
        manager.CaptureForListing(
            stock.Id,
            stock.Presentation.EquityListingId,
            "APH",
            report,
            reportedFrom: reportedFrom
        );

    private static CapturedSplit Reported(
        DateOnly date,
        decimal numerator = 2m,
        decimal denominator = 1m
    ) =>
        new()
        {
            EffectiveDate = date,
            Numerator = numerator,
            Denominator = denominator,
            Source = StockSplitSource.External,
        };

    private static StockSplit Stored(
        EquityIssuer stock,
        DateOnly date,
        DateTime capturedAt,
        DateTime? applied = null,
        StockSplitSource source = StockSplitSource.External,
        decimal numerator = 2m,
        decimal denominator = 1m
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            EquityListingId = stock.Presentation.EquityListingId,
            PriceSeriesTicker = "APH",
            EffectiveDate = date,
            Numerator = numerator,
            Denominator = denominator,
            Source = source,
            CreationTime = capturedAt,
            PriceAdjustmentAppliedTime = applied,
        };

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private async Task<(EquiblesFinancialDbContext Context, EquityIssuer Stock)> SeedIssuer()
    {
        var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "APH"
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
