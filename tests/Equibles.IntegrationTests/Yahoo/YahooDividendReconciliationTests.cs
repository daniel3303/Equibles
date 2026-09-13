using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Worker;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Yahoo;

[Collection(ParadeDbCollection.Name)]
public class YahooDividendReconciliationTests : ParadeDbMcpTestBase
{
    private EquiblesFinancialDbContext _dbContext;
    private EquityDailyStockPriceRepository _priceRepo;
    private EquityIssuerRepository _stockRepo;
    private StockSplitRepository _splitRepo;
    private CashDividendRepository _dividendRepo;
    private IYahooFinanceClient _yahooClient;
    private ISharesOutstandingProvider _sharesProvider;
    private ErrorReporter _errorReporter;
    private WorkerOptions _workerOptions;
    private YahooPriceImportService _service;

    public YahooDividendReconciliationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private void InitializeServices()
    {
        _dbContext = DbContext;
        _priceRepo = new EquityDailyStockPriceRepository(_dbContext);
        _stockRepo = new EquityIssuerRepository(_dbContext);
        _splitRepo = new StockSplitRepository(_dbContext);
        _dividendRepo = new CashDividendRepository(_dbContext);

        _yahooClient = Substitute.For<IYahooFinanceClient>();
        _errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        _workerOptions = new WorkerOptions();
        _sharesProvider = Substitute.For<ISharesOutstandingProvider>();

        // The service resolves DailyStockPriceRepository from scoped DI.
        // TickerMapService resolves CommonStockRepository from scoped DI.
        // Corporate-action reconciliation and per-ticker action capture resolve from scoped DI.
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquityDailyStockPriceRepository), _priceRepo),
            (typeof(EquityIssuerRepository), _stockRepo),
            (typeof(EquityListingRepository), new EquityListingRepository(_dbContext)),
            (typeof(StockSplitRepository), _splitRepo),
            (typeof(ISharesOutstandingProvider), _sharesProvider),
            (
                typeof(CorporateActionPriceReconciliationManager),
                new CorporateActionPriceReconciliationManager(
                    _splitRepo,
                    _dividendRepo,
                    _stockRepo,
                    new CorporateActionPriceReconciliationCursorRepository(_dbContext)
                )
            ),
            (
                typeof(StockSplitCaptureManager),
                new StockSplitCaptureManager(_splitRepo, _stockRepo)
            ),
            (
                typeof(CashDividendCaptureManager),
                new CashDividendCaptureManager(_dividendRepo, _stockRepo)
            )
        );

        var tickerMapService = new TickerMapService(scopeFactory);

        _service = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _yahooClient,
            tickerMapService,
            _errorReporter,
            Options.Create(_workerOptions),
            Options.Create(new YahooPriceScraperOptions())
        );
    }

    [Fact]
    public async Task Import_TickerReassignedDuringFetch_DoesNotAttachResponseToSibling()
    {
        InitializeServices();
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            SecondaryTickers: ["CLASS"],
            Cik: "0000320193"
        );
        var originalListing = issuer.Presentation.Listing;
        var sibling = issuer
            .Securities.SelectMany(security => security.Listings)
            .Single(row => row.Ticker == "CLASS");
        originalListing.TradingCurrency = sibling.TradingCurrency = "USD";
        _dbContext.Add(issuer);
        var exDate = new DateOnly(2025, 1, 2);
        var original = new CashDividend
        {
            Issuer = issuer,
            Listing = originalListing,
            Currency = "USD",
            ExDate = exDate,
            AmountPerShare = 1m,
            Source = CashDividendSource.Yahoo,
        };
        _dbContext.Add(original);
        var price = new EquityDailyStockPrice
        {
            Listing = originalListing,
            SourceTicker = "AAPL",
            Date = exDate,
            Open = 10,
            High = 10,
            Low = 10,
            Close = 10,
            AdjustedClose = 10,
            Volume = 1000,
        };
        _dbContext.Add(price);
        await _dbContext.SaveChangesAsync();
        _workerOptions.MinSyncDate = new(2025, 1, 1);
        var calls = 0;
        _yahooClient
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(async call =>
            {
                if (++calls != 1)
                    return new YahooChartData();
                await using var directory = Fixture.CreateDbContext();
                var listings = await directory.Set<EquityListing>().ToListAsync();
                listings.Single(row => row.Id == originalListing.Id).Ticker = "FORMER";
                listings.Single(row => row.Id == sibling.Id).Ticker = "AAPL";
                await directory.SaveChangesAsync();
                return new YahooChartData
                {
                    SourceIdentity = new()
                    {
                        Symbol = "AAPL",
                        Currency = "USD",
                        ExchangeCode = "NMS",
                        ExchangeTimeZone = "America/New_York",
                    },
                    Prices =
                    [
                        new()
                        {
                            Date = exDate,
                            Open = 99,
                            High = 99,
                            Low = 99,
                            Close = 99,
                            AdjustedClose = 99,
                            Volume = 1000,
                        },
                    ],
                    Dividends = [new() { Date = exDate, Amount = 99 }],
                    Splits =
                    [
                        new()
                        {
                            Date = exDate,
                            Numerator = 2,
                            Denominator = 1,
                        },
                    ],
                };
            });
        await _service.Import(includeEnrichment: false, CancellationToken.None);
        calls.Should().BeGreaterThan(0);
        _dbContext.ChangeTracker.Clear();
        var saved = await _dbContext.Set<CashDividend>().SingleAsync();
        saved.Id.Should().Be(original.Id);
        saved.EquityListingId.Should().Be(originalListing.Id);
        saved.AmountPerShare.Should().Be(1m);
        saved.PriceAdjustmentAppliedTime.Should().BeNull();
        (await _dbContext.Set<StockSplit>().AnyAsync()).Should().BeFalse();
        (await _dbContext.Set<EquityDailyStockPrice>().SingleAsync()).Close.Should().Be(10m);
        (
            await _dbContext
                .Set<EquityDirectorySourceRecord>()
                .AnyAsync(row => row.Source == "yahoo-chart-quotation-v1")
        )
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task Import_RequestVariantPendingDividend_StampsSameProviderResponse()
    {
        InitializeServices();
        var apple = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        apple.Presentation.Listing.TradingCurrency = "USD";
        _dbContext.Add(apple);
        await _dbContext.SaveChangesAsync();
        var beforeExDate = new DateOnly(2026, 5, 8);
        var exDate = new DateOnly(2026, 5, 11);
        foreach (var (date, close) in new[] { (beforeExDate, 100m), (exDate, 105m) })
            _dbContext.Add(
                new EquityDailyStockPrice
                {
                    Listing = apple.Presentation.Listing,
                    EquityListingId = apple.Presentation.EquityListingId,
                    SourceTicker = "AAPL",
                    Date = date,
                    Open = close,
                    High = close + 1,
                    Low = close - 1,
                    Close = close,
                    AdjustedClose = close,
                    Volume = 1000000,
                }
            );
        await _dbContext.SaveChangesAsync();
        var dividend = new CashDividend
        {
            EquityIssuerId = apple.Id,
            EquityListingId = apple.Presentation.EquityListingId,
            Currency = "USD",
            ExDate = exDate,
            AmountPerShare = 0.27m,
            Source = CashDividendSource.Yahoo,
        };
        _dividendRepo.Add(dividend);
        await _dividendRepo.SaveChanges();
        _workerOptions.MinSyncDate = new DateTime(2026, 5, 1);

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(
                new YahooChartData
                {
                    Prices =
                    [
                        new HistoricalPrice
                        {
                            Date = beforeExDate,
                            Open = 99m,
                            High = 101m,
                            Low = 98m,
                            Close = 100m,
                            AdjustedClose = 99.73m,
                            Volume = 1_000_000,
                        },
                        new HistoricalPrice
                        {
                            Date = exDate,
                            Open = 104m,
                            High = 106m,
                            Low = 103m,
                            Close = 105m,
                            AdjustedClose = 105m,
                            Volume = 1_100_000,
                        },
                    ],
                    SourceIdentity = new()
                    {
                        Symbol = "AAPL",
                        Currency = "USD",
                        ExchangeCode = "NMS",
                        ExchangeTimeZone = "America/New_York",
                    },
                    Dividends = [new CashDividendEvent { Date = exDate, Amount = 0.2701m }],
                }
            );

        await _service.Import(includeEnrichment: false, CancellationToken.None);

        var stored = _priceRepo.GetByStock(apple, "AAPL").OrderBy(price => price.Date).ToList();
        stored.Select(price => price.AdjustedClose).Should().Equal(99.73m, 105m);
        dividend.AmountPerShare.Should().Be(0.2701m);
        dividend.PriceAdjustmentAppliedAmountPerShare.Should().Be(0.2701m);
        dividend.PriceAdjustmentAppliedTime.Should().NotBeNull();
        await _yahooClient
            .Received()
            .GetChart("AAPL", new DateOnly(2026, 5, 1), Arg.Any<DateOnly>());
        await _yahooClient.DidNotReceive().GetKeyStatistics("AAPL");
    }
}
