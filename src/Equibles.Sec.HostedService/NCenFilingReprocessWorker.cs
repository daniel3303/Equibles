using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Brings stored N-CEN filings up to <see cref="Equibles.Sec.Data.Models.NCenFiling.CurrentParserVersion"/>
/// on the shared SEC limiter; a parser-version bump re-enrolls every filing with no manual step.
/// </summary>
public class NCenFilingReprocessWorker : BaseScraperWorker
{
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "N-CEN filing reprocess";
    protected override ErrorSource ErrorSource => ErrorSource.NCenReprocess;

    // Once drained a cycle finds nothing; the re-check only picks up filings an outage left behind.
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);

    // Staggered behind the other SEC scrapers so deploy-time EDGAR bursts do not overlap.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(13);

    public NCenFilingReprocessWorker(
        ILogger<NCenFilingReprocessWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "N-CEN filing reprocess",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<NCenFilingReprocessManager>();
        await manager.Run(stoppingToken);
    }
}
