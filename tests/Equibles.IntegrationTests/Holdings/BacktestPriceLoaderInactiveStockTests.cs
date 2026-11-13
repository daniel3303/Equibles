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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunBacktest_PricesRetainedInactiveHolding(bool ambiguousVenue)
    {
        var from = new DateOnly(2023, 1, 3);
        var to = new DateOnly(2023, 2, 3);
        EquityIssuer delisted = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GONE",
            Name: "Formerly Listed",
            Cik: "111",
            Active: false,
            DelistedOn: to
        );
        EquityIssuer benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Benchmark",
            Cik: "222"
        );
        _dbContext.AddRange(delisted, benchmark);
        AddPrice(delisted, from, 10m);
        AddPrice(delisted, to, 12m);
        AddPrice(benchmark, from, 100m);
        AddPrice(benchmark, to, 100m);
        if (ambiguousVenue)
        {
            var security = delisted.Presentation.Listing.Security;
            var other = new EquityListing
            {
                Security = security,
                EquitySecurityId = security.Id,
                Ticker = "GONE",
                MarketCountryCode = "US",
                MarketIdentifierCode = "XNAS",
            };
            security.Listings.Add(other);
            _dbContext.Add(
                new EquityDailyStockPrice
                {
                    Listing = other,
                    SourceTicker = "GONE",
                    Date = from,
                    Close = 900m,
                    Volume = 100,
                }
            );
        }
        await _dbContext.SaveChangesAsync();

        var loader = new BacktestPriceLoader(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            new BacktestPriceSeriesCache()
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

        if (ambiguousVenue)
        {
            result.Points.Should().BeEmpty();
            result.Reason.Should().NotBeNullOrEmpty();
            return;
        }
        result.Reason.Should().BeNull();
        result.Points.Should().NotBeEmpty();
        result.Points[^1].PortfolioValue.Should().Be(120m);
    }

    // A holding whose listing has no stored price at all is left out of the clone: the priced holding
    // carries the whole portfolio, and coverage reports the share that was left out.
    [Fact]
    public async Task RunBacktest_LeavesOutAHoldingWithoutPricesAndCountsItInCoverage()
    {
        var from = new DateOnly(2023, 1, 3);
        var to = new DateOnly(2023, 2, 3);
        EquityIssuer unpriced = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GONE",
            Name: "Formerly Listed",
            Cik: "111",
            Active: false,
            DelistedOn: to
        );
        EquityIssuer priced = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "HELD",
            Name: "Still Listed",
            Cik: "333"
        );
        EquityIssuer benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Benchmark",
            Cik: "222"
        );
        _dbContext.AddRange(unpriced, priced, benchmark);
        AddPrice(priced, from, 10m);
        AddPrice(priced, to, 12m);
        AddPrice(benchmark, from, 100m);
        AddPrice(benchmark, to, 100m);
        await _dbContext.SaveChangesAsync();

        var loader = new BacktestPriceLoader(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            new BacktestPriceSeriesCache()
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
                        CommonStockId = priced.Id,
                        Shares = 1_000,
                        Value = 10_000,
                    },
                    new BacktestPosition
                    {
                        CommonStockId = unpriced.Id,
                        Shares = 1_000,
                        Value = 30_000,
                    },
                ],
            },
        };

        var result = await loader.RunBacktest(snapshots, benchmark, "SPY", from, to);

        result.Reason.Should().BeNull();
        // Fully invested in the priced holding: 10 to 12 is +20%, not +5% on a quarter of the book.
        result.Points[^1].PortfolioValue.Should().Be(120m);
        result.Coverage.AverageLongPercent.Should().Be(25m);
        result.Coverage.AverageUnpricedPercent.Should().Be(75m);
        result.Coverage.IsRepresentative.Should().BeFalse();
    }

    // A holding with no usable close on the day the clone would buy it is left out of that
    // rebalance, whatever the reason its series does not reach the date, and the window keeps its
    // requested start: one late-priced line must not turn three years into a few months.
    [Theory]
    [InlineData("live line priced only from a later date")]
    [InlineData("retired listing with zero-volume bars only")]
    [InlineData("inactive listing without a delisting date")]
    public async Task RunBacktest_LeavesOutAHoldingWithNoUsableCloseOnTheBuyDate(string shape)
    {
        var from = new DateOnly(2023, 1, 3);
        var to = new DateOnly(2023, 2, 3);
        var live = shape.StartsWith("live");
        EquityIssuer unpriced = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "LATE",
            Name: "Priced Late Or Never",
            Cik: "111",
            Active: live,
            DelistedOn: shape.StartsWith("retired") ? to : null
        );
        EquityIssuer priced = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "HELD",
            Name: "Still Listed",
            Cik: "333"
        );
        EquityIssuer benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Benchmark",
            Cik: "222"
        );
        _dbContext.AddRange(unpriced, priced, benchmark);
        AddPrice(priced, from, 10m);
        AddPrice(priced, to, 12m);
        AddPrice(benchmark, from, 100m);
        AddPrice(benchmark, to, 100m);
        if (live)
        {
            AddPrice(unpriced, to.AddDays(-7), 50m);
            AddPrice(unpriced, to, 5m);
        }
        else if (shape.StartsWith("retired"))
        {
            AddPrice(unpriced, from, 50m, volume: 0);
            AddPrice(unpriced, to, 50m, volume: 0);
        }
        await _dbContext.SaveChangesAsync();

        var loader = new BacktestPriceLoader(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            new BacktestPriceSeriesCache()
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
                        CommonStockId = priced.Id,
                        Shares = 1_000,
                        Value = 10_000,
                    },
                    new BacktestPosition
                    {
                        CommonStockId = unpriced.Id,
                        Shares = 1_000,
                        Value = 30_000,
                    },
                ],
            },
        };

        var result = await loader.RunBacktest(snapshots, benchmark, "SPY", from, to);

        result.Reason.Should().BeNull();
        result.StartDate.Should().Be(from);
        result.Points[^1].PortfolioValue.Should().Be(120m);
        result.Coverage.AverageUnpricedPercent.Should().Be(75m);
    }

    // A line first priced between two filings is left out of the quarter it had no close for and
    // bought at the next rebalance, instead of making the whole run unavailable.
    [Fact]
    public async Task RunBacktest_LinePricedBetweenTwoRebalances_IsBoughtFromTheSecond()
    {
        var firstReport = new DateOnly(2025, 3, 31);
        var secondReport = new DateOnly(2025, 6, 30);
        var from = HoldingsBacktestCalculator.RebalanceDateOf(firstReport);
        var secondRebalance = HoldingsBacktestCalculator.RebalanceDateOf(secondReport);
        var to = secondRebalance.AddDays(30);
        EquityIssuer late = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "LATE",
            Name: "Priced Late",
            Cik: "111"
        );
        EquityIssuer priced = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "HELD",
            Name: "Still Listed",
            Cik: "333"
        );
        EquityIssuer benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Benchmark",
            Cik: "222"
        );
        _dbContext.AddRange(late, priced, benchmark);
        AddPrice(late, secondRebalance, 10m);
        AddPrice(late, to, 11m);
        AddPrice(priced, from, 20m);
        AddPrice(priced, to, 20m);
        AddPrice(benchmark, from, 100m);
        AddPrice(benchmark, to, 100m);
        await _dbContext.SaveChangesAsync();

        var loader = new BacktestPriceLoader(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            new BacktestPriceSeriesCache()
        );
        BacktestQuarterSnapshot Snapshot(DateOnly reportDate) =>
            new()
            {
                ReportDate = reportDate,
                Positions =
                [
                    new BacktestPosition
                    {
                        CommonStockId = priced.Id,
                        Shares = 1_000,
                        Value = 10_000,
                    },
                    new BacktestPosition
                    {
                        CommonStockId = late.Id,
                        Shares = 1_000,
                        Value = 10_000,
                    },
                ],
            };

        var result = await loader.RunBacktest(
            [Snapshot(firstReport), Snapshot(secondReport)],
            benchmark,
            "SPY",
            from,
            to
        );

        result.Reason.Should().BeNull();
        result.StartDate.Should().Be(from);
        // Flat in the priced holding alone, then half the book rides the late line's +10%.
        result.Points[^1].PortfolioValue.Should().Be(105m);
        result.Coverage.AverageUnpricedPercent.Should().Be(25m);
    }

    // A book whose only holding has no close yet starts at the first rebalance that can buy it:
    // sitting in cash until then would publish a flat stretch as the manager's return.
    [Fact]
    public async Task RunBacktest_NothingToBuyAtTheStart_StartsAtTheFirstPricedRebalance()
    {
        var firstReport = new DateOnly(2025, 3, 31);
        var secondReport = new DateOnly(2025, 6, 30);
        var from = HoldingsBacktestCalculator.RebalanceDateOf(firstReport);
        var secondRebalance = HoldingsBacktestCalculator.RebalanceDateOf(secondReport);
        var to = secondRebalance.AddDays(30);
        EquityIssuer late = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "LATE",
            Name: "Priced Late",
            Cik: "111"
        );
        EquityIssuer benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Benchmark",
            Cik: "222"
        );
        _dbContext.AddRange(late, benchmark);
        AddPrice(late, secondRebalance, 10m);
        AddPrice(late, to, 11m);
        AddPrice(benchmark, from, 100m);
        AddPrice(benchmark, to, 100m);
        await _dbContext.SaveChangesAsync();

        var loader = new BacktestPriceLoader(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            new BacktestPriceSeriesCache()
        );
        BacktestQuarterSnapshot Snapshot(DateOnly reportDate) =>
            new()
            {
                ReportDate = reportDate,
                Positions =
                [
                    new BacktestPosition
                    {
                        CommonStockId = late.Id,
                        Shares = 1_000,
                        Value = 10_000,
                    },
                ],
            };

        var result = await loader.RunBacktest(
            [Snapshot(firstReport), Snapshot(secondReport)],
            benchmark,
            "SPY",
            from,
            to
        );

        result.Reason.Should().BeNull();
        result.StartDate.Should().Be(secondRebalance);
        result.Points[^1].PortfolioValue.Should().Be(110m);
        result.Coverage.QuartersMeasured.Should().Be(1, "the skipped quarter was never traded");
        result.Coverage.AverageLongPercent.Should().Be(100m);
        result.Coverage.AverageUnpricedPercent.Should().Be(0m);
    }

    // A retired listing that stored bars under an earlier symbol is priced, never "without prices":
    // leaving it out would hide the loss its series shows, so the run stays unavailable.
    [Fact]
    public async Task RunBacktest_RetiredListingWithBarsUnderAnEarlierSymbol_StaysStrict()
    {
        var from = new DateOnly(2023, 1, 3);
        var to = new DateOnly(2023, 2, 3);
        EquityIssuer renamed = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GONEQ",
            Name: "Renamed Then Delisted",
            Cik: "111",
            Active: false,
            DelistedOn: to
        );
        EquityIssuer benchmark = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SPY",
            Name: "Benchmark",
            Cik: "222"
        );
        _dbContext.AddRange(renamed, benchmark);
        AddPrice(benchmark, from, 100m);
        AddPrice(benchmark, to, 100m);
        await _dbContext.SaveChangesAsync();
        _dbContext.Add(
            new EquityDailyStockPrice
            {
                Listing = renamed.Presentation.Listing,
                SourceTicker = "GONE",
                Date = from,
                Open = 10m,
                High = 10m,
                Low = 10m,
                Close = 10m,
                Volume = 100,
            }
        );
        await _dbContext.SaveChangesAsync();

        var loader = new BacktestPriceLoader(
            new EquityDailyStockPriceRepository(_dbContext),
            new EquityIssuerRepository(_dbContext),
            new StockSplitRepository(_dbContext),
            new BacktestPriceSeriesCache()
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
                        CommonStockId = renamed.Id,
                        Shares = 1_000,
                        Value = 10_000,
                    },
                ],
            },
        };

        var result = await loader.RunBacktest(snapshots, benchmark, "SPY", from, to);

        result.Points.Should().BeEmpty();
        result.Reason.Should().NotBeNullOrEmpty();
    }

    private void AddPrice(EquityIssuer stock, DateOnly date, decimal close, long volume = 1_000) =>
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
                Volume = volume,
            }
        );
}
