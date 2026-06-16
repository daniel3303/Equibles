using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
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
            new HoldingsModuleConfiguration()
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
