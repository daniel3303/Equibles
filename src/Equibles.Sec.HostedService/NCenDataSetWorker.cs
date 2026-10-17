using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Sec.HostedService;

/// <summary>
/// Keeps SEC's quarterly Form N-CEN data sets imported. SEC posts a quarter shortly after it ends,
/// so a daily check is ample; a cold start drains the 2018-onward backlog a few quarters per cycle
/// through the shared SEC limiter.
/// </summary>
public class NCenDataSetWorker : BaseScraperWorker
{
    // Each archive is 1 to 16 MB; four per cycle keeps one cycle's download modest.
    internal const int MaxDataSetsPerCycle = 4;

    private readonly IConfiguration _configuration;

    protected override string WorkerName => "N-CEN data set import";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected override ErrorSource ErrorSource => ErrorSource.NCenDataSets;

    // Staggered behind the time-sensitive SEC scrapers.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(11);

    public NCenDataSetWorker(
        ILogger<NCenDataSetWorker> logger,
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
        var moreQueued = await scope
            .ServiceProvider.GetRequiredService<NCenDataSetImportService>()
            .ImportPending(MaxDataSetsPerCycle, stoppingToken);
        if (moreQueued)
            RequestImmediateContinuation();
    }
}
