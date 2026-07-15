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
    private readonly TimeSpan _enrichmentInterval;

    // UTC stamp of the last cycle that carried the enrichment calls. In-memory on purpose, which
    // means every process start RE-RUNS enrichment on its first cycle (default stamp = due). That
    // errs on the side of extra traffic, never staleness — and is bounded: the pre-split behavior
    // carried enrichment on every single cycle, so even one restart per cycle merely matches the
    // old traffic, while seeding the stamp at startup instead would let frequent deploys starve
    // enrichment indefinitely. Persisting the stamp buys nothing worth a schema change.
    private DateTime _lastEnrichmentAt;

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
        _enrichmentInterval = TimeSpan.FromHours(options.Value.EnrichmentIntervalHours);
        _workerOptions = workerOptions.Value;
    }

    // Pure so the cadence rule is pinnable in tests: enrichment rides along only when the
    // configured interval has elapsed since the last enrichment-carrying cycle (or none ran yet).
    private static bool IsEnrichmentDue(
        DateTime lastEnrichmentAt,
        DateTime now,
        TimeSpan enrichmentInterval
    ) => lastEnrichmentAt == default || now - lastEnrichmentAt >= enrichmentInterval;

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

        var includeEnrichment = IsEnrichmentDue(
            _lastEnrichmentAt,
            DateTime.UtcNow,
            _enrichmentInterval
        );
        var importService = scope.ServiceProvider.GetRequiredService<YahooPriceImportService>();
        await importService.Import(includeEnrichment, stoppingToken);

        // Stamp only after the sweep ran to completion — an interrupted enrichment cycle (deploy,
        // crash) leaves the stamp unset so the next cycle retries it.
        if (includeEnrichment)
            _lastEnrichmentAt = DateTime.UtcNow;
    }
}
