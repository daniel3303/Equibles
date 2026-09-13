using System.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteLock_ModifiedTrackedStock_ThrowsWithoutDiscardingChanges(bool keyUpdate)
    {
        var stockId = Guid.NewGuid();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: stockId,
                    Ticker: "AAPL",
                    Name: "Apple"
                )
            );
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateDbContext();
        EquityIssuerRepository repository = new EquityIssuerRepository(context);
        EquityIssuer tracked = await repository.GetPrimaryUsByTicker("AAPL");
        tracked.Name = "Pending local name";
        await using var transaction = await repository.CreateTransaction(
            IsolationLevel.ReadCommitted
        );

        var action = async () => await Lock(repository, stockId, keyUpdate);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Modified*");
        tracked.Name.Should().Be("Pending local name");
        context.Entry(tracked).State.Should().Be(EntityState.Modified);
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteLock_ForeignKeyInsert_OnlyKeyUpdateBlocks(bool keyUpdate)
    {
        var stockId = await SeedStock();
        await using var context = _fixture.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Lock(new EquityIssuerRepository(context), stockId, keyUpdate);

        await using var writer = new NpgsqlConnection(_fixture.ConnectionString);
        await writer.OpenAsync();
        await using var setup = new NpgsqlCommand("SET lock_timeout = '500ms'", writer);
        await setup.ExecuteNonQueryAsync();
        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO "EquitySecurity" ("Id", "EquityIssuerId", "SecurityType", "RegistrationType", "SharesOutstanding", "MarketCapitalization")
            VALUES (@id, @stock, 0, 0, 0, 0)
            """,
            writer
        );
        insert.Parameters.AddWithValue("stock", stockId);
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        if (keyUpdate)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                insert.ExecuteNonQueryAsync()
            );
            error.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);
        }
        else
        {
            (await insert.ExecuteNonQueryAsync()).Should().Be(1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoKeyUpdate_ConcurrentStockWriter_StillBlocks(bool delete)
    {
        var stockId = await SeedStock();
        await using var context = _fixture.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await new EquityIssuerRepository(context).GetForNoKeyUpdate(stockId);
        await using var writer = new NpgsqlConnection(_fixture.ConnectionString);
        await writer.OpenAsync();
        await using var timeout = new NpgsqlCommand("SET lock_timeout = '500ms'", writer);
        await timeout.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand(
            delete
                ? """DELETE FROM "EquityIssuer" WHERE "Id" = @stock"""
                : """UPDATE "EquityIssuer" SET "Name" = 'Competing writer' WHERE "Id" = @stock""",
            writer
        );
        command.Parameters.AddWithValue("stock", stockId);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            command.ExecuteNonQueryAsync()
        );
        error.SqlState.Should().Be(PostgresErrorCodes.LockNotAvailable);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteLock_PreloadedSnapshot_RefreshesAfterLock(bool keyUpdate)
    {
        var stockId = await SeedStock();
        await using var context = _fixture.CreateDbContext();
        EquityIssuerRepository repository = new EquityIssuerRepository(context);
        EquityIssuer tracked = await repository.GetPrimaryUsByTicker("AAPL");
        await using (var writer = _fixture.CreateDbContext())
        {
            await writer
                .Set<EquityIssuer>()
                .Where(stock => stock.Id == stockId)
                .ExecuteUpdateAsync(update =>
                    update.SetProperty(stock => stock.Name, "Updated issuer")
                );
        }
        await using var transaction = await context.Database.BeginTransactionAsync();
        EquityIssuer locked = await Lock(repository, stockId, keyUpdate);
        locked.Should().BeSameAs(tracked);
        locked.Name.Should().Be("Updated issuer");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteLock_WithoutTransaction_RefusesUnprotectedRead(bool keyUpdate)
    {
        await using var context = _fixture.CreateDbContext();
        var action = () => Lock(new EquityIssuerRepository(context), Guid.NewGuid(), keyUpdate);
        await action
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*active transaction*");
    }

    private async Task<Guid> SeedStock()
    {
        await using var context = _fixture.CreateDbContext();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple"
        );
        context.Add(stock);
        await context.SaveChangesAsync();
        return stock.Id;
    }

    private static Task<EquityIssuer> Lock(
        EquityIssuerRepository repository,
        Guid stockId,
        bool keyUpdate
    ) => keyUpdate ? repository.GetForUpdate(stockId) : repository.GetForNoKeyUpdate(stockId);
}
