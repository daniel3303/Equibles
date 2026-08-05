using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// Pins the selection half of <see cref="SplitPriceReconciliationManager"/>: the price
/// back-adjustment pass must pick distinct exact listed series with unreconciled splits, snapshot
/// the selected split state, cap how many series it takes per cycle, and report the remainder.
/// </summary>
public class SplitPriceReconciliationManagerSelectionTests
{
    private static SplitPriceReconciliationManager NewManager(EquiblesFinancialDbContext db) =>
        new(new StockSplitRepository(db), new CommonStockRepository(db));

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static StockSplit PendingSplit(
        Guid stockId,
        DateOnly effective,
        string listedTicker = "AAPL"
    ) =>
        new()
        {
            CommonStockId = stockId,
            PriceSeriesTicker = listedTicker,
            EffectiveDate = effective,
            Numerator = 2m,
            Denominator = 1m,
            Source = StockSplitSource.Yahoo,
            PriceAdjustmentAppliedTime = null,
        };

    [Fact]
    public async Task SelectPendingSeries_ReturnsEachPendingSeriesOnce()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        // Two pending splits on the SAME stock must collapse to a single selected stock id.
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2021, 1, 4)),
            PendingSplit(stockId, new DateOnly(2024, 6, 10))
        );
        await db.SaveChangesAsync();

        var manager = NewManager(db);

        var selection = await manager.SelectPendingSeries(50);

        var selected = selection.Series.Should().ContainSingle().Which;
        selected.CommonStockId.Should().Be(stockId);
        selected.ListedTicker.Should().Be("AAPL");
        selected
            .Splits.Select(split => split.EffectiveDate)
            .Should()
            .Equal(new DateOnly(2021, 1, 4), new DateOnly(2024, 6, 10));
        selection.TotalPending.Should().Be(1);
        selection.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task SelectPendingSeries_CapsSelectionAndReportsRemainder()
    {
        await using var db = NewDb();
        for (var i = 0; i < 5; i++)
            db.Add(PendingSplit(Guid.NewGuid(), new DateOnly(2024, 1, 1)));
        await db.SaveChangesAsync();

        var manager = NewManager(db);

        var selection = await manager.SelectPendingSeries(2);

        selection.Series.Should().HaveCount(2);
        selection.TotalPending.Should().Be(5);
        selection.Skipped.Should().Be(3); // 5 pending - 2 taken = 3 deferred, not dropped
    }

    [Fact]
    public async Task SelectPendingSeries_IgnoresAlreadyReconciledSplits()
    {
        await using var db = NewDb();
        var stampedOnly = Guid.NewGuid();
        var stamped = PendingSplit(stampedOnly, new DateOnly(2023, 1, 1));
        stamped.PriceAdjustmentAppliedTime = DateTime.UtcNow;
        db.Add(stamped);
        await db.SaveChangesAsync();

        var manager = NewManager(db);

        var selection = await manager.SelectPendingSeries(50);

        selection.Series.Should().BeEmpty();
        selection.TotalPending.Should().Be(0);
    }

    [Fact]
    public async Task SelectPendingSeries_KeepsDifferentListedSeriesIndependentAndIgnoresNull()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2024, 1, 1), "GOOGL"),
            PendingSplit(stockId, new DateOnly(2024, 2, 1), "GOOG"),
            PendingSplit(stockId, new DateOnly(2024, 3, 1), listedTicker: null)
        );
        await db.SaveChangesAsync();

        var manager = NewManager(db);

        var selection = await manager.SelectPendingSeries(50);

        selection
            .Series.Select(series => (series.CommonStockId, series.ListedTicker))
            .Should()
            .BeEquivalentTo([(stockId, "GOOGL"), (stockId, "GOOG")]);
        selection.TotalPending.Should().Be(2);
    }

    [Fact]
    public async Task SelectPendingSeries_PrimaryReorder_DoesNotRelabelTheStoredSeries()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var stock = new CommonStock
        {
            Id = stockId,
            Ticker = "GOOGL",
            SecondaryTickers = ["GOOG"],
        };
        db.Add(stock);
        db.Add(PendingSplit(stockId, new DateOnly(2024, 2, 1), "GOOGL"));
        await db.SaveChangesAsync();

        // The authoritative designation changes after capture. The pending reconciliation still
        // belongs to the exact series that produced the split, not today's primary ticker.
        stock.Ticker = "GOOG";
        stock.SecondaryTickers = ["GOOGL"];
        await db.SaveChangesAsync();

        var manager = NewManager(db);

        var selection = await manager.SelectPendingSeries(50);

        var selected = selection.Series.Should().ContainSingle().Which;
        selected.CommonStockId.Should().Be(stockId);
        selected.ListedTicker.Should().Be("GOOGL");
    }
}
