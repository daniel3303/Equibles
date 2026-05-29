using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryQuarterlyActivityCombinedTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryQuarterlyActivityCombinedTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Combined current totals = current filers' current data PLUS each non-filer's
    // previous data carried forward (a holder who didn't file is assumed to still
    // hold). So a non-filer's prior shares and filer-count must be folded into the
    // *current* side — not just the actual current-quarter rows.
    [Fact]
    public async Task GetQuarterlyActivityCombined_NonFilerSharesCarriedIntoCurrentTotals()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var filer = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Filed Current",
        };
        var nonFiler = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Did Not File Current",
        };
        var previous = new DateOnly(2024, 3, 31);
        var current = new DateOnly(2024, 6, 30);

        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(filer, nonFiler);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, filer.Id, previous, 100),
                Holding(stock.Id, filer.Id, current, 150),
                // Non-filer: only a previous row — carried forward into the combined current.
                Holding(stock.Id, nonFiler.Id, previous, 200)
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var activity = (
            await _repository
                .GetQuarterlyActivityCombined(current, previous)
                .ToListAsync(CancellationToken.None)
        ).Single(a => a.CommonStockId == stock.Id);

        // 150 (filer current) + 200 (non-filer carried) = 350; naive "current rows only" = 150.
        activity.CurrentShares.Should().Be(350);
        // Both holders count toward the combined current breadth.
        activity.CurrentFilerCount.Should().Be(2);
        activity.PreviousShares.Should().Be(300);
    }

    private static InstitutionalHolding Holding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares
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
            AccessionNumber = $"{holderId:N}-{reportDate:yyyyMMdd}",
        };
}
