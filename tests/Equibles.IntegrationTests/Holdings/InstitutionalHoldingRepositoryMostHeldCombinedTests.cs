using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryMostHeldCombinedTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryMostHeldCombinedTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // "Most held (combined)" is the combined-quarter activity filtered to
    // currently-held names only (CurrentFilerCount > 0). A stock that every
    // prior holder has exited — they filed the current quarter but dropped it,
    // so it isn't carried forward — has zero combined current filers and must
    // be excluded, while a stock still held in the current quarter stays.
    [Fact]
    public async Task GetMostHeldCombined_ExcludesFullyExitedStock_KeepsCurrentlyHeld()
    {
        var held = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
        };
        var exited = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
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
        var previous = new DateOnly(2024, 3, 31);
        var current = new DateOnly(2024, 6, 30);

        _dbContext.Set<CommonStock>().AddRange(held, exited);
        _dbContext.Set<InstitutionalHolder>().AddRange(holderA, holderB);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(held.Id, holderA.Id, current, "A-AAPL-Q2"),
                // Holder B held MSFT last quarter, filed THIS quarter (AAPL only) — so MSFT
                // is not carried forward and ends with zero combined current filers.
                Holding(exited.Id, holderB.Id, previous, "B-MSFT-Q1"),
                Holding(held.Id, holderB.Id, current, "B-AAPL-Q2")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var mostHeld = await _repository
            .GetMostHeldCombined(current, previous)
            .ToListAsync(CancellationToken.None);

        mostHeld.Should().ContainSingle();
        mostHeld[0].CommonStockId.Should().Be(held.Id);
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
