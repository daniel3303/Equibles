using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.CorporateActions.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Worker;
using Equibles.Yahoo.Data;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.HostedService.Services;
using Equibles.Yahoo.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.IntegrationTests.Yahoo;

public class YahooPriceImportServiceTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly DailyStockPriceRepository _priceRepo;
    private readonly CommonStockRepository _stockRepo;
    private readonly StockSplitRepository _splitRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly ISharesOutstandingProvider _sharesProvider;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;
    private readonly YahooPriceImportService _service;

    public YahooPriceImportServiceTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _priceRepo = new DailyStockPriceRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);
        _splitRepo = new StockSplitRepository(_dbContext);

        _yahooClient = Substitute.For<IYahooFinanceClient>();
        _errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        _workerOptions = new WorkerOptions();
        _sharesProvider = Substitute.For<ISharesOutstandingProvider>();

        // The service resolves DailyStockPriceRepository from scoped DI.
        // TickerMapService resolves CommonStockRepository from scoped DI.
        // The split-reconciliation pass (#2879) resolves SplitPriceReconciliationManager
        // and the per-ticker split capture resolves StockSplitCaptureManager.
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DailyStockPriceRepository), _priceRepo),
            (typeof(CommonStockRepository), _stockRepo),
            (typeof(ISharesOutstandingProvider), _sharesProvider),
            (
                typeof(SplitPriceReconciliationManager),
                new SplitPriceReconciliationManager(_splitRepo)
            ),
            (typeof(StockSplitCaptureManager), new StockSplitCaptureManager(_splitRepo))
        );

        var tickerMapService = new TickerMapService(scopeFactory);

        _service = new YahooPriceImportService(
            scopeFactory,
            Substitute.For<ILogger<YahooPriceImportService>>(),
            _yahooClient,
            tickerMapService,
            _errorReporter,
            Options.Create(_workerOptions)
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private CommonStock CreateStock(string ticker, string name)
    {
        return new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = $"CIK-{ticker}",
        };
    }

    private async Task SeedStocks(params CommonStock[] stocks)
    {
        _stockRepo.AddRange(stocks);
        await _stockRepo.SaveChanges();
    }

    private async Task SeedPrices(params DailyStockPrice[] prices)
    {
        _priceRepo.AddRange(prices);
        await _priceRepo.SaveChanges();
    }

    private DailyStockPrice CreatePrice(CommonStock stock, DateOnly date, decimal close = 150m)
    {
        return new DailyStockPrice
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            Date = date,
            Open = close - 2m,
            High = close + 2m,
            Low = close - 3m,
            Close = close,
            AdjustedClose = close,
            Volume = 1_000_000,
        };
    }

    private static List<HistoricalPrice> CreateHistoricalPrices(
        params (DateOnly date, decimal close)[] entries
    )
    {
        return entries
            .Select(e => new HistoricalPrice
            {
                Date = e.date,
                Open = e.close - 1m,
                High = e.close + 1m,
                Low = e.close - 2m,
                Close = e.close,
                AdjustedClose = e.close,
                Volume = 500_000,
            })
            .ToList();
    }

    // Import fetches prices AND split events through the single chart call (#4049);
    // most facts only exercise the price leg, so default the splits to empty.
    private static YahooChartData CreateChartData(params (DateOnly date, decimal close)[] entries)
    {
        return new YahooChartData { Prices = CreateHistoricalPrices(entries) };
    }

    // ── Empty ticker map ──────────────────────────────────────────────

    [Fact]
    public async Task Import_NoStocksExist_InsertsNothing()
    {
        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().BeEmpty();
        await _yahooClient
            .DidNotReceive()
            .GetChart(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    // ── Creates new price records ─────────────────────────────────────

    [Fact]
    public async Task Import_NewStock_FetchesAndInsertsPrices()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var chartData = CreateChartData(
            (new DateOnly(2026, 3, 25), 180m),
            (new DateOnly(2026, 3, 26), 182m),
            (new DateOnly(2026, 3, 27), 185m)
        );

        _yahooClient.GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>()).Returns(chartData);

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(3);
        prices.Should().AllSatisfy(p => p.CommonStockId.Should().Be(apple.Id));
        prices.Select(p => p.Close).Should().BeEquivalentTo([180m, 182m, 185m]);
    }

    [Fact]
    public async Task Import_MultipleStocks_FetchesAndInsertsPricesForEach()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        await SeedStocks(apple, msft);

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateChartData((new DateOnly(2026, 3, 25), 180m)));

        _yahooClient
            .GetChart("MSFT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateChartData((new DateOnly(2026, 3, 25), 400m)));

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(2);
        prices.Should().Contain(p => p.CommonStockId == apple.Id && p.Close == 180m);
        prices.Should().Contain(p => p.CommonStockId == msft.Id && p.Close == 400m);
    }

    [Fact]
    public async Task Import_MapsAllPriceFieldsCorrectly()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var date = new DateOnly(2026, 3, 25);
        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(
                new YahooChartData
                {
                    Prices =
                    [
                        new HistoricalPrice
                        {
                            Date = date,
                            Open = 178m,
                            High = 186m,
                            Low = 176m,
                            Close = 184m,
                            AdjustedClose = 183m,
                            Volume = 42_000_000,
                        },
                    ],
                }
            );

        await _service.Import(CancellationToken.None);

        var price = _priceRepo.GetAll().Single();
        price.CommonStockId.Should().Be(apple.Id);
        price.Date.Should().Be(date);
        price.Open.Should().Be(178m);
        price.High.Should().Be(186m);
        price.Low.Should().Be(176m);
        price.Close.Should().Be(184m);
        price.AdjustedClose.Should().Be(183m);
        price.Volume.Should().Be(42_000_000);
    }

    // ── Split capture (#4049) ─────────────────────────────────────────

    [Fact]
    public async Task Import_ChartReturnsSplitEvents_CapturesThemAsStockSplits()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(
                new YahooChartData
                {
                    Prices = CreateHistoricalPrices((new DateOnly(2026, 3, 25), 180m)),
                    Splits =
                    [
                        new StockSplitEvent
                        {
                            Date = new DateOnly(2026, 3, 24),
                            Numerator = 4m,
                            Denominator = 1m,
                        },
                    ],
                }
            );

        await _service.Import(CancellationToken.None);

        // Prices still land, and the split event from the same chart payload is
        // persisted as an unreconciled StockSplit (Yahoo-sourced).
        _priceRepo.GetAll().Should().ContainSingle();
        var split = _splitRepo.GetAll().Should().ContainSingle().Which;
        split.CommonStockId.Should().Be(apple.Id);
        split.EffectiveDate.Should().Be(new DateOnly(2026, 3, 24));
        split.Numerator.Should().Be(4m);
        split.Denominator.Should().Be(1m);
        split.Source.Should().Be(StockSplitSource.Yahoo);
        split.PriceAdjustmentAppliedTime.Should().BeNull();
    }

    // ── Skips stocks with existing recent data ────────────────────────

    [Fact]
    public async Task Import_StockWithPricesUpToToday_SkipsWithoutCallingApi()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Seed a price for today so startDate >= today triggers early return
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedPrices(CreatePrice(apple, today, 180m));

        await _service.Import(CancellationToken.None);

        await _yahooClient
            .DidNotReceive()
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task Import_StockWithRecentPrices_FetchesOnlyFromDayAfterLatest()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var existingDate = new DateOnly(2026, 3, 20);
        await SeedPrices(CreatePrice(apple, existingDate, 175m));

        var expectedStartDate = existingDate.AddDays(1); // 2026-03-21
        _yahooClient
            .GetChart("AAPL", expectedStartDate, Arg.Any<DateOnly>())
            .Returns(CreateChartData((new DateOnly(2026, 3, 21), 178m)));

        await _service.Import(CancellationToken.None);

        await _yahooClient.Received(1).GetChart("AAPL", expectedStartDate, Arg.Any<DateOnly>());
    }

    // ── Deduplication of existing dates ───────────────────────────────

    [Fact]
    public async Task Import_ApiReturnsDuplicateDates_OnlyInsertsNewDates()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var existingDate = new DateOnly(2026, 3, 20);
        await SeedPrices(CreatePrice(apple, existingDate, 175m));

        // API returns both the existing date and a new date
        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateChartData((existingDate, 175m), (new DateOnly(2026, 3, 21), 178m)));

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(2); // 1 existing + 1 new
        prices.Should().ContainSingle(p => p.Date == new DateOnly(2026, 3, 21));
    }

    [Fact]
    public async Task Import_AllReturnedDatesAlreadyExist_InsertsNothing()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var date1 = new DateOnly(2026, 3, 20);
        var date2 = new DateOnly(2026, 3, 21);
        await SeedPrices(CreatePrice(apple, date1, 175m), CreatePrice(apple, date2, 178m));

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateChartData((date1, 175m), (date2, 178m)));

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(2); // no new records inserted
    }

    // ── API returns empty ─────────────────────────────────────────────

    [Fact]
    public async Task Import_ApiReturnsEmptyList_InsertsNothing()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().BeEmpty();
    }

    // ── Error handling ────────────────────────────────────────────────

    [Fact]
    public async Task Import_HttpRequestException_SkipsTickerAndContinues()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        await SeedStocks(apple, msft);

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Throws(new HttpRequestException("Network error"));

        _yahooClient
            .GetChart("MSFT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateChartData((new DateOnly(2026, 3, 25), 400m)));

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().ContainSingle();
        prices[0].CommonStockId.Should().Be(msft.Id);
    }

    [Fact]
    public async Task Import_HttpRequestException_DoesNotReportToErrorReporter()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Throws(new HttpRequestException("Timeout"));

        await _service.Import(CancellationToken.None);

        await _errorReporter
            .DidNotReceive()
            .Report(
                Arg.Any<ErrorSource>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
    }

    [Fact]
    public async Task Import_GenericException_ReportsToErrorReporterAndContinues()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        await SeedStocks(apple, msft);

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Throws(new InvalidOperationException("Unexpected error"));

        _yahooClient
            .GetChart("MSFT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateChartData((new DateOnly(2026, 3, 25), 400m)));

        await _service.Import(CancellationToken.None);

        // MSFT prices should still be inserted despite AAPL failure
        var prices = _priceRepo.GetAll().ToList();
        prices.Should().ContainSingle();
        prices[0].CommonStockId.Should().Be(msft.Id);

        // Error reporter should have been called for the AAPL failure
        await _errorReporter
            .Received(1)
            .Report(
                Arg.Any<ErrorSource>(),
                Arg.Is<string>(s => s.Contains("AAPL")),
                Arg.Any<string>(),
                Arg.Any<string>()
            );
    }

    // ── Cancellation ──────────────────────────────────────────────────

    [Fact]
    public async Task Import_CancellationRequested_ThrowsOperationCancelled()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _service.Import(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── MinSyncDate configuration ─────────────────────────────────────

    [Fact]
    public async Task Import_WithMinSyncDateConfigured_UsesItAsStartDate()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2025, 6, 1);

        var expectedStart = new DateOnly(2025, 6, 1);
        _yahooClient
            .GetChart("AAPL", expectedStart, Arg.Any<DateOnly>())
            .Returns(CreateChartData((new DateOnly(2025, 6, 2), 170m)));

        await _service.Import(CancellationToken.None);

        await _yahooClient.Received(1).GetChart("AAPL", expectedStart, Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task Import_WithoutMinSyncDate_DefaultsTo2020()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = null;

        var expectedStart = new DateOnly(2020, 1, 1);
        _yahooClient
            .GetChart("AAPL", expectedStart, Arg.Any<DateOnly>())
            .Returns(CreateChartData((new DateOnly(2020, 1, 2), 75m)));

        await _service.Import(CancellationToken.None);

        await _yahooClient.Received(1).GetChart("AAPL", expectedStart, Arg.Any<DateOnly>());
    }

    // ── Batch insertion ───────────────────────────────────────────────

    [Fact]
    public async Task Import_LargePriceSet_InsertsAllRecords()
    {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Generate more than one batch worth (InsertBatchSize = 500)
        var startDate = new DateOnly(2024, 1, 1);
        var historicalPrices = Enumerable
            .Range(0, 600)
            .Select(i => new HistoricalPrice
            {
                Date = startDate.AddDays(i),
                Open = 100m + i,
                High = 102m + i,
                Low = 99m + i,
                Close = 101m + i,
                AdjustedClose = 101m + i,
                Volume = 1_000_000,
            })
            .ToList();

        _yahooClient
            .GetChart("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData { Prices = historicalPrices });

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(600);
    }

    // ── Overflow guard + key-statistics update (zero-hit branches) ─────

    [Fact]
    public async Task Import_PriceExceedsNumericLimit_SkipsOverflowRowButInsertsValidOnes()
    {
        var stock = CreateStock("OVR", "Overflow Co.");
        await SeedStocks(stock);

        var valid = new HistoricalPrice
        {
            Date = new DateOnly(2024, 1, 2),
            Open = 100m,
            High = 102m,
            Low = 99m,
            Close = 101m,
            AdjustedClose = 101m,
            Volume = 1_000_000,
        };
        var overflow = new HistoricalPrice
        {
            Date = new DateOnly(2024, 1, 3),
            // Above the numeric(18,4) ceiling → HasOverflowPrice true.
            Open = 100_000_000_000_000m,
            High = 100_000_000_000_000m,
            Low = 100_000_000_000_000m,
            Close = 100_000_000_000_000m,
            AdjustedClose = 100_000_000_000_000m,
            Volume = 1,
        };
        _yahooClient
            .GetChart("OVR", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData { Prices = [valid, overflow] });

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().ContainSingle("the overflow row must be skipped, the valid one kept");
        prices[0].Date.Should().Be(new DateOnly(2024, 1, 2));
    }

    [Fact]
    public async Task Import_KeyStatisticsSharesDiffer_UpdatesStockSharesOutstanding()
    {
        var stock = CreateStock("KST", "KeyStat Co.");
        await SeedStocks(stock);

        // No prices → ImportTicker returns early; SyncKeyStatistics still runs.
        _yahooClient
            .GetChart("KST", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
        _yahooClient
            .GetKeyStatistics("KST")
            .Returns(new KeyStatistics { SharesOutstanding = 5_000_000 });

        await _service.Import(CancellationToken.None);

        var updated = _stockRepo.GetAll().Single(s => s.Ticker == "KST");
        updated.SharesOutStanding.Should().Be(5_000_000);
    }

    [Fact]
    public async Task Import_KeyStatisticsMarketCapDiffers_UpdatesStockMarketCapitalization()
    {
        var stock = CreateStock("MKT", "MarketCap Co.");
        await SeedStocks(stock);

        _yahooClient
            .GetChart("MKT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
        _yahooClient
            .GetKeyStatistics("MKT")
            .Returns(
                new KeyStatistics
                {
                    SharesOutstanding = 10_000_000,
                    MarketCapitalization = 1_500_000_000d,
                }
            );

        await _service.Import(CancellationToken.None);

        var updated = _stockRepo.GetAll().Single(s => s.Ticker == "MKT");
        updated.MarketCapitalization.Should().Be(1_500_000_000d);
        updated.SharesOutStanding.Should().Be(10_000_000);
    }

    [Fact]
    public async Task Import_KeyStatisticsMarketCapZero_LeavesExistingMarketCapUntouched()
    {
        // Yahoo returns 0 when market cap is unknown — the worker must not overwrite a
        // previously-known value with that sentinel.
        var stock = CreateStock("MKT0", "MarketCap Zero Co.");
        stock.MarketCapitalization = 9_876_543_210d;
        await SeedStocks(stock);

        _yahooClient
            .GetChart("MKT0", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
        _yahooClient
            .GetKeyStatistics("MKT0")
            .Returns(new KeyStatistics { SharesOutstanding = 42, MarketCapitalization = 0 });

        await _service.Import(CancellationToken.None);

        var updated = _stockRepo.GetAll().Single(s => s.Ticker == "MKT0");
        updated.MarketCapitalization.Should().Be(9_876_543_210d);
        updated.SharesOutStanding.Should().Be(42);
    }

    [Fact]
    public async Task Import_KeyStatisticsSharesZero_LeavesExistingSharesUntouched()
    {
        // Mirror of Import_KeyStatisticsMarketCapZero — the "never overwrite a known value
        // with 0" contract has to hold on both fields. A regression that dropped the
        // `stats.SharesOutstanding != 0` guard would let Yahoo's "unknown" sentinel blank
        // an existing SharesOutStanding; the MarketCap pin alone wouldn't catch it.
        var stock = CreateStock("SHS0", "Shares Zero Co.");
        stock.SharesOutStanding = 12_345_678;
        await SeedStocks(stock);

        _yahooClient
            .GetChart("SHS0", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
        _yahooClient
            .GetKeyStatistics("SHS0")
            .Returns(
                new KeyStatistics { SharesOutstanding = 0, MarketCapitalization = 2_222_222d }
            );

        await _service.Import(CancellationToken.None);

        var updated = _stockRepo.GetAll().Single(s => s.Ticker == "SHS0");
        updated.SharesOutStanding.Should().Be(12_345_678);
        updated.MarketCapitalization.Should().Be(2_222_222d);
    }

    [Fact]
    public async Task Import_ForeignPrivateIssuer_StoresYahooAdrMarketCapAndSharesWithoutReconciling()
    {
        // Latam Airlines (ADR): Yahoo returns the correct, self-consistent ADR pair — $16.66B market
        // cap on 287M ADR shares. EDGAR reports the issuer's 574B *ordinary* shares from its 20-F,
        // a different unit. Reconciling onto that ordinary base would inflate market cap ~2000x to
        // ~$33T, so a foreign private issuer must keep Yahoo's ADR figures verbatim.
        var stock = CreateStock("LTM", "Latam Airlines Group S.A.");
        await SeedStocks(stock);

        _sharesProvider.GetCurrentSharesOutstanding(stock).Returns(574_215_983_709L);
        _sharesProvider.IsForeignPrivateIssuer(stock).Returns(true);

        _yahooClient
            .GetChart("LTM", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
        _yahooClient
            .GetKeyStatistics("LTM")
            .Returns(
                new KeyStatistics
                {
                    SharesOutstanding = 287_107_992,
                    MarketCapitalization = 16_657_130_496d,
                }
            );

        await _service.Import(CancellationToken.None);

        var updated = _stockRepo.GetAll().Single(s => s.Ticker == "LTM");
        updated.MarketCapitalization.Should().Be(16_657_130_496d);
        updated.SharesOutStanding.Should().Be(287_107_992);
    }

    [Fact]
    public async Task Import_DomesticIssuer_ReconcilesMarketCapOntoEdgarShareBase()
    {
        // A domestic multi-class issuer keeps the reconciliation: EDGAR's consolidated count (10M)
        // is twice Yahoo's single-class count (5M), so Yahoo's $1B market cap rescales to $2B to
        // stay consistent with the authoritative share base.
        var stock = CreateStock("DOM", "Domestic Multi-Class Co.");
        await SeedStocks(stock);

        _sharesProvider.GetCurrentSharesOutstanding(stock).Returns(10_000_000L);
        _sharesProvider.IsForeignPrivateIssuer(stock).Returns(false);

        _yahooClient
            .GetChart("DOM", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
        _yahooClient
            .GetKeyStatistics("DOM")
            .Returns(
                new KeyStatistics
                {
                    SharesOutstanding = 5_000_000,
                    MarketCapitalization = 1_000_000_000d,
                }
            );

        await _service.Import(CancellationToken.None);

        var updated = _stockRepo.GetAll().Single(s => s.Ticker == "DOM");
        updated.MarketCapitalization.Should().Be(2_000_000_000d);
    }

    [Fact]
    public async Task Import_KeyStatisticsBothZero_LeavesStockUntouched()
    {
        var stock = CreateStock("ZERO", "All Zero Co.");
        stock.SharesOutStanding = 100;
        stock.MarketCapitalization = 200d;
        await SeedStocks(stock);

        _yahooClient
            .GetChart("ZERO", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new YahooChartData());
        _yahooClient
            .GetKeyStatistics("ZERO")
            .Returns(new KeyStatistics { SharesOutstanding = 0, MarketCapitalization = 0 });

        await _service.Import(CancellationToken.None);

        var updated = _stockRepo.GetAll().Single(s => s.Ticker == "ZERO");
        updated.SharesOutStanding.Should().Be(100);
        updated.MarketCapitalization.Should().Be(200d);
    }
}
