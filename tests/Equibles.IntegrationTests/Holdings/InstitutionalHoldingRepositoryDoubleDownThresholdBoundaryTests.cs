using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryDoubleDownThresholdBoundaryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryDoubleDownThresholdBoundaryTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: returns holders who increased shares by >= minPctIncrease.
    // The boundary is inclusive — a position up by EXACTLY the threshold must
    // qualify; a `>` regression would drop it. A holder just below the
    // threshold must be excluded, confirming the cutoff actually discriminates.
    [Fact]
    public async Task GetDoubleDownPositions_IncreaseExactlyAtThreshold_IsIncluded()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var atThreshold = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Up Exactly 50%",
        };
        var belowThreshold = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000002",
            Name = "Up 49.9%",
        };
        var previous = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);

        _dbContext.Set<CommonStock>().Add(stock);
        _dbContext.Set<InstitutionalHolder>().AddRange(atThreshold, belowThreshold);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                // 100 -> 150 = +50.0%, exactly the threshold.
                Holding(stock.Id, atThreshold.Id, previous, 100, "AT-Q3"),
                Holding(stock.Id, atThreshold.Id, current, 150, "AT-Q4"),
                // 1000 -> 1499 = +49.9%, just under — must be excluded.
                Holding(stock.Id, belowThreshold.Id, previous, 1000, "BL-Q3"),
                Holding(stock.Id, belowThreshold.Id, current, 1499, "BL-Q4")
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var positions = await _repository
            .GetDoubleDownPositions(current, previous, 50.0)
            .ToListAsync(CancellationToken.None);

        positions.Should().ContainSingle();
        positions[0].InstitutionalHolderId.Should().Be(atThreshold.Id);
        positions[0].CurrentShares.Should().Be(150);
        positions[0].PreviousShares.Should().Be(100);
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
            Value = shares * 100,
            AccessionNumber = accession,
        };
}
