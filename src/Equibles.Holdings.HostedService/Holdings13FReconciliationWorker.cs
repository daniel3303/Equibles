using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Self-healing backstop for the 13F-HR ingestion pipeline. The real-time sweep
/// and the quarterly bulk import can both leave a filer silently missing a
/// quarter — a poisoned import, a filing recorded "processed" yet wiped by a
/// same-quarter Schedule 13D/G restatement, or one that simply aged out of the
/// trailing re-sweep window before it ever imported. When that filer is large
/// (BlackRock, ~$5T) it vanishes from the AUM rankings entirely, because the
/// Top-by-AUM page only ranks filers at the single global latest report date.
///
/// This worker reconciles the largest lagging filers against EDGAR's
/// authoritative submission history: for each filer whose latest materialised
/// quarter trails the global latest, it asks EDGAR which 13F-HRs that filer has
/// actually filed and re-ingests any whose holdings we are missing. It only acts
/// when EDGAR genuinely lists a 13F-HR we lack, so a filer that legitimately
/// filed a 13F-NT notice (e.g. Vanguard Group Inc) or simply stopped filing is
/// left alone. Re-ingestion runs through the shared import path, which republishes
/// the per-quarter snapshot rebuild, so a healed filer reappears in the rankings.
/// </summary>
public class Holdings13FReconciliationWorker : BaseScraperWorker
{
    // Reconcile only the largest lagging filers each cycle. They are the ones
    // that move the rankings, and the cap bounds the per-cycle EDGAR request
    // budget (one submissions lookup per candidate, plus artifact fetches only
    // for the filers actually missing a quarter).
    private const int MaxLaggingFilersPerCycle = 200;

    private readonly WorkerOptions _workerOptions;
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "13F reconciliation";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(24);
    protected override ErrorSource ErrorSource => ErrorSource.HoldingsScraper;

    // Let the real-time sweep and bulk import take the SEC request budget first
    // after a deploy; this backstop is not time-sensitive.
    protected override TimeSpan StartupDelay => TimeSpan.FromMinutes(15);

    public Holdings13FReconciliationWorker(
        ILogger<Holdings13FReconciliationWorker> logger,
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

    protected override bool ValidateConfiguration() =>
        ValidateSecContactEmail(
            _configuration,
            "13F reconciliation",
            treatWhitespaceAsAbsent: true
        );

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        var startDate = _workerOptions.MinSyncDate ?? new DateTime(2020, 1, 1);
        var minReportDate = DateOnly.FromDateTime(startDate);

        await using var scope = ScopeFactory.CreateAsyncScope();
        var snapshotRepo =
            scope.ServiceProvider.GetRequiredService<HolderQuarterlySnapshotRepository>();
        var holderRepo = scope.ServiceProvider.GetRequiredService<InstitutionalHolderRepository>();
        var holdingRepo =
            scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();
        var edgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var ingestionService =
            scope.ServiceProvider.GetRequiredService<Realtime13FIngestionService>();

        // The latest quarter any filer has materialised — the date the Top-by-AUM
        // ranking pins to. A filer trailing this is a reconciliation candidate.
        var globalLatest = await snapshotRepo
            .GetAll()
            .MaxAsync(s => (DateOnly?)s.ReportDate, stoppingToken);
        if (globalLatest == null)
        {
            Logger.LogInformation("13F reconciliation: no snapshots yet; nothing to reconcile");
            return;
        }

        // Largest filers (by peak materialised AUM) whose newest quarter trails
        // the global latest. Peak AUM keeps dead micro-filers out of the budget
        // and puts the rankings-moving names (BlackRock, Vanguard) first.
        var laggards = await snapshotRepo
            .GetAll()
            .GroupBy(s => s.InstitutionalHolderId)
            .Select(g => new
            {
                HolderId = g.Key,
                LatestReportDate = g.Max(s => s.ReportDate),
                PeakAum = g.Max(s => s.Aum),
            })
            .Where(x => x.LatestReportDate < globalLatest.Value)
            .OrderByDescending(x => x.PeakAum)
            .Take(MaxLaggingFilersPerCycle)
            .ToListAsync(stoppingToken);

        if (laggards.Count == 0)
        {
            Logger.LogInformation(
                "13F reconciliation: every materialised filer is current at {GlobalLatest:yyyy-MM-dd}",
                globalLatest.Value
            );
            return;
        }

        var holderIds = laggards.Select(l => l.HolderId).ToList();
        var holders = await holderRepo
            .GetAll()
            .Where(h => holderIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, stoppingToken);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var totalHealed = 0;
        var filersHealed = 0;

        foreach (var laggard in laggards)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (
                !holders.TryGetValue(laggard.HolderId, out var holder)
                || string.IsNullOrWhiteSpace(holder.Cik)
            )
                continue;

            List<FilingData> edgarFilings;
            try
            {
                // FilingDate-floored at the filer's latest materialised quarter:
                // any 13F-HR for a newer quarter is filed after it, so this stays
                // a cheap recent-block lookup instead of paging the full archive.
                edgarFilings = await edgarClient.GetCompanyFilings(
                    holder.Cik,
                    documentType: null,
                    fromDate: laggard.LatestReportDate,
                    toDate: today
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(
                    ex,
                    "13F reconciliation: EDGAR submissions lookup failed for {Name} (CIK {Cik}); skipping",
                    holder.Name,
                    holder.Cik
                );
                continue;
            }

            var ingestedDates = await holdingRepo
                .Get13FReportDatesByHolder(holder)
                .ToListAsync(stoppingToken);
            var ingestedSet = ingestedDates.ToHashSet();

            var toReingest = SelectFilingsToReingest(
                edgarFilings,
                ingestedSet,
                minReportDate,
                globalLatest.Value
            );
            if (toReingest.Count == 0)
                continue;

            var entries = toReingest
                .Select(f => new EdgarDailyIndexEntry
                {
                    Cik = holder.Cik,
                    AccessionNumber = f.AccessionNumber,
                    DateFiled = f.FilingDate,
                    FormType = f.Form,
                })
                .ToList();

            Logger.LogInformation(
                "13F reconciliation: {Name} (CIK {Cik}) is missing {Count} 13F-HR quarter(s) EDGAR lists; re-ingesting {Periods}",
                holder.Name,
                holder.Cik,
                toReingest.Count,
                string.Join(", ", toReingest.Select(f => f.ReportDate.ToString("yyyy-MM-dd")))
            );

            var healed = await ingestionService.IngestSpecificFilings(
                entries,
                minReportDate,
                stoppingToken
            );
            totalHealed += healed;
            if (healed > 0)
                filersHealed++;
        }

        Logger.LogInformation(
            "13F reconciliation cycle complete: re-ingested {Filings} filing(s) across {Filers} filer(s) (scanned {Scanned} lagging filer(s))",
            totalHealed,
            filersHealed,
            laggards.Count
        );
    }

    /// <summary>
    /// The 13F-HR filings (originals and amendments, never 13F-NT notices) that
    /// EDGAR lists for a filer but whose holdings we hold none of — the gaps to
    /// heal. Ordered oldest-filed first so an original always re-imports before
    /// its amendment.
    /// </summary>
    internal static List<FilingData> SelectFilingsToReingest(
        IReadOnlyCollection<FilingData> edgarFilings,
        ISet<DateOnly> ingestedReportDates,
        DateOnly minReportDate,
        DateOnly globalLatest
    ) =>
        edgarFilings
            .Where(f =>
                f.Form != null
                && f.Form.StartsWith("13F-HR", StringComparison.OrdinalIgnoreCase)
                && f.ReportDate >= minReportDate
                && f.ReportDate <= globalLatest
                && !ingestedReportDates.Contains(f.ReportDate)
            )
            .OrderBy(f => f.FilingDate)
            .ThenBy(f => f.AccessionNumber, StringComparer.Ordinal)
            .ToList();
}
