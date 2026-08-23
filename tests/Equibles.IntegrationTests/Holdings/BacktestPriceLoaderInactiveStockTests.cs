using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Repositories.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Holdings;

public class BacktestPriceLoaderInactiveStockTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public BacktestPriceLoaderInactiveStockTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CorporateActionsModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task RunBacktest_PricesRetainedInactiveHolding()
    {
        var from = new DateOnly(2023, 1, 3);
        var to = new DateOnly(2023, 2, 3);
        var delisted = new CommonStock
        {
            Ticker = "GONE",
            Name = "Formerly Listed",
            Cik = "111",
            Active = false,
            DelistedOn = to,
        };
        var benchmark = new CommonStock
        {
            Ticker = "SPY",
            Name = "Benchmark",
            Cik = "222",
        };
        _dbContext.AddRange(delisted, benchmark);
        AddPrice(delisted, from, 10m);
        AddPrice(delisted, to, 12m);
        AddPrice(benchmark, from, 100m);
        AddPrice(benchmark, to, 100m);
        await _dbContext.SaveChangesAsync();

        var loader = new BacktestPriceLoader(
            new DailyStockPriceRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new StockSplitRepository(_dbContext)
        );
        var snapshots = new[]
        {
            new BacktestQuarterSnapshot
            {
                ReportDate = from.AddDays(-46),
                Positions =
                [
                    new BacktestPosition
                    {
                        CommonStockId = delisted.Id,
                        Shares = 1_000,
                        Value = 10_000,
                    },
                ],
            },
        };

        var result = await loader.RunBacktest(snapshots, benchmark, "SPY", from, to);

        result.Reason.Should().BeNull();
        result.Points.Should().NotBeEmpty();
        result.Points[^1].PortfolioValue.Should().Be(120m);
    }

    private void AddPrice(CommonStock stock, DateOnly date, decimal close) =>
        _dbContext.Add(
            new DailyStockPrice
            {
                CommonStockId = stock.Id,
                ListedTicker = stock.Ticker,
                Date = date,
                Open = close,
                High = close,
                Low = close,
                Close = close,
                AdjustedClose = close,
                Volume = 1_000,
            }
        );
}
