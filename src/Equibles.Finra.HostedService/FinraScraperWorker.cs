using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
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

    // True when the cycle just polled for an unpublished short-volume file, so the next
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
        // DB short of today's session and ShouldPollForToday stays true.
        await RunShortVolumeImport(stoppingToken);

        if (await ShouldPollForToday(stoppingToken))
        {
            Logger.LogInformation(
                "Short volume for today's session is not published yet; polling again in {Minutes} min",
                _options.ShortVolumePollIntervalMinutes
            );
            _pollWaitRequested = true;
            RequestRetrySoon();
            // Skip the slow-cadence imports while minute-polling so we don't hammer them.
            return;
        }

        await RunShortInterestImport(stoppingToken);
        await RunOffExchangeVolumeImport(stoppingToken);
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

    // True while we should keep minute-polling for today's file: evening polling enabled,
    // ET-now is on an NYSE trading day inside the post-close window, and today's session row
    // is not yet stored.
    private async Task<bool> ShouldPollForToday(CancellationToken stoppingToken)
    {
        if (!_options.EveningPollEnabled)
            return false;

        var nowEt = UsMarketCalendar.ToEastern(UtcNow());
        var etDate = DateOnly.FromDateTime(nowEt.DateTime);

        if (!UsMarketCalendar.IsTradingDay(etDate))
            return false;

        var timeOfDay = nowEt.TimeOfDay;
        if (
            timeOfDay < TimeSpan.FromHours(_options.WindowStartHourEt)
            || timeOfDay >= TimeSpan.FromHours(_options.WindowEndHourEt)
        )
            return false;

        await using var scope = ScopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<DailyShortVolumeRepository>();
        var alreadyStored = await repo.GetByDate(etDate).AnyAsync(stoppingToken);
        return !alreadyStored;
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
