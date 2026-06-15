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
/// trailing re-sweep window before it ever imported.
///
/// Each cycle it reconciles <b>every</b> filer that should have filed a recent
/// quarter but hasn't materialised it, against EDGAR's authoritative submission
/// history, and re-ingests any 13F-HR whose holdings we are missing. Candidacy is
/// gated two ways so the scan stays bounded and meaningful:
/// <list type="bullet">
///   <item>only quarters whose 45-day filing deadline has elapsed count as
///     "missing" — a filer still inside its filing window is not a gap;</item>
///   <item>only filers whose newest materialised quarter is within
///     <see cref="RecentWindowQuarters"/> of that deadline are chased — deeper
///     historical gaps belong to the quarterly bulk import, and long-defunct
///     filers are not re-checked forever.</item>
/// </list>
/// It only acts when EDGAR genuinely lists a 13F-HR we lack, so a filer that
/// legitimately filed a 13F-NT notice (e.g. Vanguard Group Inc) or simply stopped
/// filing is left alone. Re-ingestion runs through the shared import path, which
/// republishes the per-quarter snapshot rebuild, so a healed filer reappears in
/// the rankings.
///
/// This is a <b>transitional</b> worker: with the cross-type amendment fix in
/// place no new gaps form, so once a cycle reports <c>converged=true</c> (nothing
/// left to heal) it can be retired — see the deletion follow-up issue.
/// </summary>
public class Holdings13FReconciliationWorker : BaseScraperWorker
{
    // 13F filers have 45 days after a quarter end to submit; only a quarter past
    // that deadline can be called "missing" rather than "not filed yet".
    private const int FilingDeadlineDays = 45;

    // Only chase filers whose newest materialised quarter is within this many
    // quarters of the reconcilable horizon. Older trailing filers are either long
    // defunct or carry deep historical gaps the bulk import owns; chasing them
    // every cycle would grow the EDGAR budget without bound.
    private const int RecentWindowQuarters = 4;

    // Defensive per-cycle ceiling. The deadline + recent-window candidacy already
    // bounds the set to genuinely-behind recent filers; this only guards against a
    // pathological universe stalling the cycle, and a hit is logged as a warning.
    private const int MaxCandidatesPerCycle = 3000;

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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using var scope = ScopeFactory.CreateAsyncScope();
        var snapshotRepo =
            scope.ServiceProvider.GetRequiredService<HolderQuarterlySnapshotRepository>();
        var holderRepo = scope.ServiceProvider.GetRequiredService<InstitutionalHolderRepository>();
        var holdingRepo =
            scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();
        var edgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var ingestionService =
            scope.ServiceProvider.GetRequiredService<Realtime13FIngestionService>();

        var globalLatest = await snapshotRepo
            .GetAll()
            .MaxAsync(s => (DateOnly?)s.ReportDate, stoppingToken);
        if (globalLatest == null)
        {
            Logger.LogInformation("13F reconciliation: no snapshots yet; nothing to reconcile");
            return;
        }

        // Newest quarter a filer can be "late" on (deadline elapsed), and the
        // oldest still worth chasing. Heal up to whichever is later of the ranking
        // horizon and that deadline quarter.
        var reconcileThrough = LatestReconcilableQuarterEnd(today);
        var windowFloor = reconcileThrough.AddMonths(-3 * RecentWindowQuarters);
        var healHorizon =
            globalLatest.Value >= reconcileThrough ? globalLatest.Value : reconcileThrough;

        // Every filer whose newest materialised quarter falls in the recent window
        // but trails the deadline quarter — i.e. it owes a quarter it hasn't
        // materialised. AUM-ordered so the rankings-moving names heal first; the
        // window (not an AUM cut) is what bounds the set, so coverage is universal.
        var laggards = await snapshotRepo
            .GetAll()
            .GroupBy(s => s.InstitutionalHolderId)
            .Select(g => new
            {
                HolderId = g.Key,
                LatestReportDate = g.Max(s => s.ReportDate),
                PeakAum = g.Max(s => s.Aum),
            })
            .Where(x => x.LatestReportDate < reconcileThrough && x.LatestReportDate >= windowFloor)
            .OrderByDescending(x => x.PeakAum)
            .Take(MaxCandidatesPerCycle + 1)
            .ToListAsync(stoppingToken);

        if (laggards.Count > MaxCandidatesPerCycle)
        {
            laggards = laggards.Take(MaxCandidatesPerCycle).ToList();
            Logger.LogWarning(
                "13F reconciliation: more than {Cap} candidate filers in the recent window; "
                    + "capping this cycle and reconciling the rest next cycle",
                MaxCandidatesPerCycle
            );
        }

        if (laggards.Count == 0)
        {
            Logger.LogInformation(
                "13F reconciliation cycle complete: converged=true — no recently-active filer "
                    + "is missing a past-deadline quarter (reconcilable through {Through:yyyy-MM-dd})",
                reconcileThrough
            );
            return;
        }

        var holderIds = laggards.Select(l => l.HolderId).ToList();
        var holders = await holderRepo
            .GetAll()
            .Where(h => holderIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, stoppingToken);

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
                healHorizon
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

        // converged=true means this cycle found nothing left to re-ingest. With the
        // cross-type amendment fix preventing new gaps, a converged cycle is the
        // signal this transitional worker can be retired.
        Logger.LogInformation(
            "13F reconciliation cycle complete: converged={Converged} — re-ingested {Filings} filing(s) "
                + "across {Filers} filer(s) (scanned {Scanned} candidate(s))",
            totalHealed == 0,
            totalHealed,
            filersHealed,
            laggards.Count
        );
    }

    /// <summary>
    /// The most recent quarter end whose 13F filing deadline (45 days after the
    /// quarter end) has elapsed — the newest quarter a filer can be considered
    /// late/missing on. During a quarter's open filing window the worker must not
    /// treat not-yet-filed filers as gaps, or it would chase the whole universe.
    /// </summary>
    internal static DateOnly LatestReconcilableQuarterEnd(DateOnly today)
    {
        var quarterEnd = MostRecentQuarterEnd(today);
        while (today < quarterEnd.AddDays(FilingDeadlineDays))
            quarterEnd = PreviousQuarterEnd(quarterEnd);
        return quarterEnd;
    }

    // The latest calendar quarter end on or before today.
    private static DateOnly MostRecentQuarterEnd(DateOnly today)
    {
        var endMonth = (today.Month - 1) / 3 * 3 + 3; // 3, 6, 9, 12
        var candidate = new DateOnly(
            today.Year,
            endMonth,
            DateTime.DaysInMonth(today.Year, endMonth)
        );
        return candidate <= today ? candidate : PreviousQuarterEnd(candidate);
    }

    // The quarter end one quarter before the given quarter end (3/6/9/12 month).
    private static DateOnly PreviousQuarterEnd(DateOnly quarterEnd)
    {
        var month = quarterEnd.Month - 3;
        var year = quarterEnd.Year;
        if (month == 0)
        {
            month = 12;
            year -= 1;
        }
        return new DateOnly(year, month, DateTime.DaysInMonth(year, month));
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
        DateOnly healHorizon
    ) =>
        edgarFilings
            .Where(f =>
                f.Form != null
                && f.Form.StartsWith("13F-HR", StringComparison.OrdinalIgnoreCase)
                && f.ReportDate >= minReportDate
                && f.ReportDate <= healHorizon
                && !ingestedReportDates.Contains(f.ReportDate)
            )
            .OrderBy(f => f.FilingDate)
            .ThenBy(f => f.AccessionNumber, StringComparer.Ordinal)
            .ToList();
}
