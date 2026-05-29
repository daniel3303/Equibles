using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryGetCombinedQuarterFallbackTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryGetCombinedQuarterFallbackTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // "Current combined" = best-available per holder: current-quarter data for
    // holders who filed it, previous-quarter fallback ONLY for holders who
    // didn't. A holder who filed the current quarter must contribute exactly
    // their current row — never also their stale previous row (double-count),
    // which is the failure mode if the non-filer NOT-EXISTS guard regresses.
    [Fact]
    public async Task GetCombinedQuarter_HolderFiledCurrent_ExcludesTheirPreviousRow_AndFallsBackForNonFiler()
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
            Name = "Filed Both Quarters",
        };
        var nonFiler = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Filed Only Previous",
        };
        var previous = new DateOnly(2024, 3, 31);
        var current = new DateOnly(2024, 6, 30);

        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(filer, nonFiler);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, filer.Id, previous, shares: 100, accession: "F-Q1"),
                Holding(stock.Id, filer.Id, current, shares: 150, accession: "F-Q2"),
                Holding(stock.Id, nonFiler.Id, previous, shares: 200, accession: "N-Q1")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var combined = await _repository
            .GetCombinedQuarter(current, previous)
            .ToListAsync(CancellationToken.None);

        // Filer: exactly their current-quarter row, never the stale previous one.
        combined
            .Should()
            .ContainSingle(h => h.InstitutionalHolderId == filer.Id)
            .Which.ReportDate.Should()
            .Be(current);
        // Non-filer: previous-quarter row carried forward as fallback.
        combined
            .Should()
            .ContainSingle(h => h.InstitutionalHolderId == nonFiler.Id)
            .Which.ReportDate.Should()
            .Be(previous);
        combined.Should().HaveCount(2);
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
