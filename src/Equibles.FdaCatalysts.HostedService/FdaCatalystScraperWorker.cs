using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.FdaCatalysts.HostedService.Configuration;
using Equibles.FdaCatalysts.HostedService.Services;
using Equibles.Worker;

namespace Equibles.FdaCatalysts.HostedService;

/// <summary>
/// Periodically reconciles the FDA advisory-committee calendar into the
/// <c>FdaCatalyst</c> table. A single cycle re-reads the whole calendar and upserts by
/// the per-meeting slug, so there is no incremental state to keep.
/// </summary>
public class FdaCatalystScraperWorker : BaseScraperWorker
{
    protected override string WorkerName => "FDA catalyst scraper";
    protected override TimeSpan SleepInterval { get; }
    protected override ErrorSource ErrorSource => ErrorSource.FdaCatalystScraper;

    public FdaCatalystScraperWorker(
        ILogger<FdaCatalystScraperWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<FdaCatalystScraperOptions> options
    )
        : base(logger, scopeFactory, errorReporter)
    {
        SleepInterval = TimeSpan.FromHours(options.Value.SleepIntervalHours);
    }

    protected override Task DoWork(CancellationToken stoppingToken) =>
        RunImport<FdaAdvisoryCommitteeCalendarImportService>(stoppingToken);
}
