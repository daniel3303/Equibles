using System.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CommonStocks;

[Collection(ParadeDbCollection.Name)]
public class CommonStockRepositoryGetForUpdateTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;

    public CommonStockRepositoryGetForUpdateTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetForUpdate_ModifiedTrackedStock_ThrowsWithoutDiscardingChanges()
    {
        var stockId = Guid.NewGuid();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple",
                }
            );
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateDbContext();
        var repository = new CommonStockRepository(context);
        var tracked = await repository.GetByPrimaryTicker("AAPL");
        tracked.Name = "Pending local name";
        await using var transaction = await repository.CreateTransaction(
            IsolationLevel.ReadCommitted
        );

        var action = async () => await repository.GetForUpdate(stockId);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Modified*");
        tracked.Name.Should().Be("Pending local name");
        context.Entry(tracked).State.Should().Be(EntityState.Modified);
        await transaction.RollbackAsync();
    }
}
