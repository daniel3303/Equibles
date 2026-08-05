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

/// <summary>
/// Pins the stamping half of <see cref="SplitPriceReconciliationManager"/>: once one exact listed
/// series has been re-synced, only the unchanged splits selected before the fetch are stamped.
/// New or revised splits remain pending, and repeating one selection is idempotent.
/// </summary>
public class SplitPriceReconciliationManagerStampingTests
{
    private static SplitPriceReconciliationManager NewManager(EquiblesFinancialDbContext db) =>
        new(new StockSplitRepository(db), new CommonStockRepository(db));

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

    private static CommonStock Stock(Guid id, string ticker = "AAPL") =>
        new() { Id = id, Ticker = ticker };

    [Fact]
    public async Task StampApplied_StampsAllPendingSplitsForTheStock()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.AddRange(Stock(stockId), Stock(other, "MSFT"));
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2021, 1, 4)),
            PendingSplit(stockId, new DateOnly(2024, 6, 10)),
            PendingSplit(stockId, new DateOnly(2025, 1, 2), "AAPL-WS"),
            PendingSplit(other, new DateOnly(2022, 1, 1)) // a different stock stays untouched
        );
        await db.SaveChangesAsync();

        var appliedTime = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);
        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50)).Series.Single(series =>
            series.CommonStockId == stockId && series.ListedTicker == "AAPL"
        );

        var stamped = await manager.StampApplied(selected, appliedTime);

        stamped.Should().Be(2);
        var repo = new StockSplitRepository(db);
        (await repo.GetByStock(stockId).ToListAsync())
            .Should()
            .Contain(s => s.PriceSeriesTicker == "AAPL-WS" && s.PriceAdjustmentAppliedTime == null);
        (await repo.GetByStock(stockId).Where(s => s.PriceSeriesTicker == "AAPL").ToListAsync())
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
        db.Add(Stock(stockId));
        db.Add(PendingSplit(stockId, new DateOnly(2024, 6, 10)));
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50)).Series.Single();

        var first = await manager.StampApplied(selected, DateTime.UtcNow);
        var second = await manager.StampApplied(selected, DateTime.UtcNow);

        first.Should().Be(1);
        second.Should().Be(0); // already reconciled — nothing left to stamp
        (await manager.SelectPendingSeries(50)).Series.Should().BeEmpty();
    }

    [Fact]
    public async Task StampApplied_NewSplitCapturedAfterSelection_RemainsPending()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        db.Add(PendingSplit(stockId, new DateOnly(2024, 6, 10)));
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50)).Series.Single();
        var capturedDuringFetch = PendingSplit(stockId, new DateOnly(2025, 1, 2));
        db.Add(capturedDuringFetch);
        await db.SaveChangesAsync();

        var appliedTime = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);
        var stamped = await manager.StampApplied(selected, appliedTime);

        stamped.Should().Be(1);
        capturedDuringFetch.PriceAdjustmentAppliedTime.Should().BeNull();
        (await manager.SelectPendingSeries(50))
            .Series.Should()
            .ContainSingle()
            .Which.Splits.Should()
            .ContainSingle(split => split.Id == capturedDuringFetch.Id);
    }

    [Fact]
    public async Task StampApplied_SelectedSplitRevisedAfterSelection_RemainsPending()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        var revisedDuringFetch = PendingSplit(stockId, new DateOnly(2024, 6, 10));
        db.Add(revisedDuringFetch);
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50)).Series.Single();
        revisedDuringFetch.Numerator = 3m;
        await db.SaveChangesAsync();

        var stamped = await manager.StampApplied(selected, DateTime.UtcNow);

        stamped.Should().Be(0);
        revisedDuringFetch.PriceAdjustmentAppliedTime.Should().BeNull();
        (await manager.SelectPendingSeries(50))
            .Series.Should()
            .ContainSingle()
            .Which.Splits.Should()
            .ContainSingle(split => split.Id == revisedDuringFetch.Id && split.Numerator == 3m);
    }
}
