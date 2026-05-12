using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

public class FailToDeliverRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly FailToDeliverRepository _repository;

    public FailToDeliverRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration());
        _repository = new FailToDeliverRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetByStock_MultipleStocksWithFails_ReturnsOnlyTargetStockRecords() {
        var target = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple Inc." };
        var other = new CommonStock { Id = Guid.NewGuid(), Ticker = "MSFT", Name = "Microsoft Corp." };
        _dbContext.Set<CommonStock>().AddRange(target, other);

        _repository.Add(new FailToDeliver {
            CommonStockId = target.Id, SettlementDate = new DateOnly(2025, 10, 1), Quantity = 100, Price = 150m,
        });
        _repository.Add(new FailToDeliver {
            CommonStockId = target.Id, SettlementDate = new DateOnly(2025, 10, 2), Quantity = 200, Price = 151m,
        });
        _repository.Add(new FailToDeliver {
            CommonStockId = other.Id, SettlementDate = new DateOnly(2025, 10, 1), Quantity = 999, Price = 400m,
        });
        await _repository.SaveChanges();

        var results = await _repository.GetByStock(target).ToListAsync();

        results.Should().HaveCount(2);
        results.Should().OnlyContain(f => f.CommonStockId == target.Id);
    }
}
