using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.CommonStocks;

// Lane A (adversarial): GetExistingIds is the stale-FK guard importers run before
// insert — its contract is "the subset of the given ids whose CommonStock still
// exists". A mix of one live id and one orphan id (a stock hard-deleted after the
// ticker map was built) must come back as the live id ALONE, so the dangling FK is
// dropped rather than rolling back the whole batch. Oracle derived from the contract.
public class CommonStockRepositoryExtensionsGetExistingIdsTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly CommonStockRepository _repository;

    public CommonStockRepositoryExtensionsGetExistingIdsTests()
    {
        _dbContext = TestDbContextFactory.Create(new CommonStocksModuleConfiguration());
        _repository = new CommonStockRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task GetExistingIds_MixOfLiveAndOrphanId_ReturnsOnlyTheLiveId()
    {
        var liveId = Guid.NewGuid();
        var orphanId = Guid.NewGuid();
        _dbContext
            .Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Id = liveId,
                    Ticker = "AAPL",
                    Name = "Apple Inc",
                    Cik = "0000320193",
                }
            );
        await _dbContext.SaveChangesAsync();

        var existing = await _repository.GetExistingIds([liveId, orphanId]);

        existing.Should().BeEquivalentTo(new[] { liveId });
        existing.Should().NotContain(orphanId);
    }
}
