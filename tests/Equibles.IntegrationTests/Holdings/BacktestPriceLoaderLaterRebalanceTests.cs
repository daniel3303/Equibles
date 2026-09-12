using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;

namespace Equibles.IntegrationTests.Holdings;

public class BacktestPriceLoaderLaterRebalanceTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;

    public BacktestPriceLoaderLaterRebalanceTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CorporateActionsModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task RunBacktest_LaterRebalanceIntroducesUnpricedSecondaryListing_ReturnsUnavailable()
    {
        var firstReportDate = new DateOnly(2025, 3, 31);
        var secondReportDate = new DateOnly(2025, 6, 30);
        var from = HoldingsBacktestCalculator.RebalanceDateOf(firstReportDate);
        var secondRebalance = HoldingsBacktestCalculator.RebalanceDateOf(secondReportDate);
        var to = secondRebalance.AddDays(30);
        EquityIssuer issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "PAIR-B",
            SecondaryTickers: ["PAIR-A"],
            Name: "Paired Classes"
        );
        EquityIssuer benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Benchmark"
        );
        _dbContext.AddRange(issuer, benchmark);
        AddPrice(issuer, issuer.Presentation.Listing.Ticker, from, 100m);
        AddPrice(issuer, issuer.Presentation.Listing.Ticker, to, 110m);
        AddPrice(benchmark, benchmark.Presentation.Listing.Ticker, from, 100m);
        AddPrice(benchmark, benchmark.Presentation.Listing.Ticker, to, 100m);
        await _dbContext.SaveChangesAsync();

        var snapshots = new List<BacktestQuarterSnapshot>
        {
            Snapshot(firstReportDate, issuer.Id, listedTicker: null),
            Snapshot(secondReportDate, issuer.Id, listedTicker: "PAIR-A"),
        };
        var loader = new BacktestPriceLoader(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            new StockSplitRepository(_dbContext)
        );

        var result = await loader.RunBacktest(
            snapshots,
            benchmark,
            benchmark.Presentation.Listing.Ticker,
            from,
            to
        );

        result.Points.Should().BeEmpty();
        result.Reason.Should().Contain("rebalance").And.Contain("exact-listing price");
    }

    private static BacktestQuarterSnapshot Snapshot(
        DateOnly reportDate,
        Guid stockId,
        string listedTicker
    ) =>
        new()
        {
            ReportDate = reportDate,
            Positions =
            [
                new BacktestPosition
                {
                    CommonStockId = stockId,
                    ListedTicker = listedTicker,
                    Shares = 1_000,
                    Value = 100_000,
                },
            ],
        };

    private void AddPrice(EquityIssuer stock, string listedTicker, DateOnly date, decimal close) =>
        _dbContext.Add(
            new EquityDailyStockPrice
            {
                Listing = Equibles.TestSupport.NativeListingSeed.ForStock(
                    _dbContext,
                    stock,
                    listedTicker
                ),
                SourceTicker = listedTicker,
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
