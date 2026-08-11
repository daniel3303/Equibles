using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Holdings;

public class HoldingsCloneBacktestProviderSecondaryBenchmarkTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public HoldingsCloneBacktestProviderSecondaryBenchmarkTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new HoldingsModuleConfiguration(),
            new CorporateActionsModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task Run_SecondaryBenchmark_PricesAndScopesTheRequestedListing()
    {
        var reportDate = new DateOnly(2025, 12, 31);
        var from = HoldingsBacktestCalculator.RebalanceDateOf(reportDate);
        var to = from.AddDays(100);
        var held = new CommonStock { Ticker = "HELD", Name = "Held Co" };
        var benchmark = new CommonStock
        {
            Ticker = "BRK-B",
            SecondaryTickers = ["BRK-A"],
            Name = "Berkshire Hathaway",
        };
        var holder = new InstitutionalHolder { Cik = "0001067983", Name = "Clone Capital" };
        _dbContext.AddRange(held, benchmark, holder);
        _dbContext.Add(
            new InstitutionalHolding
            {
                InstitutionalHolderId = holder.Id,
                CommonStockId = held.Id,
                ReportDate = reportDate,
                FilingDate = from,
                Shares = 1_000,
                Value = 100_000,
                FilingType = FilingType.Form13F,
            }
        );
        AddPrice(held, held.Ticker, from, 100m);
        AddPrice(held, held.Ticker, to, 100m);
        AddPrice(benchmark, "BRK-B", from, 100m);
        AddPrice(benchmark, "BRK-B", to, 100m);
        AddPrice(benchmark, "BRK-A", from, 200m);
        AddPrice(benchmark, "BRK-A", to, 400m);
        await _dbContext.SaveChangesAsync();

        var provider = new HoldingsCloneBacktestProvider(
            new InstitutionalHolderRepository(_dbContext),
            new InstitutionalHoldingRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new BacktestPriceLoader(
                new DailyStockPriceRepository(_dbContext),
                new CommonStockRepository(_dbContext),
                new StockSplitRepository(_dbContext)
            )
        );

        var outcome = await provider.Run(holder.Cik, from, to, "BRK-A");

        outcome.Benchmark.Should().Be("BRK-A");
        outcome.Result.BenchmarkSummary.TotalReturnPercent.Should().Be(100m);
    }

    private void AddPrice(CommonStock stock, string listedTicker, DateOnly date, decimal close) =>
        _dbContext.Add(
            new DailyStockPrice
            {
                CommonStockId = stock.Id,
                ListedTicker = listedTicker,
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
