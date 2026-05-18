using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Near-real-time 13F-HR ingestion worker. Runs in parallel with the quarterly
/// <see cref="HoldingsScraperWorker"/>: this surfaces freshly filed 13F-HRs
/// within hours, while the bulk data set remains the authoritative source that
/// reconciles everything at quarter end. Both paths write through the same
/// import pipeline and upsert key, so the later bulk import updates rather than
/// duplicates anything this worker inserted.
/// </summary>
public class Holdings13FRealtimeWorker : BaseScraperWorker
{
    // EDGAR daily indexes aren't published on weekends/holidays, and filings
    // can be accepted late; a short rolling look-back makes the sweep robust
    // to gaps. Re-seeing a filing is harmless — the import upsert is idempotent.
    // Exposed as a seam so tests can pin it without changing production.
    protected virtual int LookbackDays => 4;

    private readonly WorkerOptions _workerOptions;
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "13F real-time ingestion";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);
    protected override ErrorSource ErrorSource => ErrorSource.HoldingsScraper;

    public Holdings13FRealtimeWorker(
        ILogger<Holdings13FRealtimeWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _workerOptions = workerOptions.Value;
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_configuration["Sec:ContactEmail"]))
        {
            Logger.LogWarning(
                "13F real-time ingestion stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file."
            );
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        var startDate = _workerOptions.MinSyncDate ?? new DateTime(2020, 1, 1);
        var minReportDate = DateOnly.FromDateTime(startDate);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var scope = ScopeFactory.CreateAsyncScope();
        var ingestionService =
            scope.ServiceProvider.GetRequiredService<Realtime13FIngestionService>();

        var count = await ingestionService.IngestRecentFilings(
            today,
            LookbackDays,
            minReportDate,
            stoppingToken
        );

        Logger.LogInformation(
            "13F real-time ingestion cycle complete: {Count} filings processed",
            count
        );
    }
}
