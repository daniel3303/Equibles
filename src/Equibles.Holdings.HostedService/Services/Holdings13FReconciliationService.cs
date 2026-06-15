using Equibles.Core.AutoWiring;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// On-demand backfill for the 13F-HR ingestion pipeline. The real-time sweep and
/// the quarterly bulk import can each leave a filer silently missing a quarter — a
/// poisoned import, a filing recorded "processed" yet wiped by a same-quarter
/// Schedule 13D/G restatement, or one that aged out of the trailing re-sweep
/// window before it imported. This service reconciles a filer against EDGAR's
/// authoritative submission history and re-ingests any 13F-HR whose holdings we
/// lack, recording exactly what it did to <see cref="HoldingsReconciliationLog"/>.
///
/// It is driven by a Backoffice button rather than a background worker: with the
/// cross-type amendment fix in place no new gaps form, so reconciliation should be
/// a rare, deliberate, fully-audited action — and a run that actually changes
/// something flags a regression. Candidacy is deadline-aware so it never treats a
/// filer still inside its 45-day filing window as a gap, and window-bounded so it
/// chases recently-active filers rather than long-defunct ones; deeper historical
/// gaps belong to the quarterly bulk import (re-triggered by a parser-version bump).
/// </summary>
[Service]
public class Holdings13FReconciliationService
{
    // 13F filers have 45 days after a quarter end to submit; only a quarter past
    // that deadline can be called "missing" rather than "not filed yet".
    private const int FilingDeadlineDays = 45;

    // Only chase filers whose newest materialised quarter is within this many
    // quarters of the reconcilable horizon, and only heal quarters back to this
    // window. Older trailing filers are either long defunct or carry deep
    // historical gaps the bulk import owns.
    private const int RecentWindowQuarters = 4;

    // A filer checked within this window is skipped when picking the "next" lagging
    // filer, so repeated clicks advance through the backlog instead of re-hitting
    // the same largest laggard (which may legitimately have no newer 13F-HR).
    private static readonly TimeSpan RecheckCooldown = TimeSpan.FromHours(6);

    private readonly HolderQuarterlySnapshotRepository _snapshotRepo;
    private readonly InstitutionalHolderRepository _holderRepo;
    private readonly InstitutionalHoldingRepository _holdingRepo;
    private readonly HoldingsReconciliationLogRepository _logRepo;
    private readonly ISecEdgarClient _edgarClient;
    private readonly Realtime13FIngestionService _ingestionService;
    private readonly ILogger<Holdings13FReconciliationService> _logger;

    public Holdings13FReconciliationService(
        HolderQuarterlySnapshotRepository snapshotRepo,
        InstitutionalHolderRepository holderRepo,
        InstitutionalHoldingRepository holdingRepo,
        HoldingsReconciliationLogRepository logRepo,
        ISecEdgarClient edgarClient,
        Realtime13FIngestionService ingestionService,
        ILogger<Holdings13FReconciliationService> logger
    )
    {
        _snapshotRepo = snapshotRepo;
        _holderRepo = holderRepo;
        _holdingRepo = holdingRepo;
        _logRepo = logRepo;
        _edgarClient = edgarClient;
        _ingestionService = ingestionService;
        _logger = logger;
    }

    /// <summary>
    /// Reconciles the highest-AUM lagging filer not already checked within the
    /// recheck window — the "reconcile next lagging filer" button. One filer per
    /// call. Returns a result the caller can surface to the operator.
    /// </summary>
    public async Task<Holdings13FReconciliationResult> ReconcileNextLaggingFiler(
        string triggeredBy,
        CancellationToken cancellationToken
    )
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var globalLatest = await _snapshotRepo
            .GetAll()
            .MaxAsync(s => (DateOnly?)s.ReportDate, cancellationToken);
        if (globalLatest == null)
        {
            return Holdings13FReconciliationResult.NoCandidates(
                "No holder snapshots exist yet — nothing to reconcile."
            );
        }

        var reconcileThrough = LatestReconcilableQuarterEnd(today);
        var windowFloor = reconcileThrough.AddMonths(-3 * RecentWindowQuarters);

        // Skip filers reconciled within the cooldown so repeated clicks progress.
        var checkedSince = DateTime.UtcNow - RecheckCooldown;
        var recentlyChecked = await _logRepo
            .GetHolderIdsCheckedSince(checkedSince)
            .Distinct()
            .ToListAsync(cancellationToken);

        // The most-AUM filer whose newest materialised quarter falls in the recent
        // window but trails the deadline quarter — i.e. it owes a quarter it hasn't
        // materialised. AUM-ordered so the rankings-moving names heal first.
        var next = await _snapshotRepo
            .GetAll()
            .Where(s => !recentlyChecked.Contains(s.InstitutionalHolderId))
            .GroupBy(s => s.InstitutionalHolderId)
            .Select(g => new
            {
                HolderId = g.Key,
                LatestReportDate = g.Max(s => s.ReportDate),
                PeakAum = g.Max(s => s.Aum),
            })
            .Where(x => x.LatestReportDate < reconcileThrough && x.LatestReportDate >= windowFloor)
            .OrderByDescending(x => x.PeakAum)
            .FirstOrDefaultAsync(cancellationToken);

        if (next == null)
        {
            return Holdings13FReconciliationResult.NoCandidates(
                $"No recently-active filer is missing a past-deadline 13F quarter "
                    + $"(reconcilable through {reconcileThrough:yyyy-MM-dd}). All caught up."
            );
        }

        var holder = await _holderRepo.Get(next.HolderId);
        if (holder == null)
        {
            return Holdings13FReconciliationResult.NoCandidates(
                "The next lagging filer could not be loaded; please try again."
            );
        }

        return await ReconcileFiler(holder, triggeredBy, cancellationToken);
    }

    /// <summary>
    /// Reconciles one specific filer against EDGAR and re-ingests any 13F-HR
    /// quarter whose holdings we are missing. Always idempotent: the shared import
    /// path upserts on its holding key, so a quarter we already hold is never
    /// re-fed. Writes a <see cref="HoldingsReconciliationLog"/> row and returns it.
    /// </summary>
    public async Task<Holdings13FReconciliationResult> ReconcileFiler(
        InstitutionalHolder holder,
        string triggeredBy,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(holder);

        if (string.IsNullOrWhiteSpace(holder.Cik))
        {
            return await Record(
                holder,
                ReconciliationOutcome.Failed,
                0,
                "Filer has no CIK on record; cannot query EDGAR.",
                triggeredBy,
                cancellationToken
            );
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reconcileThrough = LatestReconcilableQuarterEnd(today);
        var windowFloor = reconcileThrough.AddMonths(-3 * RecentWindowQuarters);
        var globalLatest = await _snapshotRepo
            .GetAll()
            .MaxAsync(s => (DateOnly?)s.ReportDate, cancellationToken);
        var healHorizon =
            globalLatest != null && globalLatest.Value >= reconcileThrough
                ? globalLatest.Value
                : reconcileThrough;

        var ingestedDates = await _holdingRepo
            .Get13FReportDatesByHolder(holder)
            .ToListAsync(cancellationToken);
        var ingestedSet = ingestedDates.ToHashSet();

        List<FilingData> edgarFilings;
        try
        {
            // Floor the lookup at the recent-window start, NOT the filer's latest
            // materialised quarter. EDGAR filters by FilingDate, so flooring at the
            // newest held ReportDate would drop a 13F-HR for an OLDER quarter we are
            // missing (filed before it) — exactly the wiped-quarter gap this heals.
            // The window floor covers every in-scope quarter while still excluding
            // the deep archive (those are the bulk import's job).
            edgarFilings = await _edgarClient.GetCompanyFilings(
                holder.Cik,
                documentType: null,
                fromDate: windowFloor,
                toDate: today
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "13F reconciliation: EDGAR submissions lookup failed for {Name} (CIK {Cik})",
                holder.Name,
                holder.Cik
            );
            return await Record(
                holder,
                ReconciliationOutcome.Failed,
                0,
                $"EDGAR submissions lookup failed: {ex.Message}",
                triggeredBy,
                cancellationToken
            );
        }

        var toReingest = SelectFilingsToReingest(
            edgarFilings,
            ingestedSet,
            windowFloor,
            healHorizon
        );
        if (toReingest.Count == 0)
        {
            return await Record(
                holder,
                ReconciliationOutcome.AlreadyCurrent,
                0,
                "EDGAR lists no 13F-HR quarter we are missing.",
                triggeredBy,
                cancellationToken
            );
        }

        var entries = toReingest
            .Select(f => new EdgarDailyIndexEntry
            {
                Cik = holder.Cik,
                AccessionNumber = f.AccessionNumber,
                DateFiled = f.FilingDate,
                FormType = f.Form,
            })
            .ToList();
        var periods = toReingest.Select(f => f.ReportDate).Distinct().OrderBy(d => d).ToList();

        _logger.LogInformation(
            "13F reconciliation: {Name} (CIK {Cik}) is missing {Count} 13F-HR quarter(s) EDGAR lists; re-ingesting {Periods}",
            holder.Name,
            holder.Cik,
            periods.Count,
            FormatPeriods(periods)
        );

        int healed;
        try
        {
            healed = await _ingestionService.IngestSpecificFilings(
                entries,
                windowFloor,
                cancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "13F reconciliation: re-ingest failed for {Name} (CIK {Cik})",
                holder.Name,
                holder.Cik
            );
            return await Record(
                holder,
                ReconciliationOutcome.Failed,
                0,
                $"Re-ingest of {periods.Count} quarter(s) ({FormatPeriods(periods)}) failed: {ex.Message}",
                triggeredBy,
                cancellationToken
            );
        }

        if (healed == 0)
        {
            // EDGAR listed missing quarters but none imported holdings — transient
            // (e.g. CUSIPs not yet seeded) or an upstream empty filing. There is no
            // background retry now, so the operator must reconcile again.
            return await Record(
                holder,
                ReconciliationOutcome.Failed,
                0,
                $"EDGAR listed {periods.Count} missing quarter(s) ({FormatPeriods(periods)}) "
                    + "but none re-imported with holdings; reconcile again to retry.",
                triggeredBy,
                cancellationToken
            );
        }

        var details =
            $"Re-ingested {healed} filing(s) across {periods.Count} missing 13F-HR quarter(s): "
            + $"{FormatPeriods(periods)}.";
        return await Record(
            holder,
            ReconciliationOutcome.Reconciled,
            periods.Count,
            details,
            triggeredBy,
            cancellationToken
        );
    }

    private async Task<Holdings13FReconciliationResult> Record(
        InstitutionalHolder holder,
        ReconciliationOutcome outcome,
        int quartersReingested,
        string details,
        string triggeredBy,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logRepo.Add(
            new HoldingsReconciliationLog
            {
                InstitutionalHolderId = holder.Id,
                HolderName = holder.Name,
                HolderCik = holder.Cik,
                Outcome = outcome,
                QuartersReingested = quartersReingested,
                Details = details,
                TriggeredBy = triggeredBy,
            }
        );
        await _logRepo.SaveChanges();

        return new Holdings13FReconciliationResult
        {
            FilerExamined = true,
            Outcome = outcome,
            HolderId = holder.Id,
            HolderName = holder.Name,
            HolderCik = holder.Cik,
            QuartersReingested = quartersReingested,
            Message = details,
        };
    }

    /// <summary>
    /// The most recent quarter end whose 13F filing deadline (45 days after the
    /// quarter end) has elapsed — the newest quarter a filer can be considered
    /// late/missing on. During a quarter's open filing window we must not treat
    /// not-yet-filed filers as gaps, or reconciliation would flag the whole universe.
    /// </summary>
    internal static DateOnly LatestReconcilableQuarterEnd(DateOnly today)
    {
        var quarterEnd = MostRecentQuarterEnd(today);
        while (today < quarterEnd.AddDays(FilingDeadlineDays))
        {
            quarterEnd = PreviousQuarterEnd(quarterEnd);
        }
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

    private static string FormatPeriods(IEnumerable<DateOnly> periods) =>
        string.Join(", ", periods.Select(p => p.ToString("yyyy-MM-dd")));
}
