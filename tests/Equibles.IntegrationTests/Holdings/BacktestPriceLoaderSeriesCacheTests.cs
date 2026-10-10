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

/// <summary>
/// The series cache must change what the backtest reads, never what it computes: a cached
/// listing is served from memory, extended with closes stored later, re-read after a split, and
/// keyed by the window start, and rows keep the symbol they were stored under.
/// </summary>
public class BacktestPriceLoaderSeriesCacheTests : IDisposable
{
    private static readonly DateOnly From = new(2023, 1, 3);
    private static readonly DateOnly To = new(2023, 2, 3);

    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly BacktestPriceSeriesCache _cache = new();

    public BacktestPriceLoaderSeriesCacheTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CorporateActionsModuleConfiguration(),
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _cache.Dispose();
    }

    [Fact]
    public async Task RunBacktest_CachedListing_IsServedFromMemoryUntilASplitOrNewWindowStart()
    {
        var (stock, benchmark) = await SeedPair();
        var first = await RunBacktest(stock, benchmark, From);
        first.Points[^1].PortfolioValue.Should().Be(120m);
        var windowFrom = From.AddDays(-BacktestPriceLoader.PriceLookbackDays);
        _cache.TryGet(stock.Presentation.Listing.Id, windowFrom, out var cached).Should().BeTrue();
        cached.MaxDate.Should().Be(To);
        cached.RowCount.Should().Be(2);

        // A corrected historical close without a split is not seen until the window moves.
        var row = await _dbContext
            .Set<EquityDailyStockPrice>()
            .SingleAsync(price =>
                price.EquityListingId == stock.Presentation.Listing.Id && price.Date == To
            );
        row.Close = 15m;
        await _dbContext.SaveChangesAsync();
        (await RunBacktest(stock, benchmark, From)).Points[^1].PortfolioValue.Should().Be(120m);
        (await RunBacktest(stock, benchmark, From.AddDays(1)))
            .Points[^1]
            .PortfolioValue.Should()
            .Be(150m, "a new window start reads the rows again");

        // A split captured after the load evicts the stock's series.
        _dbContext.Add(
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                EffectiveDate = To,
                Numerator = 2,
                Denominator = 1,
                CreationTime = DateTime.UtcNow.AddMinutes(1),
            }
        );
        await _dbContext.SaveChangesAsync();
        var afterSplit = await RunBacktest(stock, benchmark, From);
        afterSplit.Points.Should().BeEmpty("a captured split in the window is uncertified");
        _cache
            .TryGet(stock.Presentation.Listing.Id, windowFrom, out var reloaded)
            .Should()
            .BeTrue();
        reloaded.Segments.Single().Closes[^1].Should().Be(15m);
    }

    [Fact]
    public async Task RunBacktest_CloseStoredAfterTheLoad_IsAppendedToTheCachedSeries()
    {
        var (stock, benchmark) = await SeedPair();
        var later = To.AddDays(3);
        (await RunBacktest(stock, benchmark, From, later))
            .Points[^1]
            .PortfolioValue.Should()
            .Be(120m);

        AddPrice(stock, later, 13m);
        AddPrice(benchmark, later, 100m);
        await _dbContext.SaveChangesAsync();

        var extended = await RunBacktest(stock, benchmark, From, later);
        extended.Points[^1].PortfolioValue.Should().Be(130m);
        _cache
            .TryGet(
                stock.Presentation.Listing.Id,
                From.AddDays(-BacktestPriceLoader.PriceLookbackDays),
                out var cached
            )
            .Should()
            .BeTrue();
        cached.MaxDate.Should().Be(later);
        cached.RowCount.Should().Be(3);
    }

    [Fact]
    public async Task RunBacktest_ListingWithoutRows_GainsThemThroughTheDelta()
    {
        var (stock, benchmark) = await SeedPair();
        var empty = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "LATE",
            Name: "Late Series",
            Cik: "333"
        );
        _dbContext.Add(empty);
        Equibles.TestSupport.NativeListingSeed.ForStock(_dbContext, empty, "LATE");
        await _dbContext.SaveChangesAsync();
        var snapshots = Snapshots(stock.Id, empty.Id);

        var loader = Loader();
        var before = await loader.RunBacktest(snapshots, benchmark, "SPY", From, To);
        before.Points.Should().BeEmpty("one holding has no price at all");

        AddPrice(empty, From, 50m);
        AddPrice(empty, To, 55m);
        await _dbContext.SaveChangesAsync();
        var after = await loader.RunBacktest(snapshots, benchmark, "SPY", From, To);
        after.Reason.Should().BeNull();
        after.Points[^1].PortfolioValue.Should().Be(115m, "equal weights of +20% and +10%");
    }

    [Fact]
    public async Task RunBacktest_RowsStoredUnderAnotherSymbol_StayExcludedWhenCached()
    {
        var (stock, benchmark) = await SeedPair();
        var listing = stock.Presentation.Listing;
        _dbContext.Add(
            new EquityDailyStockPrice
            {
                EquityListingId = listing.Id,
                SourceTicker = "OLD",
                Date = From.AddDays(7),
                Open = 1m,
                High = 1m,
                Low = 1m,
                Close = 1m,
                AdjustedClose = 1m,
                Volume = 1_000,
            }
        );
        await _dbContext.SaveChangesAsync();

        var fresh = await RunBacktest(stock, benchmark, From);
        var cached = await RunBacktest(stock, benchmark, From);
        fresh.Points[^1].PortfolioValue.Should().Be(120m);
        cached
            .Points.Select(point => (point.Date, point.PortfolioValue))
            .Should()
            .Equal(fresh.Points.Select(point => (point.Date, point.PortfolioValue)));
        _cache
            .TryGet(
                listing.Id,
                From.AddDays(-BacktestPriceLoader.PriceLookbackDays),
                out var series
            )
            .Should()
            .BeTrue();
        series
            .Segments.Select(segment => segment.SourceTicker)
            .Should()
            .Equal("GOOD", "OLD", "GOOD");
    }

    private async Task<(EquityIssuer Stock, EquityIssuer Benchmark)> SeedPair()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GOOD",
            Name: "Good Co",
            Cik: "111"
        );
        EquityIssuer benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Benchmark",
            Cik: "222"
        );
        _dbContext.AddRange(stock, benchmark);
        AddPrice(stock, From, 10m);
        AddPrice(stock, To, 12m);
        AddPrice(benchmark, From, 100m);
        AddPrice(benchmark, To, 100m);
        await _dbContext.SaveChangesAsync();
        return (stock, benchmark);
    }

    private BacktestPriceLoader Loader() =>
        new(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            _cache
        );

    private Task<BacktestResult> RunBacktest(
        EquityIssuer stock,
        EquityIssuer benchmark,
        DateOnly from,
        DateOnly? to = null
    ) => Loader().RunBacktest(Snapshots(stock.Id), benchmark, "SPY", from, to ?? To);

    private static List<BacktestQuarterSnapshot> Snapshots(params Guid[] stockIds) =>
        [
            new()
            {
                ReportDate = From.AddDays(-46),
                Positions = stockIds
                    .Select(stockId => new BacktestPosition
                    {
                        CommonStockId = stockId,
                        Shares = 1_000,
                        Value = 10_000,
                    })
                    .ToList(),
            },
        ];

    private void AddPrice(EquityIssuer stock, DateOnly date, decimal close) =>
        _dbContext.Add(
            new EquityDailyStockPrice
            {
                Listing = Equibles.TestSupport.NativeListingSeed.ForStock(
                    _dbContext,
                    stock,
                    stock.Presentation.Listing.Ticker
                ),
                SourceTicker = stock.Presentation.Listing.Ticker,
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
