using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// Pins the stamping half of <see cref="SplitPriceReconciliationManager"/>: once a stock's prices
/// have been re-synced, EVERY pending split for that stock is stamped applied, and the operation is
/// idempotent — a second pass selects and stamps nothing, so a reconciled split is never reprocessed.
/// </summary>
public class SplitPriceReconciliationManagerStampingTests
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
    public async Task StampApplied_StampsAllPendingSplitsForTheStock()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2021, 1, 4)),
            PendingSplit(stockId, new DateOnly(2024, 6, 10)),
            PendingSplit(other, new DateOnly(2022, 1, 1)) // a different stock stays untouched
        );
        await db.SaveChangesAsync();

        var appliedTime = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);
        var manager = new SplitPriceReconciliationManager(new StockSplitRepository(db));

        var stamped = await manager.StampApplied(stockId, appliedTime);

        stamped.Should().Be(2);
        var repo = new StockSplitRepository(db);
        (await repo.GetByStock(stockId).ToListAsync())
            .Should()
            .OnlyContain(s => s.PriceAdjustmentAppliedTime == appliedTime);
        (await repo.GetByStock(other).ToListAsync())
            .Should()
            .OnlyContain(s => s.PriceAdjustmentAppliedTime == null);
    }

    [Fact]
    public async Task StampApplied_IsIdempotent_SecondPassStampsNothingAndSelectsNothing()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(PendingSplit(stockId, new DateOnly(2024, 6, 10)));
        await db.SaveChangesAsync();

        var manager = new SplitPriceReconciliationManager(new StockSplitRepository(db));

        var first = await manager.StampApplied(stockId, DateTime.UtcNow);
        var second = await manager.StampApplied(stockId, DateTime.UtcNow);

        first.Should().Be(1);
        second.Should().Be(0); // already reconciled — nothing left to stamp
        (await manager.SelectPendingStocks(50)).StockIds.Should().BeEmpty();
    }
}
