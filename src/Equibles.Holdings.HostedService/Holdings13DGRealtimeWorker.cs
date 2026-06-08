using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Near-real-time Schedule 13D/13G ingestion worker. Sweeps EDGAR's daily index
/// for newly accepted beneficial-ownership filings and feeds them through the
/// shared holdings import pipeline. Unlike 13F there is no quarterly bulk data
/// set to reconcile against, so this worker is the sole source — and coverage
/// only goes back to 2024-12-18, when 13D/13G became machine-readable XML.
/// </summary>
public class Holdings13DGRealtimeWorker : BaseScraperWorker
{
    // Schedule 13D/13G became machine-readable XML on this date; earlier filings
    // are unstructured HTML and out of scope, so the sweep never goes before it.
    private static readonly DateOnly ScheduleXmlInception = new(2024, 12, 18);

    // Once a watermark exists, still re-sweep this many recent days so late and
    // amended filings (which carry a fresh accession) are always re-checked.
    private const int TrailingReSweepDays = 14;

    private const string WorkerStateName = "Holdings13DGRealtime";

    private readonly IConfiguration _configuration;

    protected override string WorkerName => "13D/13G real-time ingestion";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);
    protected override ErrorSource ErrorSource => ErrorSource.HoldingsScraper;

    // Stagger behind the 13F real-time worker so the two don't hit SEC's rate
    // limiter at the same instant on startup.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(2);

    public Holdings13DGRealtimeWorker(
        ILogger<Holdings13DGRealtimeWorker> logger,
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
            "13D/13G real-time ingestion",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var scope = ScopeFactory.CreateAsyncScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<RealtimeSweepStateRepository>();
        var ingestionService =
            scope.ServiceProvider.GetRequiredService<Realtime13DGIngestionService>();

        var state = await stateRepo.GetByWorker(WorkerStateName).FirstOrDefaultAsync(stoppingToken);

        var windowStart = ComputeWindowStart(today, state?.SweptThrough);
        var lookbackDays = today.DayNumber - windowStart.DayNumber + 1;

        Logger.LogInformation(
            "13D/13G real-time ingestion sweeping {LookbackDays} days of EDGAR daily index (from {Start:yyyy-MM-dd})",
            lookbackDays,
            windowStart
        );

        var result = await ingestionService.IngestRecentFilings(
            today,
            lookbackDays,
            ScheduleXmlInception,
            stoppingToken
        );

        var newWatermark = ComputeNextWatermark(today, result.EarliestFailedDate);
        await SaveWatermark(stateRepo, state, newWatermark);

        Logger.LogInformation(
            "13D/13G real-time ingestion cycle complete: {Count} filings processed, swept through {Watermark:yyyy-MM-dd}",
            result.FilingsImported,
            newWatermark
        );

        if (result.FilingsImported > 0)
        {
            var maintenance =
                scope.ServiceProvider.GetRequiredService<HoldingsTableMaintenanceService>();
            await maintenance.VacuumInstitutionalHoldings(stoppingToken);
        }
    }

    /// <summary>
    /// The first daily-index date to sweep. With no watermark (cold start) it
    /// goes back to the XML-inception date — the full available 13D/13G history.
    /// With a watermark it resumes from it, but never covers fewer than the
    /// trailing re-sweep window, and never crosses below inception.
    /// </summary>
    internal static DateOnly ComputeWindowStart(DateOnly today, DateOnly? watermark)
    {
        if (!watermark.HasValue)
            return ScheduleXmlInception;

        var trailingStart = today.AddDays(-TrailingReSweepDays);
        var start = watermark.Value < trailingStart ? watermark.Value : trailingStart;
        return start < ScheduleXmlInception ? ScheduleXmlInception : start;
    }

    /// <summary>
    /// The watermark to persist after a cycle: today when every day swept
    /// cleanly, otherwise the day before the earliest failed day so it (and
    /// everything skipped behind it) is re-swept next cycle.
    /// </summary>
    internal static DateOnly ComputeNextWatermark(DateOnly today, DateOnly? earliestFailedDate) =>
        earliestFailedDate.HasValue ? earliestFailedDate.Value.AddDays(-1) : today;

    private static async Task SaveWatermark(
        RealtimeSweepStateRepository repo,
        RealtimeSweepState existing,
        DateOnly sweptThrough
    )
    {
        if (existing == null)
        {
            repo.Add(
                new RealtimeSweepState
                {
                    WorkerName = WorkerStateName,
                    SweptThrough = sweptThrough,
                    UpdatedAt = DateTime.UtcNow,
                }
            );
        }
        else
        {
            existing.SweptThrough = sweptThrough;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await repo.SaveChanges();
    }
}
