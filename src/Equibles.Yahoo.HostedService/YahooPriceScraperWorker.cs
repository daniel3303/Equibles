using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Worker;
using Equibles.Yahoo.HostedService.Configuration;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Yahoo.HostedService;

public class YahooPriceScraperWorker : BaseScraperWorker
{
    private readonly WorkerOptions _workerOptions;

    protected override string WorkerName => "Yahoo price scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.YahooPriceScraper;

    public YahooPriceScraperWorker(
        ILogger<YahooPriceScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<YahooPriceScraperOptions> options,
        IOptions<WorkerOptions> workerOptions
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
        _workerOptions = workerOptions.Value;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();

        // Cold-start guard (GH-851): skip + retry soon if CompanySync hasn't
        // populated CommonStock yet, instead of a 0-stock no-op then 24h sleep.
        var tickerMapService = scope.ServiceProvider.GetRequiredService<TickerMapService>();
        var tickerMap = await tickerMapService.Build(_workerOptions.TickersToSync, stoppingToken);
        if (tickerMap.Count == 0)
        {
            Logger.LogInformation(
                "Yahoo price scraper: tracked stock universe is empty (company sync pending) — skipping; will retry soon"
            );
            RequestRetrySoon();
            return;
        }

        var importService = scope.ServiceProvider.GetRequiredService<YahooPriceImportService>();
        await importService.Import(stoppingToken);
    }
}
