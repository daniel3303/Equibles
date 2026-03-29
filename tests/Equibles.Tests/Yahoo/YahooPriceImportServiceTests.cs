using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Yahoo.Contracts;
using Equibles.Integrations.Yahoo.Models;
using Equibles.Tests.Helpers;
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

namespace Equibles.Tests.Yahoo;

public class YahooPriceImportServiceTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly DailyStockPriceRepository _priceRepo;
    private readonly CommonStockRepository _stockRepo;
    private readonly IYahooFinanceClient _yahooClient;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;
    private readonly YahooPriceImportService _service;

    public YahooPriceImportServiceTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new YahooModuleConfiguration()
        );
        _priceRepo = new DailyStockPriceRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);

        _yahooClient = Substitute.For<IYahooFinanceClient>();
        _errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        _workerOptions = new WorkerOptions();

        // The service resolves DailyStockPriceRepository from scoped DI.
        // TickerMapService resolves CommonStockRepository from scoped DI.
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DailyStockPriceRepository), _priceRepo),
            (typeof(CommonStockRepository), _stockRepo)
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

    public void Dispose() {
        _dbContext.Dispose();
    }

    private CommonStock CreateStock(string ticker, string name) {
        return new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = $"CIK-{ticker}",
        };
    }

    private async Task SeedStocks(params CommonStock[] stocks) {
        _stockRepo.AddRange(stocks);
        await _stockRepo.SaveChanges();
    }

    private async Task SeedPrices(params DailyStockPrice[] prices) {
        _priceRepo.AddRange(prices);
        await _priceRepo.SaveChanges();
    }

    private DailyStockPrice CreatePrice(CommonStock stock, DateOnly date, decimal close = 150m) {
        return new DailyStockPrice {
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

    private static List<HistoricalPrice> CreateHistoricalPrices(params (DateOnly date, decimal close)[] entries) {
        return entries.Select(e => new HistoricalPrice {
            Date = e.date,
            Open = e.close - 1m,
            High = e.close + 1m,
            Low = e.close - 2m,
            Close = e.close,
            AdjustedClose = e.close,
            Volume = 500_000,
        }).ToList();
    }

    // ── Empty ticker map ──────────────────────────────────────────────

    [Fact]
    public async Task Import_NoStocksExist_InsertsNothing() {
        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().BeEmpty();
        await _yahooClient.DidNotReceive().GetHistoricalPrices(
            Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    // ── Creates new price records ─────────────────────────────────────

    [Fact]
    public async Task Import_NewStock_FetchesAndInsertsPrices() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var historicalPrices = CreateHistoricalPrices(
            (new DateOnly(2026, 3, 25), 180m),
            (new DateOnly(2026, 3, 26), 182m),
            (new DateOnly(2026, 3, 27), 185m)
        );

        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(historicalPrices);

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(3);
        prices.Should().AllSatisfy(p => p.CommonStockId.Should().Be(apple.Id));
        prices.Select(p => p.Close).Should().BeEquivalentTo([180m, 182m, 185m]);
    }

    [Fact]
    public async Task Import_MultipleStocks_FetchesAndInsertsPricesForEach() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        await SeedStocks(apple, msft);

        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices((new DateOnly(2026, 3, 25), 180m)));

        _yahooClient.GetHistoricalPrices("MSFT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices((new DateOnly(2026, 3, 25), 400m)));

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(2);
        prices.Should().Contain(p => p.CommonStockId == apple.Id && p.Close == 180m);
        prices.Should().Contain(p => p.CommonStockId == msft.Id && p.Close == 400m);
    }

    [Fact]
    public async Task Import_MapsAllPriceFieldsCorrectly() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var date = new DateOnly(2026, 3, 25);
        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns([new HistoricalPrice {
                Date = date, Open = 178m, High = 186m, Low = 176m,
                Close = 184m, AdjustedClose = 183m, Volume = 42_000_000,
            }]);

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

    // ── Skips stocks with existing recent data ────────────────────────

    [Fact]
    public async Task Import_StockWithPricesUpToToday_SkipsWithoutCallingApi() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Seed a price for today so startDate >= today triggers early return
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedPrices(CreatePrice(apple, today, 180m));

        await _service.Import(CancellationToken.None);

        await _yahooClient.DidNotReceive().GetHistoricalPrices(
            "AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task Import_StockWithRecentPrices_FetchesOnlyFromDayAfterLatest() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var existingDate = new DateOnly(2026, 3, 20);
        await SeedPrices(CreatePrice(apple, existingDate, 175m));

        var expectedStartDate = existingDate.AddDays(1); // 2026-03-21
        _yahooClient.GetHistoricalPrices("AAPL", expectedStartDate, Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices((new DateOnly(2026, 3, 21), 178m)));

        await _service.Import(CancellationToken.None);

        await _yahooClient.Received(1).GetHistoricalPrices(
            "AAPL", expectedStartDate, Arg.Any<DateOnly>());
    }

    // ── Deduplication of existing dates ───────────────────────────────

    [Fact]
    public async Task Import_ApiReturnsDuplicateDates_OnlyInsertsNewDates() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var existingDate = new DateOnly(2026, 3, 20);
        await SeedPrices(CreatePrice(apple, existingDate, 175m));

        // API returns both the existing date and a new date
        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices(
                (existingDate, 175m),
                (new DateOnly(2026, 3, 21), 178m)
            ));

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(2); // 1 existing + 1 new
        prices.Should().ContainSingle(p => p.Date == new DateOnly(2026, 3, 21));
    }

    [Fact]
    public async Task Import_AllReturnedDatesAlreadyExist_InsertsNothing() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var date1 = new DateOnly(2026, 3, 20);
        var date2 = new DateOnly(2026, 3, 21);
        await SeedPrices(
            CreatePrice(apple, date1, 175m),
            CreatePrice(apple, date2, 178m)
        );

        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices((date1, 175m), (date2, 178m)));

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(2); // no new records inserted
    }

    // ── API returns empty ─────────────────────────────────────────────

    [Fact]
    public async Task Import_ApiReturnsEmptyList_InsertsNothing() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new List<HistoricalPrice>());

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().BeEmpty();
    }

    // ── Error handling ────────────────────────────────────────────────

    [Fact]
    public async Task Import_HttpRequestException_SkipsTickerAndContinues() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        await SeedStocks(apple, msft);

        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Throws(new HttpRequestException("Network error"));

        _yahooClient.GetHistoricalPrices("MSFT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices((new DateOnly(2026, 3, 25), 400m)));

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().ContainSingle();
        prices[0].CommonStockId.Should().Be(msft.Id);
    }

    [Fact]
    public async Task Import_HttpRequestException_DoesNotReportToErrorReporter() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Throws(new HttpRequestException("Timeout"));

        await _service.Import(CancellationToken.None);

        await _errorReporter.DidNotReceive().Report(
            Arg.Any<ErrorSource>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Import_GenericException_ReportsToErrorReporterAndContinues() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        await SeedStocks(apple, msft);

        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Throws(new InvalidOperationException("Unexpected error"));

        _yahooClient.GetHistoricalPrices("MSFT", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices((new DateOnly(2026, 3, 25), 400m)));

        await _service.Import(CancellationToken.None);

        // MSFT prices should still be inserted despite AAPL failure
        var prices = _priceRepo.GetAll().ToList();
        prices.Should().ContainSingle();
        prices[0].CommonStockId.Should().Be(msft.Id);

        // Error reporter should have been called for the AAPL failure
        await _errorReporter.Received(1).Report(
            Arg.Any<ErrorSource>(),
            Arg.Is<string>(s => s.Contains("AAPL")),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ── Cancellation ──────────────────────────────────────────────────

    [Fact]
    public async Task Import_CancellationRequested_ThrowsOperationCancelled() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _service.Import(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── MinSyncDate configuration ─────────────────────────────────────

    [Fact]
    public async Task Import_WithMinSyncDateConfigured_UsesItAsStartDate() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2025, 6, 1);

        var expectedStart = new DateOnly(2025, 6, 1);
        _yahooClient.GetHistoricalPrices("AAPL", expectedStart, Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices((new DateOnly(2025, 6, 2), 170m)));

        await _service.Import(CancellationToken.None);

        await _yahooClient.Received(1).GetHistoricalPrices(
            "AAPL", expectedStart, Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task Import_WithoutMinSyncDate_DefaultsTo2020() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = null;

        var expectedStart = new DateOnly(2020, 1, 1);
        _yahooClient.GetHistoricalPrices("AAPL", expectedStart, Arg.Any<DateOnly>())
            .Returns(CreateHistoricalPrices((new DateOnly(2020, 1, 2), 75m)));

        await _service.Import(CancellationToken.None);

        await _yahooClient.Received(1).GetHistoricalPrices(
            "AAPL", expectedStart, Arg.Any<DateOnly>());
    }

    // ── Batch insertion ───────────────────────────────────────────────

    [Fact]
    public async Task Import_LargePriceSet_InsertsAllRecords() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Generate more than one batch worth (InsertBatchSize = 500)
        var startDate = new DateOnly(2024, 1, 1);
        var historicalPrices = Enumerable.Range(0, 600)
            .Select(i => new HistoricalPrice {
                Date = startDate.AddDays(i),
                Open = 100m + i, High = 102m + i, Low = 99m + i,
                Close = 101m + i, AdjustedClose = 101m + i, Volume = 1_000_000,
            })
            .ToList();

        _yahooClient.GetHistoricalPrices("AAPL", Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(historicalPrices);

        await _service.Import(CancellationToken.None);

        var prices = _priceRepo.GetAll().ToList();
        prices.Should().HaveCount(600);
    }
}
