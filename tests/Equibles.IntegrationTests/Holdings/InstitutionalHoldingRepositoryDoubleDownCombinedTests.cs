using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

public class InstitutionalHoldingRepositoryDoubleDownCombinedTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly InstitutionalHoldingRepository _repository;

    public InstitutionalHoldingRepositoryDoubleDownCombinedTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration()
        );
        _repository = new InstitutionalHoldingRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Combined double-down carries a non-filer's prior shares into the current
    // side (assumed still held). That makes their current == previous, so their
    // delta is zero — they must NOT surface as a double-down. Only a holder who
    // actually filed an increase qualifies. Guards against carry-forward
    // inflating non-filers into false conviction signals.
    [Fact]
    public async Task GetDoubleDownPositionsCombined_NonFilerCarriedForward_NotReportedAsIncrease()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var doubler = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "0000000001",
            Name = "Filed An Increase",
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
        _dbContext.Set<InstitutionalHolder>().AddRange(doubler, nonFiler);
        _dbContext
            .Set<InstitutionalHolding>()
            .AddRange(
                Holding(stock.Id, doubler.Id, previous, 100),
                Holding(stock.Id, doubler.Id, current, 250),
                // Non-filer: only a previous row — carried forward, so current == previous.
                Holding(stock.Id, nonFiler.Id, previous, 200)
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var positions = await _repository
            .GetDoubleDownPositionsCombined(current, previous, 50.0)
            .ToListAsync(CancellationToken.None);

        positions.Should().ContainSingle();
        positions[0].InstitutionalHolderId.Should().Be(doubler.Id);
        positions[0].CurrentShares.Should().Be(250);
        positions[0].PreviousShares.Should().Be(100);
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
