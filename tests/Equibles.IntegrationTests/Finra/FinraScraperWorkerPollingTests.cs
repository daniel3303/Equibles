using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Calendars;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Finra.Data;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService;
using Equibles.Finra.HostedService.Configuration;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// Pins the evening-poll orchestration in <see cref="FinraScraperWorker"/>: during the
/// post-close ET window on a trading day, while today's short-volume file is still
/// unpublished, the worker re-runs only the short-volume import (and requests a fast retry)
/// while skipping the slow-cadence short-interest and off-exchange imports. Outside that
/// condition all three run.
///
/// The exception is one of the ~24 short-interest publication evenings a year, where the
/// semi-monthly file is the thing being polled for: there short interest runs alongside the
/// short-volume poll and keeps the poll armed until its settlement date lands.
///
/// Which imports ran is observed through the shared FINRA client.
/// </summary>
public class FinraScraperWorkerPollingTests : IDisposable
{
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly IFinraClient _finraClient;
    private readonly CommonStockRepository _stockRepo;

    public FinraScraperWorkerPollingTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new FinraModuleConfiguration()
        );
        _stockRepo = new CommonStockRepository(_dbContext);
        _finraClient = Substitute.For<IFinraClient>();
        _finraClient
            .GetDailyShortVolume(Arg.Any<DateOnly>())
            .Returns(new List<ShortVolumeRecord>());
        // Discovery has three entry points — empty store, forward-from-newest, and backfill —
        // and the importer picks between them by what is already stored, so all three are
        // stubbed and the tests observe whichever one their seed data selects.
        _finraClient.GetShortInterestSettlementDates().Returns(new List<DateOnly>());
        _finraClient
            .GetShortInterestSettlementDatesAfter(Arg.Any<DateOnly>())
            .Returns(new List<DateOnly>());
        _finraClient
            .GetShortInterestSettlementDatesBetween(Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new List<DateOnly>());
        _finraClient
            .GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>())
            .Returns(new List<OffExchangeWeeklyRecord>());
    }

    public void Dispose() => _dbContext.Dispose();

    // Wednesday 2025-03-12 16:30 ET — a trading day inside the default 16:00–22:00 window.
    private static DateTimeOffset InWindowTradingDay() => Et(2025, 3, 12, 16, 30);

    private static DateTimeOffset Et(int year, int month, int day, int hour, int minute)
    {
        var unspecified = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, UsMarketCalendar.EasternTimeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private async Task SeedStock()
    {
        _stockRepo.AddRange([
            new CommonStock
            {
                Id = Guid.NewGuid(),
                Ticker = "AAPL",
                Name = "Apple Inc.",
                Cik = "CIK-AAPL",
            },
        ]);
        await _stockRepo.SaveChanges();
    }

    private async Task SeedShortVolume(DateOnly date)
    {
        var stockId = _stockRepo.GetAll().Select(s => s.Id).First();
        _dbContext
            .Set<DailyShortVolume>()
            .Add(
                new DailyShortVolume
                {
                    CommonStockId = stockId,
                    Date = date,
                    ShortVolume = 1,
                    TotalVolume = 1,
                }
            );
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    private TestableFinraScraperWorker BuildWorker(
        DateTimeOffset now,
        FinraScraperOptions options = null
    )
    {
        // The importers resolve repositories from this scope factory.
        var importScopeFactory = ServiceScopeSubstitute.Create(
            (typeof(CommonStockRepository), new CommonStockRepository(_dbContext)),
            (typeof(DailyShortVolumeRepository), new DailyShortVolumeRepository(_dbContext)),
            (typeof(ShortInterestRepository), new ShortInterestRepository(_dbContext)),
            (typeof(OffExchangeVolumeRepository), new OffExchangeVolumeRepository(_dbContext))
        );
        var tickerMapService = new TickerMapService(importScopeFactory);
        var errorReporter = new ErrorReporter(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );
        // Bound the short-volume backfill scan to a few days so the loop stays tiny.
        var workerOptions = Options.Create(
            new WorkerOptions { TickersToSync = [], MinSyncDate = DateTime.UtcNow.AddDays(-3) }
        );

        var shortVolume = new ShortVolumeImportService(
            importScopeFactory,
            Substitute.For<ILogger<ShortVolumeImportService>>(),
            _finraClient,
            tickerMapService,
            errorReporter,
            workerOptions
        );
        var shortInterest = new ShortInterestImportService(
            importScopeFactory,
            Substitute.For<ILogger<ShortInterestImportService>>(),
            _finraClient,
            tickerMapService,
            errorReporter,
            workerOptions
        );
        var offExchange = new OffExchangeVolumeImportService(
            importScopeFactory,
            Substitute.For<ILogger<OffExchangeVolumeImportService>>(),
            _finraClient,
            tickerMapService,
            errorReporter,
            workerOptions
        );

        // The worker resolves the three import services + the two repos its poll gates read.
        var workerScopeFactory = ServiceScopeSubstitute.Create(
            (typeof(ShortVolumeImportService), shortVolume),
            (typeof(ShortInterestImportService), shortInterest),
            (typeof(OffExchangeVolumeImportService), offExchange),
            (typeof(DailyShortVolumeRepository), new DailyShortVolumeRepository(_dbContext)),
            (typeof(ShortInterestRepository), new ShortInterestRepository(_dbContext))
        );

        return new TestableFinraScraperWorker(
            workerScopeFactory,
            errorReporter,
            Options.Create(options ?? new FinraScraperOptions()),
            now
        );
    }

    [Fact]
    public async Task DoWork_InWindowTradingDayWithTodayMissing_RunsOnlyShortVolume()
    {
        await SeedStock();

        await BuildWorker(InWindowTradingDay()).RunCycle(CancellationToken.None);

        await _finraClient.Received().GetDailyShortVolume(Arg.Any<DateOnly>());
        await _finraClient.DidNotReceive().GetShortInterestSettlementDates();
        await _finraClient.DidNotReceive().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_InWindowTradingDayWithTodayStored_RunsAllThree()
    {
        await SeedStock();
        await SeedShortVolume(new DateOnly(2025, 3, 12)); // the ET date of InWindowTradingDay()

        await BuildWorker(InWindowTradingDay()).RunCycle(CancellationToken.None);

        await _finraClient.Received().GetDailyShortVolume(Arg.Any<DateOnly>());
        await _finraClient.Received().GetShortInterestSettlementDates();
        await _finraClient.Received().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_WeekendInWindow_RunsAllThree()
    {
        await SeedStock();

        // Saturday 2025-03-15 16:30 ET — not a trading day, so no polling.
        await BuildWorker(Et(2025, 3, 15, 16, 30)).RunCycle(CancellationToken.None);

        await _finraClient.Received().GetShortInterestSettlementDates();
        await _finraClient.Received().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_TradingDayBeforeWindow_RunsAllThree()
    {
        await SeedStock();

        // Wednesday 10:00 ET — before the 16:00 window start, so no polling.
        await BuildWorker(Et(2025, 3, 12, 10, 0)).RunCycle(CancellationToken.None);

        await _finraClient.Received().GetShortInterestSettlementDates();
        await _finraClient.Received().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_TradingDayAfterWindowEnd_RunsAllThree()
    {
        await SeedStock();

        // Wednesday 22:30 ET — past the 22:00 window end, so the minute-poll has stopped.
        await BuildWorker(Et(2025, 3, 12, 22, 30)).RunCycle(CancellationToken.None);

        await _finraClient.Received().GetShortInterestSettlementDates();
        await _finraClient.Received().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_EveningPollDisabled_RunsAllThreeEvenInWindow()
    {
        await SeedStock();

        var options = new FinraScraperOptions { EveningPollEnabled = false };
        await BuildWorker(InWindowTradingDay(), options).RunCycle(CancellationToken.None);

        await _finraClient.Received().GetShortInterestSettlementDates();
        await _finraClient.Received().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    // Tuesday 2025-03-11 is a FINRA short-interest publication date (the 2025-02-28 settlement);
    // 16:30 ET puts it inside the default post-close window.
    private static DateTimeOffset PublicationEvening() => Et(2025, 3, 11, 16, 30);

    private static readonly DateOnly PublishedSettlementDate = new(2025, 2, 28);

    private async Task SeedShortInterest(DateOnly settlementDate)
    {
        var stockId = _stockRepo.GetAll().Select(s => s.Id).First();
        _dbContext
            .Set<ShortInterest>()
            .Add(
                new ShortInterest
                {
                    CommonStockId = stockId,
                    SettlementDate = settlementDate,
                    CurrentShortPosition = 1,
                }
            );
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task DoWork_PublicationEveningWithFileMissing_RunsShortInterestAndKeepsPolling()
    {
        await SeedStock();

        await BuildWorker(PublicationEvening()).RunCycle(CancellationToken.None);

        // Short interest runs even though today's short-volume file is also still missing —
        // otherwise a late daily file would starve the import we are actually polling for.
        await _finraClient.Received().GetShortInterestSettlementDates();
        // Still minute-polling, so the slow-cadence off-exchange import is skipped.
        await _finraClient.DidNotReceive().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_PublicationEveningWithFileLanded_StopsPolling()
    {
        await SeedStock();
        await SeedShortVolume(new DateOnly(2025, 3, 11));
        await SeedShortInterest(PublishedSettlementDate);

        await BuildWorker(PublicationEvening()).RunCycle(CancellationToken.None);

        // The settlement date is stored, so nothing is outstanding and the cycle completes
        // through to the slow-cadence import instead of re-arming the poll.
        await _finraClient.Received().GetShortInterestSettlementDatesAfter(Arg.Any<DateOnly>());
        await _finraClient.Received().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_PublicationEveningWithShortVolumeStillMissing_KeepsPolling()
    {
        await SeedStock();
        await SeedShortInterest(PublishedSettlementDate);

        await BuildWorker(PublicationEvening()).RunCycle(CancellationToken.None);

        // Short interest has landed but today's short-volume file has not, so the poll stays
        // armed and off-exchange is still deferred.
        await _finraClient.DidNotReceive().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
        // ...and short interest must NOT re-import on every one of those minute-polls: its file
        // is already in, so it drops back to the slow cadence while short volume catches up.
        await _finraClient
            .DidNotReceive()
            .GetShortInterestSettlementDatesAfter(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_PublicationPollDisabled_LeavesShortInterestOnItsSlowCadence()
    {
        await SeedStock();

        var options = new FinraScraperOptions { ShortInterestPublicationPollEnabled = false };
        await BuildWorker(PublicationEvening(), options).RunCycle(CancellationToken.None);

        // Back to the short-volume-only poll: short interest yields to it as before.
        await _finraClient.DidNotReceive().GetShortInterestSettlementDates();
        await _finraClient.DidNotReceive().GetWeeklyOffExchangeVolume(Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task DoWork_NonPublicationEvening_LeavesShortInterestOnItsSlowCadence()
    {
        await SeedStock();

        // Wednesday 2025-03-12 publishes nothing, so the short-volume poll wins as before.
        await BuildWorker(InWindowTradingDay()).RunCycle(CancellationToken.None);

        await _finraClient.DidNotReceive().GetShortInterestSettlementDates();
    }

    [Fact]
    public void EffectiveWait_IdleTradingDayBeforeWindow_CapsToNextWindowStart()
    {
        var now = Et(2025, 3, 12, 10, 0); // Wednesday, before the 16:00 window
        var worker = BuildWorker(now);

        // Idle cycle (DoWork not run, so not polling): a 24h sleep is capped to the time
        // remaining until today's 16:00 ET window start (~6h).
        var wait = worker.ComputeWait(TimeSpan.FromHours(24));

        wait.Should().Be(UsMarketCalendar.TimeUntilNextWindowStart(now, TimeSpan.FromHours(16)));
        wait.Should().BeLessThan(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task EffectiveWait_WhilePolling_PassesIntervalThrough()
    {
        await SeedStock();

        var worker = BuildWorker(InWindowTradingDay());
        await worker.RunCycle(CancellationToken.None); // in-window + missing -> polling armed

        // The poll path must not be capped to the next window — it keeps its short interval.
        worker.ComputeWait(TimeSpan.FromHours(24)).Should().Be(TimeSpan.FromHours(24));
    }

    private sealed class TestableFinraScraperWorker : FinraScraperWorker
    {
        private readonly DateTimeOffset _now;

        public TestableFinraScraperWorker(
            IServiceScopeFactory scopeFactory,
            ErrorReporter errorReporter,
            IOptions<FinraScraperOptions> options,
            DateTimeOffset now
        )
            : base(
                Substitute.For<ILogger<FinraScraperWorker>>(),
                scopeFactory,
                errorReporter,
                options
            )
        {
            _now = now;
        }

        protected override DateTimeOffset UtcNow() => _now;

        public Task RunCycle(CancellationToken stoppingToken) => DoWork(stoppingToken);

        public TimeSpan ComputeWait(TimeSpan interval) => EffectiveWait(interval);
    }
}
