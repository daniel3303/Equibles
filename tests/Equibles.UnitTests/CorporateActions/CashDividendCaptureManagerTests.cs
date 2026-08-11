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
/// Pins the upsert contract of <see cref="CashDividendCaptureManager"/>, mirroring the split
/// capture manager: the exact current primary is locked and revalidated, idempotent events write
/// nothing, same-date components are summed, a higher-priority restatement updates in place,
/// lower-priority sources cannot overwrite it, and a non-positive amount is dropped as unusable.
/// </summary>
public class CashDividendCaptureManagerTests
{
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

    private static CashDividendCaptureManager NewManager(EquiblesFinancialDbContext context) =>
        new(new CashDividendRepository(context), new CommonStockRepository(context));

    private static async Task<CommonStock> AddStock(
        EquiblesFinancialDbContext context,
        string ticker = "AAPL"
    )
    {
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = ticker };
        context.Add(stock);
        await context.SaveChangesAsync();
        return stock;
    }

    private static CapturedDividend Dividend(
        DateOnly exDate,
        decimal amount,
        CashDividendSource source = CashDividendSource.Yahoo
    ) =>
        new()
        {
            ExDate = exDate,
            AmountPerShare = amount,
            Source = source,
        };

    [Fact]
    public async Task Capture_NewDividends_InsertsOneRowPerExDate()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var manager = NewManager(db);

        var changes = await manager.Capture(
            stock.Id,
            stock.Ticker,
            [Dividend(new DateOnly(2024, 2, 9), 0.24m), Dividend(new DateOnly(2024, 5, 9), 0.25m)]
        );

        changes.Should().Be(2);
        var stored = await new CashDividendRepository(db).GetByStock(stock.Id).ToListAsync();
        stored.Should().HaveCount(2);
        stored.Single(d => d.ExDate == new DateOnly(2024, 2, 9)).AmountPerShare.Should().Be(0.24m);
        stored.Should().OnlyContain(d => d.Source == CashDividendSource.Yahoo);
        stored.Should().OnlyContain(d => d.PriceAdjustmentAppliedTime == null);
        stored.Should().OnlyContain(d => d.PriceAdjustmentAppliedAmountPerShare == null);
    }

    [Fact]
    public async Task Capture_SameEventsRerun_IsIdempotentAndWritesNothing()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var events = new[] { Dividend(new DateOnly(2024, 2, 9), 0.24m) };

        await NewManager(db).Capture(stock.Id, stock.Ticker, events);
        var secondPass = await NewManager(db).Capture(stock.Id, stock.Ticker, events);

        secondPass.Should().Be(0);
        (await new CashDividendRepository(db).GetByStock(stock.Id).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Capture_MultipleCashComponentsOnSameExDate_StoresTheirTotalOnce()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var exDate = new DateOnly(2026, 8, 7);
        var events = new[] { Dividend(exDate, 0.045m), Dividend(exDate, 0.775m) };

        var changes = await NewManager(db).Capture(stock.Id, stock.Ticker, events);
        var secondPass = await NewManager(db).Capture(stock.Id, stock.Ticker, events);

        changes.Should().Be(1);
        secondPass.Should().Be(0);
        var stored = await new CashDividendRepository(db).GetByStock(stock.Id).SingleAsync();
        stored.ExDate.Should().Be(exDate);
        stored.AmountPerShare.Should().Be(0.82m);
        stored.Source.Should().Be(CashDividendSource.Yahoo);
    }

    [Fact]
    public async Task Capture_MixedSourcesOnSameExDate_ThrowsWithoutWriting()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var exDate = new DateOnly(2026, 8, 7);
        var events = new[]
        {
            Dividend(exDate, 0.045m),
            new CapturedDividend
            {
                ExDate = exDate,
                AmountPerShare = 0.775m,
                Source = CashDividendSource.External,
            },
        };

        var capture = () => NewManager(db).Capture(stock.Id, stock.Ticker, events);

        await capture.Should().ThrowAsync<InvalidOperationException>();
        (await new CashDividendRepository(db).GetByStock(stock.Id).CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Capture_RestatedAmountForExistingExDate_UpdatesInPlace()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var exDate = new DateOnly(2024, 2, 9);

        await NewManager(db).Capture(stock.Id, stock.Ticker, [Dividend(exDate, 0.24m)]);
        var original = await db.Set<CashDividend>().SingleAsync();
        original.PriceAdjustmentAppliedAmountPerShare = original.AmountPerShare;
        original.PriceAdjustmentAppliedTime = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var changes = await NewManager(db)
            .Capture(stock.Id, stock.Ticker, [Dividend(exDate, 0.26m)]);

        changes.Should().Be(1);
        var stored = await new CashDividendRepository(db).GetByStock(stock.Id).ToListAsync();
        stored.Should().HaveCount(1);
        stored[0].AmountPerShare.Should().Be(0.26m);
        stored[0].PriceAdjustmentAppliedAmountPerShare.Should().BeNull();
        stored[0].PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Fact]
    public async Task Capture_HigherPrioritySource_ReplacesAmountAndSource()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var exDate = new DateOnly(2024, 2, 9);

        await NewManager(db)
            .Capture(
                stock.Id,
                stock.Ticker,
                [Dividend(exDate, 0.24m, CashDividendSource.External)]
            );
        var original = await db.Set<CashDividend>().SingleAsync();
        original.PriceAdjustmentAppliedAmountPerShare = original.AmountPerShare;
        original.PriceAdjustmentAppliedTime = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var changes = await NewManager(db)
            .Capture(stock.Id, stock.Ticker, [Dividend(exDate, 0.26m)]);

        changes.Should().Be(1);
        var stored = await db.Set<CashDividend>().SingleAsync();
        stored.AmountPerShare.Should().Be(0.26m);
        stored.Source.Should().Be(CashDividendSource.Yahoo);
        stored.PriceAdjustmentAppliedAmountPerShare.Should().BeNull();
        stored.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    [Fact]
    public async Task Capture_LowerPrioritySource_DoesNotOverwriteAmountOrMarker()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var exDate = new DateOnly(2024, 2, 9);

        await NewManager(db).Capture(stock.Id, stock.Ticker, [Dividend(exDate, 0.26m)]);
        var original = await db.Set<CashDividend>().SingleAsync();
        var appliedAt = DateTime.UtcNow;
        original.PriceAdjustmentAppliedAmountPerShare = original.AmountPerShare;
        original.PriceAdjustmentAppliedTime = appliedAt;
        await db.SaveChangesAsync();

        var changes = await NewManager(db)
            .Capture(
                stock.Id,
                stock.Ticker,
                [Dividend(exDate, 0.24m, CashDividendSource.External)]
            );

        changes.Should().Be(0);
        var stored = await db.Set<CashDividend>().SingleAsync();
        stored.AmountPerShare.Should().Be(0.26m);
        stored.Source.Should().Be(CashDividendSource.Yahoo);
        stored.PriceAdjustmentAppliedAmountPerShare.Should().Be(0.26m);
        stored.PriceAdjustmentAppliedTime.Should().Be(appliedAt);
    }

    [Fact]
    public async Task Capture_HigherPrioritySourceWithSameAmount_PromotesWithoutInvalidatingMarker()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var exDate = new DateOnly(2024, 2, 9);

        await NewManager(db)
            .Capture(
                stock.Id,
                stock.Ticker,
                [Dividend(exDate, 0.26m, CashDividendSource.External)]
            );
        var original = await db.Set<CashDividend>().SingleAsync();
        var appliedAt = DateTime.UtcNow;
        original.PriceAdjustmentAppliedAmountPerShare = original.AmountPerShare;
        original.PriceAdjustmentAppliedTime = appliedAt;
        await db.SaveChangesAsync();

        var changes = await NewManager(db)
            .Capture(stock.Id, stock.Ticker, [Dividend(exDate, 0.26m)]);

        changes.Should().Be(1);
        var stored = await db.Set<CashDividend>().SingleAsync();
        stored.Source.Should().Be(CashDividendSource.Yahoo);
        stored.PriceAdjustmentAppliedAmountPerShare.Should().Be(0.26m);
        stored.PriceAdjustmentAppliedTime.Should().Be(appliedAt);
    }

    [Fact]
    public async Task Capture_AutomaticSources_DoNotOverwriteManualAmount()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var exDate = new DateOnly(2024, 2, 9);

        await NewManager(db)
            .Capture(stock.Id, stock.Ticker, [Dividend(exDate, 0.255m, CashDividendSource.Manual)]);

        var yahooChanges = await NewManager(db)
            .Capture(stock.Id, stock.Ticker, [Dividend(exDate, 0.26m)]);
        var externalChanges = await NewManager(db)
            .Capture(
                stock.Id,
                stock.Ticker,
                [Dividend(exDate, 0.24m, CashDividendSource.External)]
            );

        yahooChanges.Should().Be(0);
        externalChanges.Should().Be(0);
        var stored = await db.Set<CashDividend>().SingleAsync();
        stored.AmountPerShare.Should().Be(0.255m);
        stored.Source.Should().Be(CashDividendSource.Manual);
    }

    [Fact]
    public async Task Capture_NonPositiveAmount_IsDropped()
    {
        await using var db = NewDb();
        var stock = await AddStock(db);
        var manager = NewManager(db);

        var changes = await manager.Capture(
            stock.Id,
            stock.Ticker,
            [Dividend(new DateOnly(2024, 2, 9), 0m), Dividend(new DateOnly(2024, 5, 9), -0.1m)]
        );

        changes.Should().Be(0);
        (await new CashDividendRepository(db).GetByStock(stock.Id).AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_StalePrimaryTargetAfterReorder_DoesNotWriteIssuerAction()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "GOOG",
            SecondaryTickers = ["GOOGL"],
        };
        db.Add(stock);
        await db.SaveChangesAsync();

        // The fetch originally saw GOOGL as primary. By the write boundary it is secondary, so
        // the issuer-level dividend must be skipped rather than attached to GOOG's price series.
        var staleWrite = await NewManager(db)
            .Capture(stock.Id, "GOOGL", [Dividend(new DateOnly(2024, 2, 9), 0.24m)]);

        staleWrite.Should().Be(0);
        (await db.Set<CashDividend>().ToListAsync()).Should().BeEmpty();

        var currentWrite = await NewManager(db)
            .Capture(stock.Id, "GOOG", [Dividend(new DateOnly(2024, 2, 9), 0.24m)]);

        currentWrite.Should().Be(1);
        (await db.Set<CashDividend>().SingleAsync()).CommonStockId.Should().Be(stock.Id);
    }

    [Fact]
    public async Task Capture_NullOrEmpty_ReturnsZero()
    {
        await using var db = NewDb();
        var manager = NewManager(db);

        (await manager.Capture(Guid.NewGuid(), "AAPL", null)).Should().Be(0);
        (await manager.Capture(Guid.NewGuid(), "AAPL", [])).Should().Be(0);
    }
}
