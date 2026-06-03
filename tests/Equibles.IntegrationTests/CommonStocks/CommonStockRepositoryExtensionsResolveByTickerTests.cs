using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.CommonStocks;

// Lane A (adversarial): ResolveByTicker normalizes the ticker before lookup, so
// a caller passing a ticker in the "wrong" case must still resolve the stored
// (uppercase) stock with a null error. If the Normalize step were dropped, the
// lowercase lookup would miss and return a not-found error instead.
public class CommonStockRepositoryExtensionsResolveByTickerTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly CommonStockRepository _repository;

    public CommonStockRepositoryExtensionsResolveByTickerTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task ResolveByTicker_TickerSuppliedInLowercase_ResolvesStoredUppercaseStock()
    {
        _dbContext
            .Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Id = Guid.NewGuid(),
                    Ticker = "AAPL",
                    Name = "Apple Inc",
                    Cik = "0000320193",
                }
            );
        await _dbContext.SaveChangesAsync();

        var (stock, error) = await _repository.ResolveByTicker("  aapl  ");

        stock.Should().NotBeNull();
        stock.Ticker.Should().Be("AAPL");
        error.Should().BeNull();
    }
}
