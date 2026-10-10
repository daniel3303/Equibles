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
/// listing is served from memory outside its unsettled tail, re-read inside it, extended with
/// closes stored later, re-read whole after a split or a new window start, and rows keep the
/// symbol they were stored under.
/// </summary>
public class BacktestPriceLoaderSeriesCacheTests : IDisposable
{
    private static readonly DateOnly From = new(2023, 1, 3);
    private static readonly DateOnly To = new(2023, 2, 3);
    private static readonly DateOnly WindowFrom = From.AddDays(
        -BacktestPriceLoader.PriceLookbackDays
    );

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
    public async Task RunBacktest_CachedListing_IsServedFromMemoryOutsideItsTailUntilASplitOrNewWindowStart()
    {
        var (stock, benchmark) = await SeedPair();
        var listingId = stock.Presentation.Listing.Id;
        (await RunBacktest(stock, benchmark, From)).Points[^1].PortfolioValue.Should().Be(120m);
        _cache.TryGet(listingId, WindowFrom, out var cached).Should().BeTrue();
        cached.MaxDate.Should().Be(To);
        cached.RowCount.Should().Be(2);

        // The first close is a month before the last cached date, outside the unsettled tail:
        // a correction there without a split is served from memory.
        await SetClose(listingId, From, 8m);
        (await RunBacktest(stock, benchmark, From)).Points[^1].PortfolioValue.Should().Be(120m);

        // The last close is inside the tail and is re-read on the next request.
        await SetClose(listingId, To, 15m);
        (await RunBacktest(stock, benchmark, From))
            .Points[^1]
            .PortfolioValue.Should()
            .Be(150m, "15 over the still-cached 10");

        // A new window start reads the whole window again.
        var nextFrom = From.AddDays(1);
        (await RunBacktest(stock, benchmark, nextFrom))
            .Points[^1]
            .PortfolioValue.Should()
            .Be(187.5m, "15 over the corrected 8");
        _cache
            .TryGet(listingId, WindowFrom, out _)
            .Should()
            .BeFalse("yesterday's window is released");

        // A split captured after that read evicts the stock's series: a close outside the tail
        // is re-read only because of it.
        await SetClose(listingId, From, 9m);
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
        var afterSplit = await RunBacktest(stock, benchmark, nextFrom);
        afterSplit.Points.Should().BeEmpty("a captured split in the window is uncertified");
        _cache.TryGet(listingId, WindowFrom.AddDays(1), out var reloaded).Should().BeTrue();
        reloaded.Segments.Single().Closes.Should().Equal(9m, 15m);
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
        _cache.TryGet(stock.Presentation.Listing.Id, WindowFrom, out var cached).Should().BeTrue();
        cached.MaxDate.Should().Be(later);
        cached.RowCount.Should().Be(3);
    }

    [Fact]
    public async Task RunBacktest_EndEarlierThanTheCachedEnd_StopsThereAndKeepsTheCache()
    {
        var (stock, benchmark) = await SeedPair();
        var middle = From.AddDays(14);
        AddPrice(stock, middle, 11m);
        AddPrice(benchmark, middle, 100m);
        await _dbContext.SaveChangesAsync();
        (await RunBacktest(stock, benchmark, From)).Points[^1].PortfolioValue.Should().Be(120m);

        var earlierEnd = middle.AddDays(3);
        var shorter = await RunBacktest(stock, benchmark, From, earlierEnd);
        shorter.Points.Should().OnlyContain(point => point.Date <= earlierEnd);
        shorter.Points[^1].PortfolioValue.Should().Be(110m);
        _cache.TryGet(stock.Presentation.Listing.Id, WindowFrom, out var cached).Should().BeTrue();
        cached.MaxDate.Should().Be(To, "a shorter read never truncates the cached series");
    }

    [Fact]
    public async Task RunBacktest_ListingWithoutRows_GainsThemThroughTheReRead()
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
        _cache.TryGet(listing.Id, WindowFrom, out var series).Should().BeTrue();
        series
            .Segments.Select(segment => segment.SourceTicker)
            .Should()
            .Equal("GOOD", "OLD", "GOOD");
    }

    [Fact]
    public async Task RunBacktest_ListingDelistedInTheLiveMonth_SharesTheBucketButKeepsItsOwnTail()
    {
        var (stock, benchmark) = await SeedPair();
        var gone = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GONE",
            Name: "Gone Co",
            Cik: "444"
        );
        _dbContext.Add(gone);
        Equibles.TestSupport.NativeListingSeed.ForStock(_dbContext, gone, "GONE");
        // GONE ends on Feb 1, so its tail starts Jan 12; GOOD ends on To (Feb 3), tail from Jan 14.
        var betweenTheTails = new DateOnly(2023, 1, 13);
        var delistedOn = new DateOnly(2023, 2, 1);
        AddPrice(stock, betweenTheTails, 10m);
        AddPrice(gone, From, 10m);
        AddPrice(gone, betweenTheTails, 10m);
        AddPrice(gone, delistedOn, 11m);
        await _dbContext.SaveChangesAsync();
        var snapshots = Snapshots(stock.Id, gone.Id);
        var loader = Loader();
        (await loader.RunBacktest(snapshots, benchmark, "SPY", From, To)).Reason.Should().BeNull();

        await SetClose(stock.Presentation.Listing.Id, betweenTheTails, 99m);
        await SetClose(gone.Presentation.Listing.Id, betweenTheTails, 77m);
        await loader.RunBacktest(snapshots, benchmark, "SPY", From, To);

        _cache.TryGet(stock.Presentation.Listing.Id, WindowFrom, out var live).Should().BeTrue();
        live.Segments.Single()
            .Closes.Should()
            .Equal(
                [10m, 10m, 12m],
                "Jan 13 is outside the live listing's own tail even though its bucket's read starts earlier"
            );
        _cache.TryGet(gone.Presentation.Listing.Id, WindowFrom, out var delisted).Should().BeTrue();
        delisted
            .Segments.Single()
            .Closes.Should()
            .Equal([10m, 77m, 11m], "Jan 13 is inside the delisted listing's own tail");
    }

    private async Task SetClose(Guid listingId, DateOnly date, decimal close)
    {
        var row = await _dbContext
            .Set<EquityDailyStockPrice>()
            .SingleAsync(price => price.EquityListingId == listingId && price.Date == date);
        row.Close = close;
        await _dbContext.SaveChangesAsync();
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
