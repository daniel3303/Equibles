using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Equibles.UnitTests.CorporateActions;

/// <summary>
/// Pins the effective-splits seam every read-time restatement goes through. Split rows are
/// captured at ANNOUNCEMENT, ahead of their effective date, so the unfiltered set scales a
/// series by a split that has not happened yet for the whole announcement window (GH-7254).
/// <see cref="StockSplitRepository.GetEffectiveByStock"/> / <see cref="StockSplitRepository.GetEffective"/>
/// must return only splits effective on or before the as-of date; the boundary is INCLUSIVE
/// so a split effective today already restates (matching SplitAdjustment's strictly-after rule).
/// </summary>
public class StockSplitRepositoryEffectiveTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
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

    private static StockSplit Split(Guid stockId, DateOnly effectiveDate) =>
        new()
        {
            CommonStockId = stockId,
            EffectiveDate = effectiveDate,
            Numerator = 10,
            Denominator = 1,
            Source = StockSplitSource.Yahoo,
        };

    [Fact]
    public async Task GetEffectiveByStock_ExcludesAnnouncedFutureSplit_KeepsPastAndSameDay()
    {
        await using var db = NewDb();
        var stock = new CommonStock { Ticker = "NVDA" };
        var asOf = new DateOnly(2026, 8, 13);
        db.Add(stock);
        db.AddRange(
            Split(stock.Id, asOf.AddYears(-1)), // long effective
            Split(stock.Id, asOf), // effective today — inclusive boundary
            Split(stock.Id, asOf.AddDays(7)) // announced, not yet a basis change
        );
        await db.SaveChangesAsync();

        var effective = await new StockSplitRepository(db)
            .GetEffectiveByStock(stock.Id, asOf)
            .Select(s => s.EffectiveDate)
            .ToListAsync();

        effective.Should().BeEquivalentTo([asOf.AddYears(-1), asOf]);
    }

    [Fact]
    public async Task GetEffective_BatchVariant_AppliesTheSameCutoffAcrossStocks()
    {
        await using var db = NewDb();
        var first = new CommonStock { Ticker = "AAA" };
        var second = new CommonStock { Ticker = "BBB" };
        var asOf = new DateOnly(2026, 8, 13);
        db.AddRange(first, second);
        db.AddRange(
            Split(first.Id, asOf.AddMonths(-2)),
            Split(first.Id, asOf.AddDays(1)), // announced for tomorrow — excluded
            Split(second.Id, asOf.AddDays(30)) // whole stock still inside its announcement window
        );
        await db.SaveChangesAsync();

        var effective = await new StockSplitRepository(db)
            .GetEffective(asOf)
            .Select(s => new { s.CommonStockId, s.EffectiveDate })
            .ToListAsync();

        effective.Should().HaveCount(1);
        effective[0].CommonStockId.Should().Be(first.Id);
        effective[0].EffectiveDate.Should().Be(asOf.AddMonths(-2));
    }
}
