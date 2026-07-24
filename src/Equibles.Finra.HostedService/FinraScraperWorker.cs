using Equibles.Core.Calendars;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data.Calendars;
using Equibles.Finra.HostedService.Configuration;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Finra.HostedService;

public class FinraScraperWorker : BaseScraperWorker
{
    private readonly FinraScraperOptions _options;

    // True when the cycle just polled for a file FINRA hasn't published yet, so the next
    // wait uses the short poll interval rather than the idle wait-until-next-window. Reset
    // at the top of every DoWork, mirroring the base class resetting its own retry flag.
    private bool _pollWaitRequested;

    protected override string WorkerName => "FINRA scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FinraScraper;

    protected override TimeSpan NotReadyRetryInterval =>
        TimeSpan.FromMinutes(_options.ShortVolumePollIntervalMinutes);

    public FinraScraperWorker(
        ILogger<FinraScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FinraScraperOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _options = options.Value;
        SleepInterval = TimeSpan.FromHours(_options.SleepIntervalHours);
    }

    /// <summary>Current instant; a protected seam so tests can pin "now" deterministically.</summary>
    protected virtual DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;

    protected override bool ValidateConfiguration()
    {
        using var scope = ScopeFactory.CreateScope();
        var finraClient = scope.ServiceProvider.GetRequiredService<IFinraClient>();
        if (!finraClient.IsConfigured)
        {
            Logger.LogWarning("FINRA Scraper stopped: FINRA API credentials not configured.");
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        _pollWaitRequested = false;

        // Short volume is always attempted first — its importer scans [floor, today] and
        // stores nothing for a day FINRA hasn't published, so an unpublished day leaves the
        // DB short of today's session and ShortVolumeOutstanding stays true.
        await RunShortVolumeImport(stoppingToken);
        var shortVolumeOutstanding = await ShortVolumeOutstanding(stoppingToken);

        // On one of the ~24 publication evenings a year, the short-interest file is the thing
        // we are waiting for, so its import runs even while short volume is still outstanding —
        // otherwise a late daily file would starve the very import being polled for. On every
        // other cycle short interest keeps its slow cadence and yields to the short-volume poll.
        var publishingCycle = PublishingCycleInWindow();
        var shortInterestOutstanding = false;
        if (publishingCycle != null)
        {
            await RunShortInterestImport(stoppingToken);
            shortInterestOutstanding = !await SettlementDateStored(
                publishingCycle.SettlementDate,
                stoppingToken
            );
        }
        else if (!shortVolumeOutstanding)
        {
            await RunShortInterestImport(stoppingToken);
        }

        if (shortVolumeOutstanding || shortInterestOutstanding)
        {
            Logger.LogInformation(
                "FINRA has not published {Pending} yet; polling again in {Minutes} min",
                PendingDescription(
                    shortVolumeOutstanding,
                    publishingCycle,
                    shortInterestOutstanding
                ),
                _options.ShortVolumePollIntervalMinutes
            );
            _pollWaitRequested = true;
            RequestRetrySoon();
            // Skip the slow-cadence import while minute-polling so we don't hammer it.
            return;
        }

        await RunOffExchangeVolumeImport(stoppingToken);
    }

    private static string PendingDescription(
        bool shortVolumeOutstanding,
        ShortInterestReportingCycle publishingCycle,
        bool shortInterestOutstanding
    )
    {
        var pending = new List<string>(2);
        if (shortVolumeOutstanding)
            pending.Add("today's short-volume file");
        if (shortInterestOutstanding)
            pending.Add($"short interest settling {publishingCycle.SettlementDate:yyyy-MM-dd}");
        return string.Join(" and ", pending);
    }

    private async Task RunShortVolumeImport(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Starting daily short volume import");
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ShortVolumeImportService>();
        await service.Import(stoppingToken);
    }

    private async Task RunShortInterestImport(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Starting short interest import");
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ShortInterestImportService>();
        await service.Import(stoppingToken);
    }

    private async Task RunOffExchangeVolumeImport(CancellationToken stoppingToken)
    {
        Logger.LogInformation("Starting off-exchange volume import");
        await using var scope = ScopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<OffExchangeVolumeImportService>();
        await service.Import(stoppingToken);
    }

    // True while we should keep minute-polling for today's short-volume file: ET-now is inside
    // the post-close window on an NYSE trading day and today's session row is not yet stored.
    private async Task<bool> ShortVolumeOutstanding(CancellationToken stoppingToken)
    {
        var etDate = EveningWindowDate();
        if (etDate == null || !UsMarketCalendar.IsTradingDay(etDate.Value))
            return false;

        await using var scope = ScopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyShortVolumeRepository>();
        var alreadyStored = await repo.GetByDate(etDate.Value).AnyAsync(stoppingToken);
        return !alreadyStored;
    }

    // The short-interest cycle FINRA publishes today, when ET-now is inside the post-close
    // window and the publication poll is enabled; null on any other cycle. Publication dates
    // are derived as trading-day arithmetic, so they are always trading days.
    private ShortInterestReportingCycle PublishingCycleInWindow()
    {
        if (!_options.ShortInterestPublicationPollEnabled)
            return null;

        var etDate = EveningWindowDate();
        return etDate == null ? null : ShortInterestCalendar.PublishingOn(etDate.Value);
    }

    // Today's ET date when evening polling is enabled and ET-now is inside the post-close
    // window; null otherwise. The window opens at the regular market close so a file that
    // lands minutes later is picked up the same evening.
    private DateOnly? EveningWindowDate()
    {
        if (!_options.EveningPollEnabled)
            return null;

        var nowEt = UsMarketCalendar.ToEastern(UtcNow());
        var timeOfDay = nowEt.TimeOfDay;
        if (
            timeOfDay < TimeSpan.FromHours(_options.WindowStartHourEt)
            || timeOfDay >= TimeSpan.FromHours(_options.WindowEndHourEt)
        )
            return null;

        return DateOnly.FromDateTime(nowEt.DateTime);
    }

    // True once any row for this settlement date has landed — the importer stores nothing for
    // a date FINRA has not published, so the first stored row means the file is out.
    private async Task<bool> SettlementDateStored(
        DateOnly settlementDate,
        CancellationToken stoppingToken
    )
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ShortInterestRepository>();
        return await repo.GetBySettlementDate(settlementDate).AnyAsync(stoppingToken);
    }

    protected override Task WaitForNextCycle(TimeSpan interval, CancellationToken stoppingToken) =>
        base.WaitForNextCycle(EffectiveWait(interval), stoppingToken);

    // The wait the worker actually uses. While polling, the short retry interval passes
    // through unchanged. On an idle cycle (and only when evening polling is enabled) it is
    // capped at the time until the next trading-day poll window, so a long SleepInterval
    // never sleeps past the evening the file publishes. Protected so tests can pin it.
    protected TimeSpan EffectiveWait(TimeSpan interval)
    {
        if (_pollWaitRequested || !_options.EveningPollEnabled)
            return interval;

        var untilWindow = UsMarketCalendar.TimeUntilNextWindowStart(
            UtcNow(),
            TimeSpan.FromHours(_options.WindowStartHourEt)
        );
        return untilWindow < interval ? untilWindow : interval;
    }
}
