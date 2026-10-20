using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Keeps the 13D/13G history complete behind the realtime sweep's trailing window. Each cycle
/// re-reads stored filings whose position has since become resolvable, then walks the next stretch
/// of daily indexes from <see cref="CoverageStart"/> toward that window, reading every XML or
/// pre-XML accession that has neither holdings nor details on file.
/// </summary>
public class Holdings13DGCoverageWorker : BaseScraperWorker
{
    /// <summary>
    /// Bump to walk the daily indexes again from the start, as with a parser or import fix; each
    /// version keeps its own cursor row. Accessions on file are still skipped, and a document that
    /// could not be parsed leaves nothing on file, so the new walk reads it again. v1 recovers the
    /// filings the first realtime backfill lost to defects fixed while it ran, and the voluntary
    /// XML filings before 2024-12-18; v2 adds the pre-XML text and HTML filings from
    /// <see cref="CoverageStart"/>. A <see cref="Filing13DGTextParser.Version"/> bump also starts a
    /// new walk, which reads the refusals parked under the older version.
    /// </summary>
    internal const int CoverageVersion = 2;

    /// <summary>
    /// The walk's first day. Pre-XML schedules end on 2024-12-17; fourteen months of them hold
    /// every passive holder's last annual 13G amendment and every later change.
    /// </summary>
    internal static readonly DateOnly CoverageStart = new(2023, 10, 20);

    // A few minutes of SEC requests (5 a second, shared) and imports; the worker continues at once
    // while days remain.
    internal const int DocumentsPerCycle = 400;

    internal const int RetriesPerCycle = 200;

    // Every read refreshes a filing's details, so a filing that still cannot be imported is
    // retried at most once a day.
    internal static readonly TimeSpan RetryBackoff = TimeSpan.FromDays(1);

    internal static string CursorName =>
        $"Holdings13DGCoverage.v{CoverageVersion}.t{Filing13DGTextParser.Version}";

    private readonly IConfiguration _configuration;
    private readonly WorkerOptions _workerOptions;

    protected override string WorkerName => "13D/13G coverage";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);
    protected override ErrorSource ErrorSource => ErrorSource.HoldingsScraper;

    // Staggered behind the realtime sweep and the detail sync on the shared SEC limiter.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(23);

    public Holdings13DGCoverageWorker(
        ILogger<Holdings13DGCoverageWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IConfiguration configuration,
        IOptions<WorkerOptions> workerOptions
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _configuration = configuration;
        _workerOptions = workerOptions.Value;
    }

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(_configuration, "13D/13G coverage", treatWhitespaceAsAbsent: true);

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        // The pipeline's general floor, matching the realtime sweep's event-date floor.
        var minEventDate = DateOnly.FromDateTime(
            _workerOptions.MinSyncDate ?? new DateTime(2020, 1, 1)
        );

        await using var scope = ScopeFactory.CreateAsyncScope();
        var coverage = scope.ServiceProvider.GetRequiredService<Schedule13DGCoverageService>();
        await coverage.RetryResolvable(RetriesPerCycle, RetryBackoff, minEventDate, stoppingToken);

        var states = scope.ServiceProvider.GetRequiredService<RealtimeSweepStateRepository>();
        var realtime = await states
            .GetByWorker(Holdings13DGRealtimeWorker.WorkerStateName)
            .AsNoTracking()
            .FirstOrDefaultAsync(stoppingToken);
        var cursor = await states
            .GetByWorker(CursorName)
            .AsNoTracking()
            .FirstOrDefaultAsync(stoppingToken);

        var window = ComputeWalkWindow(cursor?.SweptThrough, realtime?.SweptThrough);
        if (window == null)
            return;

        var (from, through) = window.Value;
        var result = await coverage.BackfillDays(
            from,
            through,
            minEventDate,
            DocumentsPerCycle,
            stoppingToken
        );
        if (result.CompletedThrough is { } completed)
            await states.SaveProgress(CursorName, cursor, completed, stoppingToken);

        Logger.LogInformation(
            "13D/13G coverage walked {From:yyyy-MM-dd} through {Through}: {Documents} filings read, {Imported} imported",
            from,
            result.CompletedThrough?.ToString("yyyy-MM-dd") ?? "(none)",
            result.Documents,
            result.Imported
        );

        // The pass ends early once it has read enough documents; an outage waits for the next cycle.
        if (!result.Stopped && result.CompletedThrough < through)
            RequestImmediateContinuation();
    }

    /// <summary>
    /// The days the next pass walks: from the day after <paramref name="cursor"/> (or
    /// <see cref="CoverageStart"/>) up to the day before the realtime sweep's trailing window,
    /// which that sweep still re-reads itself. Null when there is nothing to walk, including before
    /// the realtime sweep has a watermark, since its first cycle reads the whole history.
    /// </summary>
    internal static (DateOnly From, DateOnly Through)? ComputeWalkWindow(
        DateOnly? cursor,
        DateOnly? realtimeSweptThrough
    )
    {
        if (realtimeSweptThrough == null)
            return null;

        var target = realtimeSweptThrough.Value.AddDays(
            -Holdings13DGRealtimeWorker.TrailingReSweepDays - 1
        );
        var from = cursor?.AddDays(1) ?? CoverageStart;
        if (from > target)
            return null;

        return (from, target);
    }
}
