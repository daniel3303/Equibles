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

// Stored raw closes can already reflect a split. A ratio alone cannot establish their basis.
public class BacktestPriceLoaderSplitComparabilityTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly BacktestPriceLoader _loader;

    private static readonly Guid StockId = Guid.NewGuid();
    private static readonly Guid BenchmarkId = Guid.NewGuid();

    public BacktestPriceLoaderSplitComparabilityTests()
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

    [Theory]
    [InlineData(500, 125, 4, 1)]
    [InlineData(125, 125, 4, 1)]
    [InlineData(10, 100, 1, 10)]
    [InlineData(125, 125, 0, 0)]
    [InlineData(124.8075, 134.18, 4, 1)]
    [InlineData(120.888, 120.91, 10, 1)]
    public async Task HeldSplit_RefusesEveryUncertifiedBasis(
        decimal before,
        decimal after,
        decimal numerator,
        decimal denominator
    )
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, SplitDate, numerator, denominator);
        await SeedPrices(stock, "ACME", (Start, before), (SplitDate, after), (End, after));
        await SeedPrices(benchmark, "SPY", (Start, 100m), (End, 100m));

        var result = await Run(benchmark, [Snapshot(ReportDate)]);

        Assert.True(result.HasUncertifiedSplitPrices);
        Assert.Contains("captured split", result.Reason);
        Assert.Empty(result.Points);
        Assert.Equal(Start, result.StartDate);
        Assert.Equal(End, result.EndDate);
        Assert.Null(result.PortfolioSummary.CagrPercent);
    }

    [Fact]
    public async Task BenchmarkSplit_RefusesReturn()
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(benchmark, SplitDate, 4, 1);
        await SeedPrices(stock, "ACME", (Start, 100m), (End, 110m));
        await SeedPrices(benchmark, "SPY", (Start, 100m), (SplitDate, 100m), (End, 100m));
        Assert.True((await Run(benchmark, [Snapshot(ReportDate)])).HasUncertifiedSplitPrices);
    }

    [Fact]
    public async Task SplitAfterSale_DoesNotInvalidateWindow()
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, SplitDate, 4, 1);
        await SeedPrices(stock, "ACME", (Start, 100m), (SplitDate, 100m), (End, 100m));
        await SeedPrices(benchmark, "SPY", (Start, 100m), (End, 100m));
        var sold = new BacktestQuarterSnapshot
        {
            ReportDate = SplitDate.AddDays(-46),
            Positions = [],
        };
        var result = await Run(benchmark, [Snapshot(ReportDate), sold]);
        Assert.Null(result.Reason);
        Assert.Equal(Start, result.StartDate);
        Assert.Equal(End, result.EndDate);
    }

    [Fact]
    public async Task SplitOnSaleDate_StillRefusesOldPositionValuation()
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, SplitDate, 4, 1);
        await SeedPrices(stock, "ACME", (Start, 100m), (SplitDate, 100m), (End, 100m));
        await SeedPrices(benchmark, "SPY", (Start, 100m), (End, 100m));
        var sold = new BacktestQuarterSnapshot
        {
            ReportDate = SplitDate.AddDays(-45),
            Positions = [],
        };
        Assert.True((await Run(benchmark, [Snapshot(ReportDate), sold])).HasUncertifiedSplitPrices);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BoughtAfterSplit_RequiresPostSplitEntryClose(bool postSplitBar)
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, SplitDate, 4, 1);
        await SeedPrices(stock, "ACME", (Start, 100m), (End, 110m));
        if (postSplitBar)
            await SeedPrices(stock, "ACME", (SplitDate, 100m));
        await SeedPrices(benchmark, "SPY", (Start, 100m), (End, 100m));
        var cash = new BacktestQuarterSnapshot { ReportDate = ReportDate, Positions = [] };
        var bought = Snapshot(SplitDate.AddDays(-44));
        var result = await Run(benchmark, [cash, bought]);
        Assert.Equal(!postSplitBar, result.HasUncertifiedSplitPrices);
        if (postSplitBar)
            Assert.Equal(10m, result.PortfolioSummary.TotalReturnPercent);
        else
            Assert.Empty(result.Points);
    }

    [Fact]
    public async Task SiblingListingSplit_DoesNotInvalidatePrimary()
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, SplitDate, 4, 1);
        (await _dbContext.Set<StockSplit>().SingleAsync()).PriceSeriesTicker = "ACME-B";
        await _dbContext.SaveChangesAsync();
        await SeedPrices(stock, "ACME", (Start, 100m), (End, 110m));
        await SeedPrices(benchmark, "SPY", (Start, 100m), (End, 100m));
        var result = await Run(benchmark, [Snapshot(ReportDate)]);
        Assert.Null(result.Reason);
        Assert.Equal(10m, result.PortfolioSummary.TotalReturnPercent);
    }

    [Fact]
    public async Task SplitBeforeRequestedStart_WithFreshClose_PreservesFullWindow()
    {
        var (stock, benchmark) = await SeedStocks();
        await SeedSplit(stock, Start.AddDays(-1), 4, 1);
        await SeedPrices(stock, "ACME", (Start, 100m), (End, 110m));
        await SeedPrices(benchmark, "SPY", (Start, 100m), (End, 100m));
        var result = await Run(benchmark, [Snapshot(ReportDate)]);
        Assert.Null(result.Reason);
        Assert.Equal(Start, result.StartDate);
        Assert.Equal(10m, result.PortfolioSummary.TotalReturnPercent);
    }

    private static readonly DateOnly Start = new(2026, 3, 2);
    private static readonly DateOnly End = new(2026, 3, 23);
    private static readonly DateOnly SplitDate = new(2026, 3, 10);
    private static readonly DateOnly ReportDate = new(2026, 1, 15);

    private Task<BacktestResult> Run(
        CommonStock benchmark,
        IReadOnlyList<BacktestQuarterSnapshot> snapshots
    ) => _loader.RunBacktest(snapshots, benchmark, "SPY", Start, End);

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
