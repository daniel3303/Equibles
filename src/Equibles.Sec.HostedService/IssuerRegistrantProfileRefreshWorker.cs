using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Runs <see cref="IssuerRegistrantProfileRefreshService"/> in the worker process, so it shares the
/// single SEC rate limiter with the other EDGAR scrapers. While full batches are due (a cold start)
/// the next batch follows after the continuation pause; afterwards each hourly cycle finds only the
/// profiles that aged out, and a restart picks the queue up where it stopped.
/// </summary>
public class IssuerRegistrantProfileRefreshWorker : BaseScraperWorker
{
    // One submissions request each; the pause between batches yields the shared request budget.
    internal const int IssuersPerCycle = 2000;

    private readonly IConfiguration _configuration;

    protected override string WorkerName => "Issuer registrant profile refresh";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(1);
    protected override ErrorSource ErrorSource => ErrorSource.DocumentScraper;

    // Staggered behind the filing scraper (5 minutes) and the EDGAR backfills (9-13 minutes).
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(15);

    public IssuerRegistrantProfileRefreshWorker(
        ILogger<IssuerRegistrantProfileRefreshWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(_configuration, WorkerName, treatWhitespaceAsAbsent: true);

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var run = await scope
            .ServiceProvider.GetRequiredService<IssuerRegistrantProfileRefreshService>()
            .Run(IssuersPerCycle, stoppingToken);
        if (run.MoreDue)
            RequestImmediateContinuation();
    }
}
