using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepository13FAvailableReportDatesTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepository13FAvailableReportDatesTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new CorporateActionsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: the market-wide report-date list must be 13F-only and newest-first.
    // Schedule 13D/G rows carry a daily event date, not a quarter end; if a later 13D/G
    // date leaked in, callers that treat index 0 as "latest" and index 1 as "prior
    // quarter" would compare a quarter-end portfolio against the prior DAY — the regression
    // behind the market-wide activity boards (double-down) showing zero positions.
    [Fact]
    public async Task Get13FAvailableReportDates_ExcludesLater13DGEventDates_NewestFirst()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Holder A",
        };

        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var q3 = new DateOnly(2024, 9, 30);
        // A 13D/G stake filed AFTER the latest 13F quarter — exactly the row that
        // pollutes the all-filings list and makes "prior" the prior day.
        var event13G = new DateOnly(2024, 11, 14);

        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().Add(holder);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, holder.Id, q2, FilingType.Form13F, "13F-Q2"),
                Holding(stock.Id, holder.Id, q1, FilingType.Form13F, "13F-Q1"),
                Holding(stock.Id, holder.Id, q3, FilingType.Form13F, "13F-Q3"),
                Holding(stock.Id, holder.Id, event13G, FilingType.Schedule13G, "13G-EVENT"),
                Holding(stock.Id, holder.Id, event13G, FilingType.Schedule13D, "13D-EVENT")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository
            .Get13FAvailableReportDates()
            .ToListAsync(CancellationToken.None);

        // Only the 13F quarter ends, newest-first; the 2024-11-14 event date is gone, so
        // index 0 is the latest quarter and index 1 is the genuine prior quarter.
        dates.Should().Equal(q3, q2, q1);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_ReturnsSnapshotDatesNewestFirst()
    {
        var stock = Stock("MSFT");
        var holder = Holder("0000000005");
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .AddRange(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = q1,
                    CurrentFilerCount = 1,
                },
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = q2,
                    CurrentFilerCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, q2, FilingType.Form13F, "13F-Q2"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(q2, q1);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_PrependsNewestLiveQuarterDuringRefreshLag()
    {
        var stock = Stock("NVDA");
        var holder = Holder("0000000002");
        var snapshotQuarter = new DateOnly(2024, 6, 30);
        var liveQuarter = new DateOnly(2024, 9, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .Add(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = snapshotQuarter,
                    CurrentFilerCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, liveQuarter, FilingType.Form13F, "13F-LIVE"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(liveQuarter, snapshotQuarter);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_ExcludesSoldOutSnapshotQuarter()
    {
        var stock = Stock("EXIT");
        var holder = Holder("0000000004");
        var heldQuarter = new DateOnly(2024, 3, 31);
        var soldOutQuarter = new DateOnly(2024, 6, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .AddRange(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = heldQuarter,
                    CurrentFilerCount = 1,
                },
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = soldOutQuarter,
                    CurrentFilerCount = 0,
                    PreviousFilerCount = 1,
                    SoldOutFilerCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, heldQuarter, FilingType.Form13F, "13F-HELD"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(heldQuarter);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_DropsSnapshotNewerThanLiveHistory()
    {
        var stock = Stock("STALE");
        var holder = Holder("0000000006");
        var q1 = new DateOnly(2024, 3, 31);
        var liveQuarter = new DateOnly(2024, 6, 30);
        var staleSnapshotQuarter = new DateOnly(2024, 9, 30);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .AddRange(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = q1,
                    CurrentFilerCount = 1,
                },
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = staleSnapshotQuarter,
                    CurrentFilerCount = 1,
                }
            );
        _dbContext
            .Set<InstitutionalHolding>()
            .Add(Holding(stock.Id, holder.Id, liveQuarter, FilingType.Form13F, "13F-LIVE-Q2"));
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(liveQuarter, q1);
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_ReturnsEmptyWhenSnapshotHasNoLiveHoldings()
    {
        var stock = Stock("EMPTY");
        _dbContext.Add(stock);
        _dbContext
            .Set<StockQuarterlyActivity>()
            .Add(
                new StockQuarterlyActivity
                {
                    CommonStockId = stock.Id,
                    ReportDate = new DateOnly(2024, 9, 30),
                    CurrentFilerCount = 1,
                }
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().BeEmpty();
    }

    [Fact]
    public async Task Get13FReportDatesByStockSnapshotBacked_FallsBackToLive13FHistoryWithoutSnapshot()
    {
        var stock = Stock("MU");
        var holder = Holder("0000000003");
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var schedule13GDate = new DateOnly(2024, 8, 12);

        _dbContext.AddRange(stock, holder);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, holder.Id, q1, FilingType.Form13F, "13F-Q1"),
                Holding(stock.Id, holder.Id, q2, FilingType.Form13F, "13F-Q2"),
                Holding(stock.Id, holder.Id, schedule13GDate, FilingType.Schedule13G, "13G")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.Get13FReportDatesByStockSnapshotBacked(stock);

        dates.Should().Equal(q2, q1);
    }

    private static CommonStock Stock(string ticker) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = $"{ticker} Inc.",
            Cik = Guid.NewGuid().ToString("N")[..10],
        };

    private static InstitutionalHolder Holder(string cik) =>
        new()
        {
            Id = Guid.NewGuid(),
            Cik = cik,
            Name = $"Holder {cik}",
        };

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        FilingType filingType,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate,
            FilingType = filingType,
            Shares = 100,
            Value = 1000,
            AccessionNumber = accession,
        };
}
