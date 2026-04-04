using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.Tests.Helpers;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.Tests.Finra;

public class ShortVolumeImportServiceTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly DailyShortVolumeRepository _volumeRepo;
    private readonly CommonStockRepository _stockRepo;
    private readonly IFinraClient _finraClient;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;
    private readonly ShortVolumeImportService _service;

    public ShortVolumeImportServiceTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _volumeRepo = new DailyShortVolumeRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);

        _finraClient = Substitute.For<IFinraClient>();
        _errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        _workerOptions = new WorkerOptions();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DailyShortVolumeRepository), _volumeRepo),
            (typeof(CommonStockRepository), _stockRepo)
        );

        var tickerMapService = new TickerMapService(scopeFactory);

        _service = new ShortVolumeImportService(
            scopeFactory,
            Substitute.For<ILogger<ShortVolumeImportService>>(),
            _finraClient,
            tickerMapService,
            _errorReporter,
            Options.Create(_workerOptions)
        );
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────

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

    private async Task SeedVolume(CommonStock stock, DateOnly date, long shortVolume = 1_000_000) {
        _dbContext.Set<DailyShortVolume>().Add(new DailyShortVolume {
            CommonStockId = stock.Id,
            Date = date,
            ShortVolume = shortVolume,
            ShortExemptVolume = 5_000,
            TotalVolume = 5_000_000,
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    private static List<ShortVolumeRecord> CreateVolumeRecords(
        params (string symbol, long? shortVolume, long? shortExemptVolume, long? totalVolume)[] entries
    ) {
        return entries.Select(e => new ShortVolumeRecord {
            Symbol = e.symbol,
            ShortVolume = e.shortVolume,
            ShortExemptVolume = e.shortExemptVolume,
            TotalVolume = e.totalVolume,
        }).ToList();
    }

    // ── Import creates new records ───────────────────────────────────

    [Fact]
    public async Task Import_NewRecords_FetchesAndInserts() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Set MinSyncDate to a known weekday (Wednesday)
        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        var records = CreateVolumeRecords(
            ("AAPL", 500_000, 10_000, 2_000_000)
        );
        _finraClient.GetDailyShortVolume(new DateOnly(2026, 3, 25)).Returns(records);
        // Return empty for subsequent dates up to today
        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > new DateOnly(2026, 3, 25)))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        var volumes = _volumeRepo.GetAll().ToList();
        volumes.Should().ContainSingle();
        volumes[0].CommonStockId.Should().Be(apple.Id);
        volumes[0].Date.Should().Be(new DateOnly(2026, 3, 25));
        volumes[0].ShortVolume.Should().Be(500_000);
        volumes[0].ShortExemptVolume.Should().Be(10_000);
        volumes[0].TotalVolume.Should().Be(2_000_000);
    }

    [Fact]
    public async Task Import_MultipleStocks_InsertsRecordsForEach() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        await SeedStocks(apple, msft);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        var records = CreateVolumeRecords(
            ("AAPL", 500_000, 10_000, 2_000_000),
            ("MSFT", 300_000, 5_000, 1_500_000)
        );
        _finraClient.GetDailyShortVolume(new DateOnly(2026, 3, 25)).Returns(records);
        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > new DateOnly(2026, 3, 25)))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        var volumes = _volumeRepo.GetAll().ToList();
        volumes.Should().HaveCount(2);
        volumes.Should().Contain(v => v.CommonStockId == apple.Id && v.ShortVolume == 500_000);
        volumes.Should().Contain(v => v.CommonStockId == msft.Id && v.ShortVolume == 300_000);
    }

    // ── Aggregates across markets ────────────────────────────────────

    [Fact]
    public async Task Import_MultipleMarketsForSameSymbol_AggregatesVolumes() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        // Same symbol from different market codes -- should aggregate
        var records = new List<ShortVolumeRecord> {
            new() { Symbol = "AAPL", ShortVolume = 200_000, ShortExemptVolume = 1_000, TotalVolume = 800_000, MarketCode = "TRF" },
            new() { Symbol = "AAPL", ShortVolume = 300_000, ShortExemptVolume = 2_000, TotalVolume = 1_200_000, MarketCode = "ADF" },
        };
        _finraClient.GetDailyShortVolume(new DateOnly(2026, 3, 25)).Returns(records);
        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > new DateOnly(2026, 3, 25)))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        var volume = _volumeRepo.GetAll().ToList();
        volume.Should().ContainSingle();
        volume[0].ShortVolume.Should().Be(500_000);
        volume[0].ShortExemptVolume.Should().Be(3_000);
        volume[0].TotalVolume.Should().Be(2_000_000);
    }

    // ── Skips duplicates ─────────────────────────────────────────────

    [Fact]
    public async Task Import_DataAlreadyUpToDate_SkipsWithoutCallingApi() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Seed a volume for today so startDate > today
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedVolume(apple, today);

        await _service.Import(CancellationToken.None);

        await _finraClient.DidNotReceive().GetDailyShortVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task Import_ExistingData_FetchesOnlyFromDayAfterLatest() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Seed data for a recent weekday
        var existingDate = new DateOnly(2026, 3, 25); // Wednesday
        await SeedVolume(apple, existingDate);

        var nextDay = existingDate.AddDays(1); // Thursday
        var records = CreateVolumeRecords(("AAPL", 100_000, 1_000, 500_000));
        _finraClient.GetDailyShortVolume(nextDay).Returns(records);
        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > nextDay))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        // Should have fetched starting from the day after the existing date, not before
        await _finraClient.DidNotReceive().GetDailyShortVolume(existingDate);
        await _finraClient.Received().GetDailyShortVolume(nextDay);
    }

    // ── Handles empty API response ───────────────────────────────────

    [Fact]
    public async Task Import_ApiReturnsEmptyList_InsertsNothing() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        _finraClient.GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        var volumes = _volumeRepo.GetAll().ToList();
        volumes.Should().BeEmpty();
    }

    // ── Skips unknown tickers ────────────────────────────────────────

    [Fact]
    public async Task Import_UnknownSymbolInApiResponse_SkipsRecord() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        var records = CreateVolumeRecords(
            ("AAPL", 500_000, 10_000, 2_000_000),
            ("UNKNOWN", 100_000, 1_000, 400_000)
        );
        _finraClient.GetDailyShortVolume(new DateOnly(2026, 3, 25)).Returns(records);
        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > new DateOnly(2026, 3, 25)))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        var volumes = _volumeRepo.GetAll().ToList();
        volumes.Should().ContainSingle();
        volumes[0].CommonStockId.Should().Be(apple.Id);
    }

    [Fact]
    public async Task Import_NullOrEmptySymbol_SkipsRecord() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        var records = new List<ShortVolumeRecord> {
            new() { Symbol = null, ShortVolume = 100, TotalVolume = 500 },
            new() { Symbol = "", ShortVolume = 200, TotalVolume = 600 },
            new() { Symbol = "AAPL", ShortVolume = 500_000, ShortExemptVolume = 10_000, TotalVolume = 2_000_000 },
        };
        _finraClient.GetDailyShortVolume(new DateOnly(2026, 3, 25)).Returns(records);
        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > new DateOnly(2026, 3, 25)))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        var volumes = _volumeRepo.GetAll().ToList();
        volumes.Should().ContainSingle();
        volumes[0].CommonStockId.Should().Be(apple.Id);
    }

    // ── Null volume fields default to zero ────────────────────────────

    [Fact]
    public async Task Import_NullVolumeFields_DefaultToZero() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        var records = CreateVolumeRecords(("AAPL", null, null, null));
        _finraClient.GetDailyShortVolume(new DateOnly(2026, 3, 25)).Returns(records);
        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > new DateOnly(2026, 3, 25)))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        var volume = _volumeRepo.GetAll().Single();
        volume.ShortVolume.Should().Be(0);
        volume.ShortExemptVolume.Should().Be(0);
        volume.TotalVolume.Should().Be(0);
    }

    // ── Skips weekends ───────────────────────────────────────────────

    [Fact]
    public async Task Import_SkipsWeekendDates_DoesNotCallApiForSaturdayOrSunday() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Friday March 27 2026 through Monday March 30 2026
        _workerOptions.MinSyncDate = new DateTime(2026, 3, 27);

        _finraClient.GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        // Saturday and Sunday should be skipped
        await _finraClient.DidNotReceive().GetDailyShortVolume(new DateOnly(2026, 3, 28));
        await _finraClient.DidNotReceive().GetDailyShortVolume(new DateOnly(2026, 3, 29));

        // Friday and Monday should be called
        await _finraClient.Received().GetDailyShortVolume(new DateOnly(2026, 3, 27));
    }

    // ── Handles API errors gracefully ────────────────────────────────

    [Fact]
    public async Task Import_HttpRequestException_SkipsDateAndContinues() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        // Wednesday and Thursday
        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        var wednesday = new DateOnly(2026, 3, 25);
        var thursday = new DateOnly(2026, 3, 26);

        _finraClient.GetDailyShortVolume(wednesday)
            .Throws(new HttpRequestException("API unavailable"));

        var records = CreateVolumeRecords(("AAPL", 500_000, 10_000, 2_000_000));
        _finraClient.GetDailyShortVolume(thursday).Returns(records);

        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > thursday))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        // Thursday data should still be inserted despite Wednesday failure
        var volumes = _volumeRepo.GetAll().ToList();
        volumes.Should().ContainSingle();
        volumes[0].Date.Should().Be(thursday);
    }

    [Fact]
    public async Task Import_HttpRequestException_DoesNotReportToErrorReporter() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        _finraClient.GetDailyShortVolume(Arg.Any<DateOnly>())
            .Throws(new HttpRequestException("Timeout"));

        await _service.Import(CancellationToken.None);

        await _errorReporter.DidNotReceive().Report(
            Arg.Any<ErrorSource>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Import_GenericException_ReportsToErrorReporterAndContinues() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var wednesday = new DateOnly(2026, 3, 25);
        var thursday = new DateOnly(2026, 3, 26);
        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        _finraClient.GetDailyShortVolume(wednesday)
            .Throws(new InvalidOperationException("Unexpected error"));

        _finraClient.GetDailyShortVolume(thursday)
            .Returns(CreateVolumeRecords(("AAPL", 100_000, 1_000, 500_000)));

        _finraClient.GetDailyShortVolume(Arg.Is<DateOnly>(d => d > thursday))
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        // Thursday data should still be inserted
        var volumes = _volumeRepo.GetAll().ToList();
        volumes.Should().ContainSingle();
        volumes[0].Date.Should().Be(thursday);

        // Error reporter should have been called for Wednesday failure
        await _errorReporter.Received(1).Report(
            ErrorSource.FinraScraper,
            "ShortVolume.ImportDate",
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // ── Cancellation ─────────────────────────────────────────────────

    [Fact]
    public async Task Import_CancellationRequested_ThrowsOperationCancelled() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _service.Import(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── MinSyncDate configuration ────────────────────────────────────

    [Fact]
    public async Task Import_WithoutMinSyncDate_DefaultsTo2020() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = null;

        _finraClient.GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(new List<ShortVolumeRecord>());

        await _service.Import(CancellationToken.None);

        // Should have started from 2020-01-01 (a Wednesday)
        await _finraClient.Received().GetDailyShortVolume(new DateOnly(2020, 1, 1));
    }

    // ── No stocks exist ──────────────────────────────────────────────

    [Fact]
    public async Task Import_NoStocksExist_InsertsNothing() {
        _workerOptions.MinSyncDate = new DateTime(2026, 3, 25);

        _finraClient.GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(CreateVolumeRecords(("AAPL", 500_000, 10_000, 2_000_000)));

        await _service.Import(CancellationToken.None);

        var volumes = _volumeRepo.GetAll().ToList();
        volumes.Should().BeEmpty();
    }
}

public class ShortInterestImportServiceTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly ShortInterestRepository _interestRepo;
    private readonly CommonStockRepository _stockRepo;
    private readonly IFinraClient _finraClient;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;
    private readonly ShortInterestImportService _service;

    public ShortInterestImportServiceTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _interestRepo = new ShortInterestRepository(_dbContext);
        _stockRepo = new CommonStockRepository(_dbContext);

        _finraClient = Substitute.For<IFinraClient>();
        _errorReporter = Substitute.For<ErrorReporter>(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        _workerOptions = new WorkerOptions();

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ShortInterestRepository), _interestRepo),
            (typeof(CommonStockRepository), _stockRepo)
        );

        var tickerMapService = new TickerMapService(scopeFactory);

        _service = new ShortInterestImportService(
            scopeFactory,
            Substitute.For<ILogger<ShortInterestImportService>>(),
            _finraClient,
            tickerMapService,
            _errorReporter,
            Options.Create(_workerOptions)
        );
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── Helpers ────────────────────────────────────────────────────────

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

    private async Task SeedInterest(CommonStock stock, DateOnly settlementDate, long currentShort = 10_000_000) {
        _dbContext.Set<ShortInterest>().Add(new ShortInterest {
            CommonStockId = stock.Id,
            SettlementDate = settlementDate,
            CurrentShortPosition = currentShort,
            PreviousShortPosition = 9_000_000,
            ChangeInShortPosition = 1_000_000,
        });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    private static List<ShortInterestRecord> CreateInterestRecords(
        params (string symbol, long? current, long? previous, long? change, long? avgVolume, decimal? daysToCover)[] entries
    ) {
        return entries.Select(e => new ShortInterestRecord {
            Symbol = e.symbol,
            CurrentShortPosition = e.current,
            PreviousShortPosition = e.previous,
            ChangeInShortPosition = e.change,
            AverageDailyVolume = e.avgVolume,
            DaysToCover = e.daysToCover,
        }).ToList();
    }

    // ── Import creates new records ───────────────────────────────────

    [Fact]
    public async Task Import_NewRecords_FetchesAndInserts() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        var records = CreateInterestRecords(
            ("AAPL", 15_000_000, 14_000_000, 1_000_000, 3_000_000, 5.0m)
        );
        _finraClient.GetShortInterest(settlementDate).Returns(records);

        await _service.Import(CancellationToken.None);

        var interests = _interestRepo.GetAll().ToList();
        interests.Should().ContainSingle();
        interests[0].CommonStockId.Should().Be(apple.Id);
        interests[0].SettlementDate.Should().Be(settlementDate);
        interests[0].CurrentShortPosition.Should().Be(15_000_000);
        interests[0].PreviousShortPosition.Should().Be(14_000_000);
        interests[0].ChangeInShortPosition.Should().Be(1_000_000);
        interests[0].AverageDailyVolume.Should().Be(3_000_000);
        interests[0].DaysToCover.Should().Be(5.0m);
    }

    [Fact]
    public async Task Import_MultipleStocks_InsertsRecordsForEach() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        var msft = CreateStock("MSFT", "Microsoft Corp.");
        await SeedStocks(apple, msft);

        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        var records = CreateInterestRecords(
            ("AAPL", 15_000_000, 14_000_000, 1_000_000, 3_000_000, 5.0m),
            ("MSFT", 8_000_000, 7_500_000, 500_000, 2_000_000, 4.0m)
        );
        _finraClient.GetShortInterest(settlementDate).Returns(records);

        await _service.Import(CancellationToken.None);

        var interests = _interestRepo.GetAll().ToList();
        interests.Should().HaveCount(2);
        interests.Should().Contain(i => i.CommonStockId == apple.Id && i.CurrentShortPosition == 15_000_000);
        interests.Should().Contain(i => i.CommonStockId == msft.Id && i.CurrentShortPosition == 8_000_000);
    }

    [Fact]
    public async Task Import_MultipleSettlementDates_ImportsAll() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { date1, date2 });

        _finraClient.GetShortInterest(date1)
            .Returns(CreateInterestRecords(("AAPL", 10_000_000, 9_000_000, 1_000_000, 2_000_000, 5.0m)));
        _finraClient.GetShortInterest(date2)
            .Returns(CreateInterestRecords(("AAPL", 12_000_000, 10_000_000, 2_000_000, 2_500_000, 4.8m)));

        await _service.Import(CancellationToken.None);

        var interests = _interestRepo.GetAll().ToList();
        interests.Should().HaveCount(2);
        interests.Should().Contain(i => i.SettlementDate == date1 && i.CurrentShortPosition == 10_000_000);
        interests.Should().Contain(i => i.SettlementDate == date2 && i.CurrentShortPosition == 12_000_000);
    }

    // ── Skips duplicates ─────────────────────────────────────────────

    [Fact]
    public async Task Import_ExistingSettlementDate_OnlyImportsNewDates() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var existingDate = new DateOnly(2026, 3, 1);
        await SeedInterest(apple, existingDate);

        var newDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDatesAfter(existingDate)
            .Returns(new List<DateOnly> { newDate });

        _finraClient.GetShortInterest(newDate)
            .Returns(CreateInterestRecords(("AAPL", 12_000_000, 10_000_000, 2_000_000, null, null)));

        await _service.Import(CancellationToken.None);

        var interests = _interestRepo.GetAll().ToList();
        interests.Should().HaveCount(2); // 1 existing + 1 new
        interests.Should().Contain(i => i.SettlementDate == newDate);

        // Should not have called the API for the existing date
        await _finraClient.DidNotReceive().GetShortInterest(existingDate);
    }

    [Fact]
    public async Task Import_AllDatesAlreadyImported_SkipsWithoutCallingApi() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var existingDate = new DateOnly(2026, 3, 15);
        await SeedInterest(apple, existingDate);

        _finraClient.GetShortInterestSettlementDatesAfter(existingDate)
            .Returns(new List<DateOnly>());

        await _service.Import(CancellationToken.None);

        await _finraClient.DidNotReceive().GetShortInterest(Arg.Any<DateOnly>());
    }

    // ── Handles empty API response ───────────────────────────────────

    [Fact]
    public async Task Import_ApiReturnsEmptyRecords_InsertsNothing() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        _finraClient.GetShortInterest(settlementDate)
            .Returns(new List<ShortInterestRecord>());

        await _service.Import(CancellationToken.None);

        var interests = _interestRepo.GetAll().ToList();
        interests.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_NoSettlementDatesAvailable_InsertsNothing() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly>());

        await _service.Import(CancellationToken.None);

        await _finraClient.DidNotReceive().GetShortInterest(Arg.Any<DateOnly>());
        var interests = _interestRepo.GetAll().ToList();
        interests.Should().BeEmpty();
    }

    // ── Skips unknown tickers ────────────────────────────────────────

    [Fact]
    public async Task Import_UnknownSymbolInApiResponse_SkipsRecord() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        var records = CreateInterestRecords(
            ("AAPL", 15_000_000, 14_000_000, 1_000_000, 3_000_000, 5.0m),
            ("UNKNOWN", 1_000_000, 900_000, 100_000, 500_000, 2.0m)
        );
        _finraClient.GetShortInterest(settlementDate).Returns(records);

        await _service.Import(CancellationToken.None);

        var interests = _interestRepo.GetAll().ToList();
        interests.Should().ContainSingle();
        interests[0].CommonStockId.Should().Be(apple.Id);
    }

    [Fact]
    public async Task Import_NullOrEmptySymbol_SkipsRecord() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        var records = new List<ShortInterestRecord> {
            new() { Symbol = null, CurrentShortPosition = 100 },
            new() { Symbol = "", CurrentShortPosition = 200 },
            new() { Symbol = "AAPL", CurrentShortPosition = 15_000_000, PreviousShortPosition = 14_000_000, ChangeInShortPosition = 1_000_000 },
        };
        _finraClient.GetShortInterest(settlementDate).Returns(records);

        await _service.Import(CancellationToken.None);

        var interests = _interestRepo.GetAll().ToList();
        interests.Should().ContainSingle();
        interests[0].CommonStockId.Should().Be(apple.Id);
    }

    // ── Null fields default to zero or null ───────────────────────────

    [Fact]
    public async Task Import_NullPositionFields_DefaultToZero() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        var records = CreateInterestRecords(("AAPL", null, null, null, null, null));
        _finraClient.GetShortInterest(settlementDate).Returns(records);

        await _service.Import(CancellationToken.None);

        var interest = _interestRepo.GetAll().Single();
        interest.CurrentShortPosition.Should().Be(0);
        interest.PreviousShortPosition.Should().Be(0);
        interest.ChangeInShortPosition.Should().Be(0);
        interest.AverageDailyVolume.Should().BeNull();
        interest.DaysToCover.Should().BeNull();
    }

    // ── Handles API errors gracefully ────────────────────────────────

    [Fact]
    public async Task Import_SettlementDatesFetchFails_ReportsErrorAndReturnsEarly() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _finraClient.GetShortInterestSettlementDates()
            .Throws(new HttpRequestException("API down"));

        await _service.Import(CancellationToken.None);

        await _finraClient.DidNotReceive().GetShortInterest(Arg.Any<DateOnly>());
        await _errorReporter.Received(1).Report(
            ErrorSource.FinraScraper,
            "ShortInterest.FetchDates",
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Import_HttpRequestExceptionOnDate_SkipsDateAndContinues() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { date1, date2 });

        _finraClient.GetShortInterest(date1)
            .Throws(new HttpRequestException("Transient error"));

        _finraClient.GetShortInterest(date2)
            .Returns(CreateInterestRecords(("AAPL", 12_000_000, 10_000_000, 2_000_000, 2_500_000, 4.8m)));

        await _service.Import(CancellationToken.None);

        // Second date data should still be inserted
        var interests = _interestRepo.GetAll().ToList();
        interests.Should().ContainSingle();
        interests[0].SettlementDate.Should().Be(date2);
    }

    [Fact]
    public async Task Import_HttpRequestExceptionOnDate_DoesNotReportToErrorReporter() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        _finraClient.GetShortInterest(settlementDate)
            .Throws(new HttpRequestException("Timeout"));

        await _service.Import(CancellationToken.None);

        // HttpRequestException on a per-date fetch should not be reported
        await _errorReporter.DidNotReceive().Report(
            ErrorSource.FinraScraper,
            "ShortInterest.ImportDate",
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Import_GenericException_ReportsToErrorReporterAndContinues() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var date1 = new DateOnly(2026, 3, 1);
        var date2 = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { date1, date2 });

        _finraClient.GetShortInterest(date1)
            .Throws(new InvalidOperationException("Unexpected"));

        _finraClient.GetShortInterest(date2)
            .Returns(CreateInterestRecords(("AAPL", 12_000_000, 10_000_000, 2_000_000, 2_500_000, 4.8m)));

        await _service.Import(CancellationToken.None);

        // Second date should still be imported
        var interests = _interestRepo.GetAll().ToList();
        interests.Should().ContainSingle();
        interests[0].SettlementDate.Should().Be(date2);

        // Error reporter should have been called for date1
        await _errorReporter.Received(1).Report(
            ErrorSource.FinraScraper,
            "ShortInterest.ImportDate",
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // ── Cancellation ─────────────────────────────────────────────────

    [Fact]
    public async Task Import_CancellationRequested_ThrowsOperationCancelled() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => _service.Import(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── No stocks exist ──────────────────────────────────────────────

    [Fact]
    public async Task Import_NoStocksExist_InsertsNothing() {
        var settlementDate = new DateOnly(2026, 3, 15);
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { settlementDate });

        _finraClient.GetShortInterest(settlementDate)
            .Returns(CreateInterestRecords(("AAPL", 15_000_000, 14_000_000, 1_000_000, 3_000_000, 5.0m)));

        await _service.Import(CancellationToken.None);

        var interests = _interestRepo.GetAll().ToList();
        interests.Should().BeEmpty();
    }

    // ── Respects MinSyncDate ─────────────────────────────────────────

    [Fact]
    public async Task Import_SettlementDateBeforeMinSyncDate_SkipsDate() {
        var apple = CreateStock("AAPL", "Apple Inc.");
        await SeedStocks(apple);

        _workerOptions.MinSyncDate = new DateTime(2026, 3, 10);

        var oldDate = new DateOnly(2026, 3, 1); // Before MinSyncDate
        var newDate = new DateOnly(2026, 3, 15); // After MinSyncDate
        _finraClient.GetShortInterestSettlementDates()
            .Returns(new List<DateOnly> { oldDate, newDate });

        _finraClient.GetShortInterest(newDate)
            .Returns(CreateInterestRecords(("AAPL", 12_000_000, 10_000_000, 2_000_000, 2_500_000, 4.8m)));

        await _service.Import(CancellationToken.None);

        // Only newDate should be imported
        await _finraClient.DidNotReceive().GetShortInterest(oldDate);
        var interests = _interestRepo.GetAll().ToList();
        interests.Should().ContainSingle();
        interests[0].SettlementDate.Should().Be(newDate);
    }
}
