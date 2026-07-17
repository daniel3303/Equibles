using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Regression pin for the market-wide combined-quarter lane's 13F filter (MCP audit 2026-07,
/// GH-4449 family). A Schedule 13G event row whose ReportDate lands exactly on the current
/// quarter end made the carry-forward's "has this fund filed yet?" NOT-EXISTS test treat the
/// fund as already filed, dropping its entire carried-forward 13F book from the combined
/// view: BlackRock's $5.4T read as ~$25B mid filing window, and every mega-cap showed a
/// phantom multi-hundred-billion-dollar QoQ value drop on GetMostHeldStocks /
/// GetMarketWide13FActivity. A holder with ONLY a 13G row on the current quarter end must
/// still be carried forward at its prior-quarter positions, and 13G rows must not count as
/// holdings, filers, or churn anywhere in the market-wide lane.
/// </summary>
public class InstitutionalHoldingRepositoryCombinedCarryForward13GTests
{
    private static readonly DateOnly Previous = new(2026, 3, 31);
    private static readonly DateOnly Current = new(2026, 6, 30);

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
                new HoldingsModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value,
        FilingType filingType = FilingType.Form13F
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(30),
            ReportDate = reportDate,
            FilingType = filingType,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}-{filingType}",
        };

    [Fact]
    public async Task GetQuarterlyActivityCombined_HolderWithOnly13GOnCurrentQuarterEnd_IsStillCarriedForward()
    {
        await using var db = NewDb();

        var stock = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        var carried = new InstitutionalHolder { Cik = "1", Name = "Not-Yet-Filed Capital" };
        var filed = new InstitutionalHolder { Cik = "2", Name = "Early Filer LP" };
        db.AddRange(stock, carried, filed);

        // Both held MSFT in the previous quarter.
        db.Add(MakeHolding(stock, carried, Previous, shares: 1_000, value: 400_000));
        db.Add(MakeHolding(stock, filed, Previous, shares: 500, value: 200_000));
        // Only the early filer has filed its 13F for the current quarter; the other fund has
        // just a Schedule 13G event row that happens to land on the quarter end.
        db.Add(MakeHolding(stock, filed, Current, shares: 500, value: 210_000));
        db.Add(
            MakeHolding(
                stock,
                carried,
                Current,
                shares: 999_999,
                value: 999_999_999,
                FilingType.Schedule13G
            )
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new InstitutionalHoldingRepository(db);
        var activity = await repository
            .GetQuarterlyActivityCombined(Current, Previous)
            .ToListAsync();

        var row = activity.Should().ContainSingle().Subject;
        // The 13G-only holder carries its prior 1,000 shares forward; the 13G row itself
        // (999,999 shares) never counts. Combined current = 1,000 carried + 500 filed.
        row.CurrentShares.Should().Be(1_500);
        row.PreviousShares.Should().Be(1_500);
        row.CurrentValue.Should().Be(400_000 + 210_000);
        row.CurrentFilerCount.Should().Be(2);
    }

    [Fact]
    public async Task GetUniqueFilerIds_13GOnlyFilerOnQuarterEnd_IsNotCountedInTheUniverse()
    {
        await using var db = NewDb();

        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var realFiler = new InstitutionalHolder { Cik = "1", Name = "Real 13F Filer" };
        var eventOnly = new InstitutionalHolder { Cik = "2", Name = "13G Event Filer" };
        db.AddRange(stock, realFiler, eventOnly);
        db.Add(MakeHolding(stock, realFiler, Current, shares: 100, value: 10_000));
        db.Add(
            MakeHolding(
                stock,
                eventOnly,
                Current,
                shares: 100,
                value: 10_000,
                FilingType.Schedule13G
            )
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new InstitutionalHoldingRepository(db);
        var universe = await repository.GetUniqueFilerIds(Current).ToListAsync();

        universe.Should().ContainSingle().Which.Should().Be(realFiler.Id);
    }

    [Fact]
    public async Task GetQuarterlyNewSoldOutPositionsCombined_13GOnCurrentQuarterEnd_DoesNotProveAnExit()
    {
        await using var db = NewDb();

        var stock = new CommonStock
        {
            Ticker = "NVDA",
            Name = "NVIDIA Corp.",
            Cik = "0001045810",
        };
        var holder = new InstitutionalHolder { Cik = "1", Name = "Carried Capital" };
        db.AddRange(stock, holder);
        // Held in the previous quarter; current quarter has only a 13G event row — the fund
        // has NOT filed its 13F yet, so it must not count as a sold-out exit.
        db.Add(MakeHolding(stock, holder, Previous, shares: 1_000, value: 400_000));
        db.Add(
            MakeHolding(
                stock,
                holder,
                Current,
                shares: 1_000,
                value: 400_000,
                FilingType.Schedule13G
            )
        );
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var repository = new InstitutionalHoldingRepository(db);
        var churn = await repository
            .GetQuarterlyNewSoldOutPositionsCombined(Current, Previous)
            .ToListAsync();

        var row = churn.Should().ContainSingle().Subject;
        row.SoldOutFilerCount.Should().Be(0);
        row.NewFilerCount.Should().Be(0);
    }
}
