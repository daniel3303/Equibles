using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Finra;

public class ShortInterestRepositoryGetStockIdsBySettlementDateTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ShortInterestRepository _repository;

    public ShortInterestRepositoryGetStockIdsBySettlementDateTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _repository = new ShortInterestRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: the stock ids that have short interest on EXACTLY the given
    // settlement date. A stock reporting only on another settlement date must
    // not leak in — the date filter is the whole point of the method.
    [Fact]
    public async Task GetStockIdsBySettlementDate_ReturnsOnlyStocksReportingOnThatDate()
    {
        var onDateA = Guid.NewGuid();
        var onDateB = Guid.NewGuid();
        var otherDateOnly = Guid.NewGuid();
        var target = new DateOnly(2024, 6, 15);
        var other = new DateOnly(2024, 5, 31);

        _dbContext
            .Set<ShortInterest>()
            .AddRange(
                ShortInterest(onDateA, target),
                ShortInterest(onDateB, target),
                ShortInterest(otherDateOnly, other) // different settlement date — excluded
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var ids = await _repository
            .GetStockIdsBySettlementDate(target)
            .ToListAsync(CancellationToken.None);

        ids.Should().BeEquivalentTo([onDateA, onDateB]);
        ids.Should().NotContain(otherDateOnly);
    }

    private static ShortInterest ShortInterest(Guid stockId, DateOnly settlementDate) =>
        new()
        {
            CommonStockId = stockId,
            SettlementDate = settlementDate,
            CurrentShortPosition = 1000,
        };
}
