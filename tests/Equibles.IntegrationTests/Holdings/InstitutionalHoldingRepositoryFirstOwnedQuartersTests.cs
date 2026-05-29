using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryFirstOwnedQuartersTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryFirstOwnedQuartersTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: per requested holder, the EARLIEST report date they held the
    // stock (regardless of row insert order). A requested holder who never held
    // the stock is absent (callers read absence as "no first-owned quarter"); a
    // holder who holds it but wasn't requested is excluded by the id filter.
    [Fact]
    public async Task GetFirstOwnedQuarters_ReturnsEarliestQuarterPerRequestedHolderOnly()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var requestedOwner = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Requested Owner",
        };
        var requestedNeverHeld = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Requested But Never Held",
        };
        var unrequestedOwner = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000003",
            Name = "Holds But Not Requested",
        };
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);
        var q3 = new DateOnly(2024, 9, 30);

        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext
            .Set<InstitutionalHolder>()
            .AddRange(requestedOwner, requestedNeverHeld, unrequestedOwner);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                // Inserted newest-first to prove Min picks the earliest, not the first row.
                Holding(stock.Id, requestedOwner.Id, q3, "RO-Q3"),
                Holding(stock.Id, requestedOwner.Id, q1, "RO-Q1"),
                Holding(stock.Id, requestedOwner.Id, q2, "RO-Q2"),
                Holding(stock.Id, unrequestedOwner.Id, q1, "UO-Q1")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _repository
            .GetFirstOwnedQuarters(stock, [requestedOwner.Id, requestedNeverHeld.Id])
            .ToListAsync(CancellationToken.None);

        result.Should().ContainSingle();
        result[0].Key.Should().Be(requestedOwner.Id);
        result[0].Value.Should().Be(q1);
    }

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate,
            Shares = 100,
            Value = 1000,
            AccessionNumber = accession,
        };
}
