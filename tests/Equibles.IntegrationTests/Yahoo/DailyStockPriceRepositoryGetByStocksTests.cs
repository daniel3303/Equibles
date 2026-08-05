using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Yahoo;

public class DailyStockPriceRepositoryGetByStocksTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly DailyStockPriceRepository _repository;

    public DailyStockPriceRepositoryGetByStocksTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _repository = new DailyStockPriceRepository(_dbContext);
    }

    public void Dispose() => _dbContext.Dispose();

    // Contract: prices for any stock in the id set whose Date falls within the
    // INCLUSIVE [startDate, endDate] window. Both boundaries are inclusive; a day
    // outside the window, or a stock outside the id set, must be excluded.
    [Fact]
    public async Task GetByStocks_FiltersByIdSetAndInclusiveDateRange()
    {
        var inSet = Guid.NewGuid();
        var notInSet = Guid.NewGuid();
        var start = new DateOnly(2024, 6, 10);
        var end = new DateOnly(2024, 6, 20);

        _dbContext
            .Set<CommonStock>()
            .AddRange(
                new CommonStock { Id = inSet, Ticker = "IN" },
                new CommonStock { Id = notInSet, Ticker = "OUT" }
            );

        _dbContext
            .Set<DailyStockPrice>()
            .AddRange(
                Price(inSet, "IN", start.AddDays(-1)), // before window — excluded
                Price(inSet, "IN", start), // lower boundary — included
                Price(inSet, "IN", end), // upper boundary — included
                Price(inSet, "IN", end.AddDays(1)), // after window — excluded
                Price(notInSet, "OUT", new DateOnly(2024, 6, 15)) // in range but wrong stock — excluded
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _repository
            .GetByStocks([inSet], start, end)
            .ToListAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.CommonStockId == inSet);
        result.Select(p => p.Date).Should().BeEquivalentTo([start, end]);
    }

    private static DailyStockPrice Price(Guid stockId, string listedTicker, DateOnly date) =>
        new()
        {
            CommonStockId = stockId,
            ListedTicker = listedTicker,
            Date = date,
            Close = 100m,
        };
}
