using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryReportDatesByHolderTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryReportDatesByHolderTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: the holder's report dates, DISTINCT and newest-first (callers
    // treat index 0 as the latest filing window). Duplicates collapse, ordering
    // is descending regardless of insert order, and other holders' dates are
    // excluded — the holder-scoped sibling of GetReportDatesByStock.
    [Fact]
    public async Task GetReportDatesByHolder_ReturnsDistinctDatesNewestFirst_ScopedToHolder()
    {
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Holder A",
        };
        var other = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Holder B",
        };
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var q3 = new DateOnly(2024, 9, 30);

        _dbContext.Set<InstitutionalHolder>().AddRange(holder, other);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                // Inserted out of order with a duplicate q3 — must collapse + sort desc.
                Holding(holder.Id, q1, "A-Q1"),
                Holding(holder.Id, q3, "A-Q3a"),
                Holding(holder.Id, q2, "A-Q2"),
                Holding(holder.Id, q3, "A-Q3b"),
                // Other holder's date must not leak in.
                Holding(other.Id, new DateOnly(2024, 12, 31), "B-Q4")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository
            .GetReportDatesByHolder(holder)
            .ToListAsync(CancellationToken.None);

        dates.Should().Equal(q3, q2, q1);
    }

    private static InstitutionalHolding Holding(
        Guid holderId,
        DateOnly reportDate,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = Guid.NewGuid(),
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate,
            Shares = 100,
            Value = 1000,
            AccessionNumber = accession,
        };
}
