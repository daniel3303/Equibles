using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// Pins the selection half of <see cref="SplitPriceReconciliationManager"/>: the price
/// back-adjustment pass must pick the DISTINCT stocks that still have an unreconciled split
/// (PriceAdjustmentAppliedTime == null), cap how many it takes per cycle so the universe backfill
/// throttles against Yahoo's shared limiter, and report the remainder rather than dropping it.
/// </summary>
public class SplitPriceReconciliationManagerSelectionTests
{
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

    private static StockSplit PendingSplit(Guid stockId, DateOnly effective) =>
        new()
        {
            CommonStockId = stockId,
            EffectiveDate = effective,
            Numerator = 2m,
            Denominator = 1m,
            Source = StockSplitSource.Yahoo,
            PriceAdjustmentAppliedTime = null,
        };

    [Fact]
    public async Task SelectPendingStocks_ReturnsEachPendingStockOnce()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        // Two pending splits on the SAME stock must collapse to a single selected stock id.
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2021, 1, 4)),
            PendingSplit(stockId, new DateOnly(2024, 6, 10))
        );
        await db.SaveChangesAsync();

        var manager = new SplitPriceReconciliationManager(new StockSplitRepository(db));

        var selection = await manager.SelectPendingStocks(50);

        selection.StockIds.Should().ContainSingle().Which.Should().Be(stockId);
        selection.TotalPending.Should().Be(1);
        selection.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task SelectPendingStocks_CapsSelectionAndReportsRemainder()
    {
        await using var db = NewDb();
        for (var i = 0; i < 5; i++)
            db.Add(PendingSplit(Guid.NewGuid(), new DateOnly(2024, 1, 1)));
        await db.SaveChangesAsync();

        var manager = new SplitPriceReconciliationManager(new StockSplitRepository(db));

        var selection = await manager.SelectPendingStocks(2);

        selection.StockIds.Should().HaveCount(2);
        selection.TotalPending.Should().Be(5);
        selection.Skipped.Should().Be(3); // 5 pending - 2 taken = 3 deferred, not dropped
    }

    [Fact]
    public async Task SelectPendingStocks_IgnoresAlreadyReconciledSplits()
    {
        await using var db = NewDb();
        var stampedOnly = Guid.NewGuid();
        var stamped = PendingSplit(stampedOnly, new DateOnly(2023, 1, 1));
        stamped.PriceAdjustmentAppliedTime = DateTime.UtcNow;
        db.Add(stamped);
        await db.SaveChangesAsync();

        var manager = new SplitPriceReconciliationManager(new StockSplitRepository(db));

        var selection = await manager.SelectPendingStocks(50);

        selection.StockIds.Should().BeEmpty();
        selection.TotalPending.Should().Be(0);
    }
}
