using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Finra;

public class ShortInterestRepositoryGetAllSettlementDatesTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ShortInterestRepository _repository;

    public ShortInterestRepositoryGetAllSettlementDatesTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _repository = new ShortInterestRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: every distinct settlement date, each reported once regardless of how
    // many stocks reported on it, ordered newest-first so callers (the date selector,
    // the latest-date default) need not re-sort.
    [Fact]
    public async Task GetAllSettlementDates_ReturnsDistinctDatesNewestFirst()
    {
        var june = new DateOnly(2024, 6, 15);
        var may = new DateOnly(2024, 5, 31);
        var april = new DateOnly(2024, 4, 30);

        _dbContext
            .Set<ShortInterest>()
            .AddRange(
                ShortInterest(Guid.NewGuid(), may),
                ShortInterest(Guid.NewGuid(), june),
                ShortInterest(Guid.NewGuid(), june), // same date, different stock — collapsed
                ShortInterest(Guid.NewGuid(), april)
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var dates = await _repository.GetAllSettlementDates().ToListAsync(CancellationToken.None);

        dates.Should().Equal(june, may, april);
    }

    private static ShortInterest ShortInterest(Guid stockId, DateOnly settlementDate) =>
        new()
        {
            CommonStockId = stockId,
            SettlementDate = settlementDate,
            CurrentShortPosition = 1000,
        };
}
