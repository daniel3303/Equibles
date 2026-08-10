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
/// Pins snapshot-safe stamping for selected splits and cash dividends.
/// </summary>
public class CorporateActionPriceReconciliationManagerStampingTests
{
    private static readonly DateOnly SettledBefore = new(2026, 8, 10);

    private static CorporateActionPriceReconciliationManager NewManager(
        EquiblesFinancialDbContext db
    ) =>
        new(
            new StockSplitRepository(db),
            new CashDividendRepository(db),
            new CommonStockRepository(db),
            new CorporateActionPriceReconciliationCursorRepository(db)
        );

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warning => warning.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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

    private static CommonStock Stock(
        Guid id,
        string ticker = "AAPL",
        List<string> secondaryTickers = null
    ) =>
        new()
        {
            Id = id,
            Ticker = ticker,
            SecondaryTickers = secondaryTickers ?? [],
        };

    private static StockSplit PendingSplit(
        Guid stockId,
        DateOnly effectiveDate,
        string listedTicker = "AAPL"
    ) =>
        new()
        {
            CommonStockId = stockId,
            PriceSeriesTicker = listedTicker,
            EffectiveDate = effectiveDate,
            Numerator = 2m,
            Denominator = 1m,
            Source = StockSplitSource.Yahoo,
        };

    private static CashDividend PendingDividend(
        Guid stockId,
        DateOnly exDate,
        decimal amount = 0.25m
    ) =>
        new()
        {
            CommonStockId = stockId,
            ExDate = exDate,
            AmountPerShare = amount,
            Source = CashDividendSource.Yahoo,
        };

    private static CapturedDividend PriceSeriesDividend(DateOnly exDate, decimal amount) =>
        new()
        {
            ExDate = exDate,
            AmountPerShare = amount,
            Source = CashDividendSource.Yahoo,
        };

    [Fact]
    public async Task StampApplied_StampsSelectedSplitAndDividendSnapshots()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        db.AddRange(Stock(stockId, secondaryTickers: ["AAPL-WS"]), Stock(otherId, "MSFT"));
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2024, 6, 10)),
            PendingSplit(stockId, new DateOnly(2025, 1, 2), "AAPL-WS"),
            PendingDividend(stockId, new DateOnly(2024, 5, 9), 0.25m),
            PendingDividend(otherId, new DateOnly(2024, 5, 9), 0.75m)
        );
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single(
            series => series.CommonStockId == stockId && series.ListedTicker == "AAPL"
        );
        var appliedTime = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);

        var stamped = await manager.StampApplied(selected, appliedTime);

        stamped.Should().Be(2);
        var split = await db.Set<StockSplit>()
            .SingleAsync(row => row.CommonStockId == stockId && row.PriceSeriesTicker == "AAPL");
        split.PriceAdjustmentAppliedTime.Should().Be(appliedTime);
        var dividend = await db.Set<CashDividend>()
            .SingleAsync(row => row.CommonStockId == stockId);
        dividend.PriceAdjustmentAppliedTime.Should().Be(appliedTime);
        dividend.PriceAdjustmentAppliedAmountPerShare.Should().Be(0.25m);
        (await manager.SelectPendingSeries(50, SettledBefore)).TotalPending.Should().Be(2);
    }

    [Fact]
    public async Task StampApplied_IsIdempotent_SecondPassStampsNothing()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        db.Add(PendingDividend(stockId, new DateOnly(2024, 5, 9)));
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single();

        var first = await manager.StampApplied(selected, DateTime.UtcNow);
        var second = await manager.StampApplied(selected, DateTime.UtcNow);

        first.Should().Be(1);
        second.Should().Be(0);
        (await manager.SelectPendingSeries(50, SettledBefore)).Series.Should().BeEmpty();
    }

    [Fact]
    public async Task StampApplied_ActionsCapturedAfterSelection_RemainPending()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        db.AddRange(
            PendingSplit(stockId, new DateOnly(2024, 6, 10)),
            PendingDividend(stockId, new DateOnly(2024, 5, 9))
        );
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single();
        var newSplit = PendingSplit(stockId, new DateOnly(2025, 1, 2));
        var newDividend = PendingDividend(stockId, new DateOnly(2025, 2, 7), 0.26m);
        db.AddRange(newSplit, newDividend);
        await db.SaveChangesAsync();

        var stamped = await manager.StampApplied(selected, DateTime.UtcNow);

        stamped.Should().Be(2);
        newSplit.PriceAdjustmentAppliedTime.Should().BeNull();
        newDividend.PriceAdjustmentAppliedTime.Should().BeNull();
        var next = (await manager.SelectPendingSeries(50, SettledBefore))
            .Series.Should()
            .ContainSingle()
            .Which;
        next.Splits.Should().ContainSingle(snapshot => snapshot.Id == newSplit.Id);
        next.Dividends.Should().ContainSingle(snapshot => snapshot.Id == newDividend.Id);
    }

    [Fact]
    public async Task StampApplied_SelectedActionsRevisedAfterSelection_RemainPending()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.Add(Stock(stockId));
        var split = PendingSplit(stockId, new DateOnly(2024, 6, 10));
        var dividend = PendingDividend(stockId, new DateOnly(2024, 5, 9), 0.25m);
        db.AddRange(split, dividend);
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single();
        split.Numerator = 3m;
        dividend.AmountPerShare = 0.26m;
        await db.SaveChangesAsync();

        var stamped = await manager.StampApplied(selected, DateTime.UtcNow);

        stamped.Should().Be(0);
        (await manager.SelectPendingSeries(50, SettledBefore))
            .Series.Should()
            .ContainSingle()
            .Which.Dividends.Should()
            .ContainSingle(snapshot => snapshot.AmountPerShare == 0.26m);
    }

    [Fact]
    public async Task StampApplied_SelectedDividendRevisedToPriceSeriesAmount_StampsCurrentAmount()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var exDate = new DateOnly(2024, 5, 9);
        db.Add(Stock(stockId));
        var dividend = PendingDividend(stockId, exDate, 0.25m);
        dividend.Source = CashDividendSource.External;
        db.Add(dividend);
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single();
        dividend.AmountPerShare = 0.26m;
        await db.SaveChangesAsync();
        var appliedTime = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

        var stamped = await manager.StampApplied(
            selected,
            [PriceSeriesDividend(exDate, 0.26m)],
            SettledBefore,
            appliedTime
        );

        stamped.Should().Be(1);
        dividend.PriceAdjustmentAppliedAmountPerShare.Should().Be(0.26m);
        dividend.PriceAdjustmentAppliedTime.Should().Be(appliedTime);
        (await manager.SelectPendingSeries(50, SettledBefore)).Series.Should().BeEmpty();
    }

    [Fact]
    public async Task StampApplied_CurrentDividendDoesNotMatchPriceSeries_RemainsPending()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var exDate = new DateOnly(2024, 5, 9);
        db.Add(Stock(stockId));
        var dividend = PendingDividend(stockId, exDate, 0.25m);
        db.Add(dividend);
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single();
        dividend.AmountPerShare = 0.27m;
        await db.SaveChangesAsync();

        var stamped = await manager.StampApplied(
            selected,
            [PriceSeriesDividend(exDate, 0.26m)],
            SettledBefore,
            DateTime.UtcNow
        );

        stamped.Should().Be(0);
        dividend.PriceAdjustmentAppliedTime.Should().BeNull();
        (await manager.SelectPendingSeries(50, SettledBefore))
            .Series.Should()
            .ContainSingle()
            .Which.Dividends.Should()
            .ContainSingle(snapshot => snapshot.AmountPerShare == 0.27m);
    }

    [Fact]
    public async Task StampApplied_NewPriceSeriesDividendAfterSelection_StampsSameFetch()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var selectedExDate = new DateOnly(2024, 5, 9);
        var discoveredExDate = new DateOnly(2025, 2, 7);
        db.Add(Stock(stockId));
        db.Add(PendingDividend(stockId, selectedExDate));
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single();
        var discovered = PendingDividend(stockId, discoveredExDate, 0.26m);
        db.Add(discovered);
        await db.SaveChangesAsync();

        var stamped = await manager.StampApplied(
            selected,
            [
                PriceSeriesDividend(selectedExDate, 0.25m),
                PriceSeriesDividend(discoveredExDate, 0.26m),
            ],
            SettledBefore,
            DateTime.UtcNow
        );

        stamped.Should().Be(2);
        discovered.PriceAdjustmentAppliedAmountPerShare.Should().Be(0.26m);
        discovered.PriceAdjustmentAppliedTime.Should().NotBeNull();
        (await manager.SelectPendingSeries(50, SettledBefore)).Series.Should().BeEmpty();
    }

    [Fact]
    public async Task StampApplied_UnsettledPriceSeriesDividend_RemainsPending()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var selectedExDate = new DateOnly(2024, 5, 9);
        db.Add(Stock(stockId));
        db.Add(PendingDividend(stockId, selectedExDate));
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single();
        var unsettled = PendingDividend(stockId, SettledBefore, 0.26m);
        db.Add(unsettled);
        await db.SaveChangesAsync();

        var stamped = await manager.StampApplied(
            selected,
            [PriceSeriesDividend(selectedExDate, 0.25m), PriceSeriesDividend(SettledBefore, 0.26m)],
            SettledBefore,
            DateTime.UtcNow
        );

        stamped.Should().Be(1);
        unsettled.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Fact]
    public async Task StampApplied_PrimaryChangedDuringFetch_LeavesDividendForNewPrimary()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        var stock = Stock(stockId);
        db.Add(stock);
        db.Add(PendingDividend(stockId, new DateOnly(2024, 5, 9)));
        await db.SaveChangesAsync();

        var manager = NewManager(db);
        var selected = (await manager.SelectPendingSeries(50, SettledBefore)).Series.Single();
        stock.Ticker = "MSFT";
        stock.SecondaryTickers = ["AAPL"];
        await db.SaveChangesAsync();

        var stamped = await manager.StampApplied(selected, DateTime.UtcNow);

        stamped.Should().Be(0);
        (await manager.SelectPendingSeries(50, SettledBefore))
            .Series.Should()
            .ContainSingle()
            .Which.ListedTicker.Should()
            .Be("MSFT");
    }
}
