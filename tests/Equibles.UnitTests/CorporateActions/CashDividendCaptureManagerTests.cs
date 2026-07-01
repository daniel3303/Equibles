using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// Pins the upsert contract of <see cref="CashDividendCaptureManager"/>, mirroring the split
/// capture manager: idempotent by (stock, ExDate) — a re-run with the same events writes nothing —
/// a restated amount for an existing ex-date is updated in place, and a non-positive amount is
/// dropped as unusable.
/// </summary>
public class CashDividendCaptureManagerTests
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

    private static CapturedDividend Dividend(DateOnly exDate, decimal amount) =>
        new()
        {
            ExDate = exDate,
            AmountPerShare = amount,
            Source = CashDividendSource.Yahoo,
        };

    [Fact]
    public async Task Capture_NewDividends_InsertsOneRowPerExDate()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid() };
        var manager = new CashDividendCaptureManager(new CashDividendRepository(db));

        var changes = await manager.Capture(
            stock,
            [Dividend(new DateOnly(2024, 2, 9), 0.24m), Dividend(new DateOnly(2024, 5, 9), 0.25m)]
        );

        changes.Should().Be(2);
        var stored = await new CashDividendRepository(db).GetByStock(stock.Id).ToListAsync();
        stored.Should().HaveCount(2);
        stored.Single(d => d.ExDate == new DateOnly(2024, 2, 9)).AmountPerShare.Should().Be(0.24m);
        stored.Should().OnlyContain(d => d.Source == CashDividendSource.Yahoo);
    }

    [Fact]
    public async Task Capture_SameEventsRerun_IsIdempotentAndWritesNothing()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid() };
        var events = new[] { Dividend(new DateOnly(2024, 2, 9), 0.24m) };

        await new CashDividendCaptureManager(new CashDividendRepository(db)).Capture(stock, events);
        var secondPass = await new CashDividendCaptureManager(
            new CashDividendRepository(db)
        ).Capture(stock, events);

        secondPass.Should().Be(0);
        (await new CashDividendRepository(db).GetByStock(stock.Id).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Capture_RestatedAmountForExistingExDate_UpdatesInPlace()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid() };
        var exDate = new DateOnly(2024, 2, 9);

        await new CashDividendCaptureManager(new CashDividendRepository(db)).Capture(
            stock,
            [Dividend(exDate, 0.24m)]
        );
        var changes = await new CashDividendCaptureManager(new CashDividendRepository(db)).Capture(
            stock,
            [Dividend(exDate, 0.26m)]
        );

        changes.Should().Be(1);
        var stored = await new CashDividendRepository(db).GetByStock(stock.Id).ToListAsync();
        stored.Should().HaveCount(1);
        stored[0].AmountPerShare.Should().Be(0.26m);
    }

    [Fact]
    public async Task Capture_NonPositiveAmount_IsDropped()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid() };
        var manager = new CashDividendCaptureManager(new CashDividendRepository(db));

        var changes = await manager.Capture(
            stock,
            [Dividend(new DateOnly(2024, 2, 9), 0m), Dividend(new DateOnly(2024, 5, 9), -0.1m)]
        );

        changes.Should().Be(0);
        (await new CashDividendRepository(db).GetByStock(stock.Id).AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_NullOrEmpty_ReturnsZero()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Id = Guid.NewGuid() };
        var manager = new CashDividendCaptureManager(new CashDividendRepository(db));

        (await manager.Capture(stock, null)).Should().Be(0);
        (await manager.Capture(stock, [])).Should().Be(0);
    }
}
