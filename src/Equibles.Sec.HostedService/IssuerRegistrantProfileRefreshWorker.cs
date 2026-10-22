using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Runs <see cref="IssuerRegistrantProfileRefreshService"/> hourly in the worker process, so it
/// shares the single SEC rate limiter with the other EDGAR scrapers. A cold start reads every
/// followed issuer within a few cycles; afterwards each cycle finds only the profiles that aged out.
/// </summary>
public class IssuerRegistrantProfileRefreshWorker : BaseScraperWorker
{
    // One submissions request each; sized so a cold start of about ten thousand issuers drains
    // within a working day without crowding the filing scraper out of the request budget.
    internal const int IssuersPerCycle = 2000;

    // Each archive read walks every archived filing page of the issuer.
    internal const int ArchiveReadsPerCycle = 100;

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
        await scope
            .ServiceProvider.GetRequiredService<IssuerRegistrantProfileRefreshService>()
            .Run(IssuersPerCycle, ArchiveReadsPerCycle, stoppingToken);
    }
}
