using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryReportDatesByStockTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryReportDatesByStockTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: the stock's report dates, DISTINCT and newest-first (callers
    // treat index 0 as the latest filing window). Duplicate dates collapse, the
    // ordering is descending regardless of insert order, and other stocks'
    // dates are excluded.
    [Fact]
    public async Task GetReportDatesByStock_ReturnsDistinctDatesNewestFirst_ScopedToStock()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var other = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
        };
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var q3 = new DateOnly(2024, 9, 30);

        _dbContext.Set<CommonStock>().AddRange(stock, other);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                // Inserted out of order with a duplicate q3 — must collapse + sort desc.
                Holding(stock.Id, q1, "S-Q1"),
                Holding(stock.Id, q3, "S-Q3a"),
                Holding(stock.Id, q2, "S-Q2"),
                Holding(stock.Id, q3, "S-Q3b"),
                // Other stock's date must not leak in.
                Holding(other.Id, new DateOnly(2024, 12, 31), "O-Q4")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository
            .GetReportDatesByStock(stock)
            .ToListAsync(CancellationToken.None);

        dates.Should().Equal(q3, q2, q1);
    }

    private static InstitutionalHolding Holding(
        Guid stockId,
        DateOnly reportDate,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = Guid.NewGuid(),
            ReportDate = reportDate,
            FilingDate = reportDate,
            Shares = 100,
            Value = 1000,
            AccessionNumber = accession,
        };
}
