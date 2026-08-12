using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Repositories.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

// The clone backtest used to bound every listing at its latest captured split and floor the
// WHOLE simulation at the latest boundary across the book, so one recent split in any single
// holding collapsed a five-year request to weeks while a headline alpha was still reported
// (#4368). These tests pin the replacement: pre-split closes are restated onto the current
// basis with the captured ratio, the requested window is honoured, and the split day itself
// contributes no phantom return. An unusable ratio keeps the old exclusion for its own listing.
public class BacktestPriceLoaderSplitRestatementTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly BacktestPriceLoader _loader;

    private static readonly Guid StockId = Guid.NewGuid();
    private static readonly Guid BenchmarkId = Guid.NewGuid();

    public BacktestPriceLoaderSplitRestatementTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new CorporateActionsModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _loader = new BacktestPriceLoader(
            new DailyStockPriceRepository(_dbContext),
            new CommonStockRepository(_dbContext),
            new StockSplitRepository(_dbContext)
        );
    }

    public void Dispose() => _dbContext.Dispose();

    // One snapshot holds only ACME; ACME trades flat at 500 pre-split and flat at 125 after a
    // 4:1 split. Restated onto the post-split basis the series is a constant 125, so the
    // simulation must span the whole window with a ~0% return — the old behavior either
    // started at the split day or reported the -75% raw-close cliff.
    [Fact]
    public async Task RunBacktest_SpansACapturedSplit_WithNoPhantomReturn()
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, new DateOnly(2026, 3, 10), numerator: 4, denominator: 1);
        // Trading days around the split; 2026-03-10 itself trades post-split.
        await SeedPrices(
            stock,
            "ACME",
            (new DateOnly(2026, 3, 2), 500m),
            (new DateOnly(2026, 3, 5), 500m),
            (new DateOnly(2026, 3, 10), 125m),
            (new DateOnly(2026, 3, 16), 125m),
            (new DateOnly(2026, 3, 23), 125m)
        );
        await SeedPrices(
            benchmark,
            "SPY",
            (new DateOnly(2026, 3, 2), 100m),
            (new DateOnly(2026, 3, 5), 100m),
            (new DateOnly(2026, 3, 10), 100m),
            (new DateOnly(2026, 3, 16), 100m),
            (new DateOnly(2026, 3, 23), 100m)
        );

        var result = await _loader.RunBacktest(
            [Snapshot(new DateOnly(2026, 1, 15))],
            benchmark,
            "SPY",
            from: new DateOnly(2026, 3, 2),
            to: new DateOnly(2026, 3, 23)
        );

        Assert.NotNull(result);
        Assert.Null(result.Reason);
        Assert.Equal(new DateOnly(2026, 3, 2), result.StartDate);
        Assert.Equal(0m, Math.Round(result.PortfolioSummary.TotalReturnPercent, 4));
    }

    // A reverse split (1:10) multiplies pre-split closes by 10 on the current basis: 10 → 100.
    [Fact]
    public async Task RunBacktest_RestatesAReverseSplitUpward()
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, new DateOnly(2026, 3, 10), numerator: 1, denominator: 10);
        await SeedPrices(
            stock,
            "ACME",
            (new DateOnly(2026, 3, 2), 10m),
            (new DateOnly(2026, 3, 10), 100m),
            (new DateOnly(2026, 3, 16), 110m)
        );
        await SeedPrices(
            benchmark,
            "SPY",
            (new DateOnly(2026, 3, 2), 100m),
            (new DateOnly(2026, 3, 10), 100m),
            (new DateOnly(2026, 3, 16), 100m)
        );

        var result = await _loader.RunBacktest(
            [Snapshot(new DateOnly(2026, 1, 15))],
            benchmark,
            "SPY",
            from: new DateOnly(2026, 3, 2),
            to: new DateOnly(2026, 3, 16)
        );

        Assert.NotNull(result);
        Assert.Null(result.Reason);
        Assert.Equal(new DateOnly(2026, 3, 2), result.StartDate);
        // 100 → 110 on the restated series = +10%.
        Assert.Equal(10m, Math.Round(result.PortfolioSummary.TotalReturnPercent, 2));
    }

    // A split with an unusable ratio cannot restate anything across it, so its listing's
    // pre-boundary closes are dropped and the simulation starts at the boundary — the old,
    // honest fallback, scoped to the one listing.
    [Fact]
    public async Task RunBacktest_UnusableRatioSplit_StillExcludesPreBoundaryCloses()
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, new DateOnly(2026, 3, 10), numerator: 0, denominator: 0);
        await SeedPrices(
            stock,
            "ACME",
            (new DateOnly(2026, 3, 2), 500m),
            (new DateOnly(2026, 3, 10), 125m),
            (new DateOnly(2026, 3, 16), 125m)
        );
        await SeedPrices(
            benchmark,
            "SPY",
            (new DateOnly(2026, 3, 2), 100m),
            (new DateOnly(2026, 3, 10), 100m),
            (new DateOnly(2026, 3, 16), 100m)
        );

        var result = await _loader.RunBacktest(
            [Snapshot(new DateOnly(2026, 1, 15))],
            benchmark,
            "SPY",
            from: new DateOnly(2026, 3, 2),
            to: new DateOnly(2026, 3, 16)
        );

        Assert.NotNull(result);
        Assert.Null(result.Reason);
        Assert.Equal(new DateOnly(2026, 3, 10), result.StartDate);
        Assert.Equal(0m, Math.Round(result.PortfolioSummary.TotalReturnPercent, 4));
    }

    private BacktestQuarterSnapshot Snapshot(DateOnly reportDate) =>
        new()
        {
            ReportDate = reportDate,
            Positions =
            [
                new BacktestPosition
                {
                    CommonStockId = StockId,
                    Shares = 100,
                    Value = 50_000,
                },
            ],
        };

    private async Task<(CommonStock Stock, CommonStock Benchmark)> SeedStocks()
    {
        var stock = new CommonStock
        {
            Id = StockId,
            Ticker = "ACME",
            Name = "Acme Corp",
        };
        var benchmark = new CommonStock
        {
            Id = BenchmarkId,
            Ticker = "SPY",
            Name = "SPDR S&P 500",
        };
        _dbContext.Set<CommonStock>().AddRange(stock, benchmark);
        await _dbContext.SaveChangesAsync();
        return (stock, benchmark);
    }

    private async Task SeedSplit(
        CommonStock stock,
        DateOnly effectiveDate,
        decimal numerator,
        decimal denominator
    )
    {
        _dbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    CommonStockId = stock.Id,
                    EffectiveDate = effectiveDate,
                    Numerator = numerator,
                    Denominator = denominator,
                    PriceSeriesTicker = stock.Ticker,
                }
            );
        await _dbContext.SaveChangesAsync();
    }

    private async Task SeedPrices(
        CommonStock stock,
        string listedTicker,
        params (DateOnly Date, decimal Close)[] bars
    )
    {
        foreach (var (date, close) in bars)
        {
            _dbContext
                .Set<DailyStockPrice>()
                .Add(
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
        await _dbContext.SaveChangesAsync();
    }
}
