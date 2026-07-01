using System.Reflection;
using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins <see cref="YahooPriceImportService.ReplacePriceRows"/>, the transactional core of the split
/// back-adjustment pass: it swaps a stock's stored rows in [floor, today] for the fresh
/// fully-adjusted series, leaves rows outside that window alone, and — crucially — never deletes the
/// stored series when the fetch came back empty (a delisted/unresolved ticker), so the split stays
/// pending for a later run (#2879).
/// </summary>
public class YahooPriceImportServiceReplacePriceRowsTests
{
    private static readonly DateOnly Floor = new(2020, 1, 1);
    private static readonly DateOnly Today = new(2026, 6, 30);

    private static readonly MethodInfo ReplacePriceRowsMethod =
        typeof(YahooPriceImportService).GetMethod(
            "ReplacePriceRows",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static Task ReplacePriceRows(
        DailyStockPriceRepository repo,
        Guid commonStockId,
        DateOnly floor,
        DateOnly today,
        List<DailyStockPrice> freshRows
    ) =>
        (Task)
            ReplacePriceRowsMethod.Invoke(
                null,
                [repo, commonStockId, floor, today, freshRows, CancellationToken.None]
            );

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            // The in-memory provider has no real transactions; ReplacePriceRows opens one, so tell
            // EF the ignored-transaction warning is expected rather than throwing.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new YahooModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static DailyStockPrice Row(Guid stockId, DateOnly date, decimal close) =>
        new()
        {
            CommonStockId = stockId,
            Date = date,
            Open = close,
            High = close,
            Low = close,
            Close = close,
            AdjustedClose = close,
            Volume = 1000,
        };

    [Fact]
    public async Task ReplacePriceRows_SwapsWindowRowsForTheFreshSeries()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        // Pre-split stored rows carry the old basis (close 400).
        db.AddRange(
            Row(stockId, new DateOnly(2024, 1, 2), 400m),
            Row(stockId, new DateOnly(2024, 1, 3), 404m)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Fresh fully-adjusted series (10:1 split -> close 40) plus a new day.
        var fresh = new List<DailyStockPrice>
        {
            Row(stockId, new DateOnly(2024, 1, 2), 40m),
            Row(stockId, new DateOnly(2024, 1, 3), 40.4m),
            Row(stockId, new DateOnly(2024, 1, 4), 41m),
        };

        await ReplacePriceRows(new DailyStockPriceRepository(db), stockId, Floor, Today, fresh);

        db.ChangeTracker.Clear();
        var stored = await db.Set<DailyStockPrice>()
            .Where(p => p.CommonStockId == stockId)
            .OrderBy(p => p.Date)
            .ToListAsync();
        stored.Should().HaveCount(3);
        stored.Select(p => p.Close).Should().Equal(40m, 40.4m, 41m); // old 400/404 basis gone
    }

    [Fact]
    public async Task ReplacePriceRows_LeavesRowsOutsideTheWindowUntouched()
    {
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        // A row before the floor must survive the replacement of the [floor, today] window.
        db.Add(Row(stockId, new DateOnly(2019, 6, 1), 10m));
        db.Add(Row(stockId, new DateOnly(2024, 1, 2), 400m));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var fresh = new List<DailyStockPrice> { Row(stockId, new DateOnly(2024, 1, 2), 40m) };

        await ReplacePriceRows(new DailyStockPriceRepository(db), stockId, Floor, Today, fresh);

        db.ChangeTracker.Clear();
        var dates = await db.Set<DailyStockPrice>()
            .Where(p => p.CommonStockId == stockId)
            .Select(p => p.Date)
            .OrderBy(d => d)
            .ToListAsync();
        dates.Should().Equal(new DateOnly(2019, 6, 1), new DateOnly(2024, 1, 2));
    }

    [Fact]
    public async Task ReplacePriceRows_EmptyFreshSeries_DeletesNothing()
    {
        // Delisted/unresolved ticker: Yahoo returned no prices. The guard must keep the existing
        // rows rather than wiping the series to empty, so the split can stay pending for a later run.
        await using var db = NewDb();
        var stockId = Guid.NewGuid();
        db.AddRange(
            Row(stockId, new DateOnly(2024, 1, 2), 400m),
            Row(stockId, new DateOnly(2024, 1, 3), 404m)
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await ReplacePriceRows(new DailyStockPriceRepository(db), stockId, Floor, Today, []);

        db.ChangeTracker.Clear();
        var count = await db.Set<DailyStockPrice>().CountAsync(p => p.CommonStockId == stockId);
        count.Should().Be(2); // existing rows preserved
    }
}
