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
