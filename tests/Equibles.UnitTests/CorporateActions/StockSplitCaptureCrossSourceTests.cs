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

// Two sources dating one sizable split weeks apart hold one row. The shapes are RUSHA's 2026
// three-for-two: charted on 11 August by the lower source, executed on 1 September per the feed,
// 21 days apart and so outside SameEventWindowDays but inside the cross-source window.
public class StockSplitCaptureCrossSourceTests
{
    private static readonly InMemoryDatabaseRoot Root = new();
    private static readonly DateOnly ChartedDate = new(2026, 8, 11);
    private static readonly DateOnly ExecutedDate = new(2026, 9, 1);
    private readonly string _databaseName = Guid.NewGuid().ToString();

    [Fact]
    public async Task CaptureForListing_LowerSourceDatesTheSplitWeeksBeforeAStoredHigherRow_KeepsTheStoredRowAlone()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var applied = Utc(2026, 9, 2);
        var stored = Stored(stock, ExecutedDate, StockSplitSource.External, applied);
        context.Add(stored);
        await context.SaveChangesAsync();
        var manager = NewManager(context);

        (await Capture(manager, stock, Reported(ChartedDate, StockSplitSource.Yahoo)))
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        var row = await context.Set<StockSplit>().SingleAsync();
        row.Id.Should().Be(stored.Id);
        row.EffectiveDate.Should().Be(ExecutedDate);
        row.Source.Should().Be(StockSplitSource.External);
        row.PriceAdjustmentAppliedTime.Should()
            .Be(applied, "the stored closes already carry this split once");
    }

    [Fact]
    public async Task CaptureForListing_HigherSourceDatesTheSplitWeeksAfterAStoredLowerRow_MovesItAndReopensReconciliation()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var stored = Stored(stock, ChartedDate, StockSplitSource.Yahoo, Utc(2026, 8, 12));
        context.Add(stored);
        await context.SaveChangesAsync();
        var manager = NewManager(context);

        (await Capture(manager, stock, Reported(ExecutedDate, StockSplitSource.External)))
            .Should()
            .Be(1);
        (await Capture(manager, stock, Reported(ExecutedDate, StockSplitSource.External)))
            .Should()
            .Be(0, "the corrected row is a fixed point");

        context.ChangeTracker.Clear();
        var row = await context.Set<StockSplit>().SingleAsync();
        row.Id.Should().Be(stored.Id);
        row.EffectiveDate.Should().Be(ExecutedDate);
        row.Source.Should().Be(StockSplitSource.External);
        row.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Fact]
    public async Task CaptureForListing_NearUnityRatioFromTwoSourcesAMonthApart_StaysTwoEvents()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        context.Add(
            Stored(
                stock,
                new DateOnly(2026, 8, 3),
                StockSplitSource.Yahoo,
                Utc(2026, 8, 4),
                numerator: 101m,
                denominator: 100m
            )
        );
        await context.SaveChangesAsync();

        (
            await Capture(
                NewManager(context),
                stock,
                Reported(
                    new DateOnly(2026, 9, 2),
                    StockSplitSource.External,
                    numerator: 101m,
                    denominator: 100m
                )
            )
        )
            .Should()
            .Be(1);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task CaptureForListing_OneSourceRepeatsTheRatioWeeksApart_StaysTwoEvents()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        context.Add(Stored(stock, ChartedDate, StockSplitSource.External, Utc(2026, 8, 12)));
        await context.SaveChangesAsync();

        (
            await Capture(
                NewManager(context),
                stock,
                Reported(ExecutedDate, StockSplitSource.External)
            )
        )
            .Should()
            .Be(1);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    // One ratio has several spellings and a source may round it: a three-for-four consolidation is
    // 3:4, 0.75:1 or 1:1.333333. A lower source's later observation of a stored event under another
    // spelling is still that event, never a second split restating the series again.
    [Theory]
    [InlineData(1, 1.5, 2, 3)]
    [InlineData(1, 1.333333, 3, 4)]
    [InlineData(30.0003, 1, 30, 1)]
    public async Task CaptureForListing_LowerSourceSpellsTheStoredRatioAnotherWayADayLater_KeepsTheStoredRowAlone(
        double storedNumerator,
        double storedDenominator,
        double numerator,
        double denominator
    )
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var applied = Utc(2026, 9, 2);
        var stored = Stored(
            stock,
            ExecutedDate,
            StockSplitSource.External,
            applied,
            (decimal)storedNumerator,
            (decimal)storedDenominator
        );
        context.Add(stored);
        await context.SaveChangesAsync();

        (
            await Capture(
                NewManager(context),
                stock,
                Reported(
                    ExecutedDate.AddDays(1),
                    StockSplitSource.Yahoo,
                    (decimal)numerator,
                    (decimal)denominator
                )
            )
        )
            .Should()
            .Be(0);

        context.ChangeTracker.Clear();
        var row = await context.Set<StockSplit>().SingleAsync();
        row.Id.Should().Be(stored.Id);
        row.EffectiveDate.Should().Be(ExecutedDate);
        row.Source.Should().Be(StockSplitSource.External);
        row.Numerator.Should().Be((decimal)storedNumerator);
        row.PriceAdjustmentAppliedTime.Should()
            .Be(applied, "the stored closes already carry this split once");
    }

    [Fact]
    public async Task CaptureForListing_HigherSourceSpellsAStoredLowerRowAnotherWay_MovesItInsteadOfAddingASecond()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        var stored = Stored(
            stock,
            ExecutedDate.AddDays(1),
            StockSplitSource.Yahoo,
            Utc(2026, 9, 3),
            numerator: 2m,
            denominator: 3m
        );
        context.Add(stored);
        await context.SaveChangesAsync();

        (
            await Capture(
                NewManager(context),
                stock,
                Reported(ExecutedDate, StockSplitSource.External, numerator: 1m, denominator: 1.5m)
            )
        )
            .Should()
            .Be(1);

        context.ChangeTracker.Clear();
        var row = await context.Set<StockSplit>().SingleAsync();
        row.Id.Should().Be(stored.Id);
        row.EffectiveDate.Should().Be(ExecutedDate);
        row.Source.Should().Be(StockSplitSource.External);
        row.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    // The width is a rounding width, not a likeness: 751:1000 beside 75:100 is a per-thousand
    // approximation, which one source's two rows keep apart as they did before.
    [Fact]
    public async Task CaptureForListing_OneSourceStatesRatiosAPerThousandApartDaysApart_StaysTwoRows()
    {
        var (context, stock) = await SeedIssuer();
        await using var _ = context;
        context.Add(
            Stored(
                stock,
                ExecutedDate,
                StockSplitSource.Yahoo,
                Utc(2026, 9, 2),
                numerator: 751m,
                denominator: 1000m
            )
        );
        await context.SaveChangesAsync();

        (
            await Capture(
                NewManager(context),
                stock,
                Reported(
                    ExecutedDate.AddDays(2),
                    StockSplitSource.Yahoo,
                    numerator: 75m,
                    denominator: 100m
                )
            )
        )
            .Should()
            .Be(1);

        context.ChangeTracker.Clear();
        (await context.Set<StockSplit>().CountAsync()).Should().Be(2);
    }

    private static Task<int> Capture(
        StockSplitCaptureManager manager,
        EquityIssuer stock,
        params CapturedSplit[] report
    ) => manager.CaptureForListing(stock.Id, stock.Presentation.EquityListingId, "RUSHA", report);

    private static CapturedSplit Reported(
        DateOnly date,
        StockSplitSource source,
        decimal numerator = 3m,
        decimal denominator = 2m
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
        DateOnly date,
        StockSplitSource source,
        DateTime? applied,
        decimal numerator = 3m,
        decimal denominator = 2m
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            EquityListingId = stock.Presentation.EquityListingId,
            PriceSeriesTicker = "RUSHA",
            EffectiveDate = date,
            Numerator = numerator,
            Denominator = denominator,
            Source = source,
            CreationTime = Utc(2026, 8, 1),
            PriceAdjustmentAppliedTime = applied,
        };

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private async Task<(EquiblesFinancialDbContext Context, EquityIssuer Stock)> SeedIssuer()
    {
        var context = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "RUSHA"
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
