using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryNewSoldOutCombinedTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryNewSoldOutCombinedTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Combined-quarter churn: a prior holder of the stock counts as "sold out"
    // ONLY if they filed the current quarter (proving they dropped it). A holder
    // who didn't file at all is carried forward in the combined view (assumed to
    // still hold) and must NOT be counted — the difference from plain churn.
    [Fact]
    public async Task GetQuarterlyNewSoldOutPositionsCombined_NonFilerNotCountedSoldOut_OnlyActualDropper()
    {
        var stockS = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var stockT = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
        };
        var dropper = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Filed Current, Dropped S",
        };
        var nonFiler = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Did Not File Current",
        };
        var previous = new DateOnly(2024, 3, 31);
        var current = new DateOnly(2024, 6, 30);

        _dbContext.Set<CommonStock>().AddRange(stockS, stockT);
        _dbContext.Set<InstitutionalHolder>().AddRange(dropper, nonFiler);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                // Dropper held S last quarter, filed THIS quarter (only MSFT) — genuinely exited S.
                Holding(stockS.Id, dropper.Id, previous, "D-S-Q1"),
                Holding(stockT.Id, dropper.Id, current, "D-T-Q2"),
                // Non-filer held S last quarter and filed nothing this quarter — carried forward.
                Holding(stockS.Id, nonFiler.Id, previous, "N-S-Q1")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var churn = await _repository
            .GetQuarterlyNewSoldOutPositionsCombined(current, previous)
            .ToListAsync(CancellationToken.None);

        var sChurn = churn.Single(c => c.CommonStockId == stockS.Id);
        sChurn.SoldOutFilerCount.Should().Be(1, "only the holder who filed and dropped S sold out");
        sChurn.NewFilerCount.Should().Be(0);
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
