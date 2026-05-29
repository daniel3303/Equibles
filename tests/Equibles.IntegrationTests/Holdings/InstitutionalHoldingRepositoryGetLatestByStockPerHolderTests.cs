using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryGetLatestByStockPerHolderTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryGetLatestByStockPerHolderTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // "Latest by stock" means each holder's MOST RECENT filing of the stock —
    // not the single globally-latest report date. A holder who stopped filing
    // still holds per their last disclosure, so their older row must survive.
    // A global-latest-date implementation would silently drop that holder.
    [Fact]
    public async Task GetLatestByStock_HolderWithOnlyOlderFiling_StillReturnsThatHoldersLatestRow()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var holderA = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Holder A",
        };
        var holderB = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Holder B",
        };
        var q1 = new DateOnly(2024, 3, 31);
        var q2 = new DateOnly(2024, 6, 30);

        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(holderA, holderB);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                // Holder A files both quarters — only the Q2 row should win for A.
                Holding(stock.Id, holderA.Id, q1, shares: 100, accession: "A-Q1"),
                Holding(stock.Id, holderA.Id, q2, shares: 150, accession: "A-Q2"),
                // Holder B files only Q1 — that older row is B's latest and must survive.
                Holding(stock.Id, holderB.Id, q1, shares: 200, accession: "B-Q1")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var latest = await _repository.GetLatestByStock(stock).ToListAsync(CancellationToken.None);

        latest.Should().HaveCount(2);
        latest
            .Should()
            .ContainSingle(h => h.InstitutionalHolderId == holderA.Id)
            .Which.Shares.Should()
            .Be(150);
        latest
            .Should()
            .ContainSingle(h => h.InstitutionalHolderId == holderB.Id)
            .Which.Shares.Should()
            .Be(200);
    }

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate,
            Shares = shares,
            Value = shares * 10,
            AccessionNumber = accession,
        };
}
