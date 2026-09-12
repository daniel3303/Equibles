using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Equibles.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

public class FailToDeliverRepositoryTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly FailToDeliverRepository _repository;

    public FailToDeliverRepositoryTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );
        _repository = new FailToDeliverRepository(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetByStock_MultipleStocksWithFails_ReturnsOnlyTargetStockRecords()
    {
        EquityIssuer target = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc."
        );
        EquityIssuer other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "MSFT",
            Name: "Microsoft Corp."
        );
        _dbContext.Set<EquityIssuer>().AddRange(target, other);

        _repository.Add(
            new FailToDeliver
            {
                EquityListingId = NativeListingSeed.ForStock(_dbContext, target).Id,

                ListedTicker = target.Presentation.Listing.Ticker,
                SettlementDate = new DateOnly(2025, 10, 1),
                Quantity = 100,
                Price = 150m,
            }
        );
        _repository.Add(
            new FailToDeliver
            {
                EquityListingId = NativeListingSeed.ForStock(_dbContext, target).Id,

                ListedTicker = target.Presentation.Listing.Ticker,
                SettlementDate = new DateOnly(2025, 10, 2),
                Quantity = 200,
                Price = 151m,
            }
        );
        _repository.Add(
            new FailToDeliver
            {
                EquityListingId = NativeListingSeed.ForStock(_dbContext, other).Id,

                ListedTicker = other.Presentation.Listing.Ticker,
                SettlementDate = new DateOnly(2025, 10, 1),
                Quantity = 999,
                Price = 400m,
            }
        );
        await _repository.SaveChanges();

        var results = await _repository.GetByStock(target).ToListAsync();

        results.Should().HaveCount(2);
        results.Should().OnlyContain(f => f.Listing.Security.EquityIssuerId == target.Id);
    }
}
