using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CorporateActions;

[Collection(ParadeDbCollection.Name)]
public class CashDividendCaptureManagerDesignationRaceTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;

    public CashDividendCaptureManagerDesignationRaceTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Capture_TrackedPrimaryChangedBeforeLock_UsesLockedDatabaseTicker()
    {
        var stockId = Guid.NewGuid();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "GOOGL",
                    SecondaryTickers = ["GOOG"],
                }
            );
            await seed.SaveChangesAsync();
        }

        await using var capture = _fixture.CreateDbContext();
        var stockRepository = new CommonStockRepository(capture);
        var staleStock = await stockRepository.GetByPrimaryTicker("GOOGL");
        staleStock.Should().NotBeNull();

        await using (var designation = _fixture.CreateDbContext())
        {
            var currentStock = await designation.Set<CommonStock>().SingleAsync();
            currentStock.Ticker = "GOOG";
            currentStock.SecondaryTickers = ["GOOGL"];
            await designation.SaveChangesAsync();
        }

        // This context still holds the pre-fetch snapshot. The capture boundary must acquire the
        // row lock and refresh it before deciding whether the observed ticker is still primary.
        staleStock.Ticker.Should().Be("GOOGL");
        var manager = new CashDividendCaptureManager(
            new CashDividendRepository(capture),
            stockRepository
        );
        var dividend = new CapturedDividend
        {
            ExDate = new DateOnly(2026, 8, 8),
            AmountPerShare = 0.25m,
            Source = CashDividendSource.External,
        };

        var staleWrite = await manager.Capture(stockId, "GOOGL", [dividend]);

        staleWrite.Should().Be(0);
        staleStock.Ticker.Should().Be("GOOG");
        (await capture.Set<CashDividend>().ToListAsync()).Should().BeEmpty();

        var currentWrite = await manager.Capture(stockId, "GOOG", [dividend]);

        currentWrite.Should().Be(1);
        await using var verify = _fixture.CreateDbContext();
        var stored = await verify.Set<CashDividend>().SingleAsync();
        stored.CommonStockId.Should().Be(stockId);
    }
}
