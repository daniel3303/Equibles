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
    private readonly EquityDailyStockPriceRepository _repository;

    public DailyStockPriceRepositoryGetByStocksTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _repository = new EquityDailyStockPriceRepository(_dbContext);
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
            .Set<EquityDailyStockPrice>()
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
        result.Should().OnlyContain(p => p.Listing.Security.EquityIssuerId == inSet);
        result.Select(p => p.Date).Should().BeEquivalentTo([start, end]);
    }

    [Fact]
    public async Task GetTradedByStocks_ExcludesCarryForwardAndInvalidVolumeRows()
    {
        var stockId = Guid.NewGuid();
        var start = new DateOnly(2026, 8, 7);
        var end = new DateOnly(2026, 8, 11);
        _dbContext.Set<CommonStock>().Add(new CommonStock { Id = stockId, Ticker = "THIN" });
        _dbContext
            .Set<EquityDailyStockPrice>()
            .AddRange(
                Price(stockId, "THIN", start, 10),
                Price(stockId, "THIN", start.AddDays(1), 0),
                Price(stockId, "THIN", end, -1)
            );
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        var result = await _repository
            .GetTradedByStocks([stockId], start, end)
            .ToListAsync(CancellationToken.None);

        result.Should().ContainSingle().Which.Date.Should().Be(start);
    }

    private EquityDailyStockPrice Price(
        Guid stockId,
        string listedTicker,
        DateOnly date,
        long volume = 100
    ) =>
        new()
        {
            Listing = Equibles.TestSupport.NativeListingSeed.ForStockId(
                _dbContext,
                stockId,
                listedTicker
            ),
            SourceTicker = listedTicker,
            Date = date,
            Close = 100m,
            Volume = volume,
        };
}
