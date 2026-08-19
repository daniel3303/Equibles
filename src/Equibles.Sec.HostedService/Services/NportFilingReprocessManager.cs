using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Helpers;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Re-derives the schedule of holdings for NPORT-P filings whose
/// <see cref="NportFiling.ParserVersion"/> sits below
/// <see cref="NportFiling.CurrentParserVersion"/>. For each such filing it re-fetches the
/// submission from EDGAR, re-parses it through <see cref="NportFilingProcessor.ParseEntity"/>,
/// replaces the stored holdings and header facts, and stamps the current version.
///
/// The parser version is the single selector: once a filing is stamped at the current version it
/// drops out, so the run terminates and is resumable — an interrupted run continues where it left
/// off next invocation, and bumping <see cref="NportFiling.CurrentParserVersion"/> after a parser
/// change re-enrolls every filing automatically. Filings that imported before holdings were parsed
/// correctly default to version 0 and are backfilled on the first pass.
///
/// Filings are processed strictly ONE at a time and the stored schedule is replaced with a
/// set-based delete: a single filing can carry hundreds of thousands of holdings, so loading a
/// batch of schedules through the change tracker (or lazy-loading one via the
/// <see cref="NportFiling.Holdings"/> navigation) exhausts the worker's heap — a parser-version
/// bump once OOM-crashed every worker lane this way.
/// </summary>
[Service]
public class NportFilingReprocessManager
{
    // Page size of the id-only selection query and cadence of the progress log line. Each filing
    // still loads, commits and releases individually — paging only avoids re-running the ordered
    // selection (with its growing exclusion list) once per filing.
    private const int BatchSize = 32;

    // After this many failed fetch/parse attempts a filing is advanced to the current version even
    // though its holdings were never re-derived, so a permanently-unfetchable filing (pulled
    // submission, missing CIK) can't keep re-selecting itself every cycle.
    internal const int MaxReprocessAttempts = 3;

    private readonly NportFilingRepository _filingRepository;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<NportFilingReprocessManager> _logger;

    // The tracked-stock CUSIP set, loaded on first need within a run. Only sweep-discovered
    // (registrant-only) filings store a CUSIP-filtered schedule, so the set is consulted only when
    // one is re-derived — most runs never touch it.
    private HashSet<string> _trackedCusips;

    public NportFilingReprocessManager(
        NportFilingRepository filingRepository,
        CommonStockRepository commonStockRepository,
        ISecEdgarClient secEdgarClient,
        EquiblesFinancialDbContext dbContext,
        ErrorReporter errorReporter,
        ILogger<NportFilingReprocessManager> logger
    )
    {
        _filingRepository = filingRepository;
        _commonStockRepository = commonStockRepository;
        _secEdgarClient = secEdgarClient;
        _dbContext = dbContext;
        _errorReporter = errorReporter;
        _logger = logger;
    }

    /// <summary>
    /// Brings every filing below <see cref="NportFiling.CurrentParserVersion"/> up to date.
    /// Filings of a series in <paramref name="fullFidelitySeriesIds"/> keep their whole schedule
    /// rather than being re-narrowed to tracked-stock positions, and any of that series' filings
    /// still holding a narrowed schedule are re-enrolled first so the opt-in heals history too.
    /// </summary>
    public async Task<NportFilingReprocessResult> Run(
        IReadOnlySet<string> fullFidelitySeriesIds = null,
        CancellationToken cancellationToken = default
    )
    {
        fullFidelitySeriesIds ??= NportFullFidelitySeries.None;
        await ReenrollNarrowedFullFidelityFilings(fullFidelitySeriesIds, cancellationToken);

        var result = new NportFilingReprocessResult
        {
            Total = await _filingRepository
                .GetAll()
                .Where(f => f.ParserVersion < NportFiling.CurrentParserVersion)
                .CountAsync(cancellationToken),
        };

        if (result.Total == 0)
            return result;

        // Replacing a six-figure schedule in one commit can run long; lift the per-command timeout
        // so the set-based delete and the insert save don't trip the default. Guarded because the
        // timeout is a relational-only facility (the in-memory provider used in tests rejects it).
        if (_dbContext.Database.IsRelational())
            _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        // No DB cursor: a reprocessed filing advances to the current version and drops out of the
        // filter, so each pass takes the next filing. Filings that fail this run are held
        // in-memory and excluded so the run still terminates; they're retried on the next run.
        var failedThisRun = new HashSet<Guid>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var page = await _filingRepository
                .GetAll()
                .Where(f => f.ParserVersion < NportFiling.CurrentParserVersion)
                .Where(f => !failedThisRun.Contains(f.Id))
                .OrderBy(f => f.FilingDate)
                .Select(f => f.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (page.Count == 0)
                break;

            foreach (var filingId in page)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var filing = await _filingRepository
                    .GetAll()
                    .Include(f => f.CommonStock)
                    .FirstOrDefaultAsync(f => f.Id == filingId, cancellationToken);

                // Deleted (or already advanced) by a concurrent ingest since the page was taken.
                if (filing == null || filing.ParserVersion >= NportFiling.CurrentParserVersion)
                    continue;

                try
                {
                    result.HoldingsAdded += await ReprocessFiling(
                        filing,
                        fullFidelitySeriesIds,
                        cancellationToken
                    );
                    result.Processed++;
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // A concurrent ingest inserted the same rows — the filing's replace rolled
                    // back; retry it next run without burning an attempt.
                    _logger.LogWarning(
                        ex,
                        "NPORT-P reprocess hit a concurrent-write conflict on {AccessionNumber}; retrying next run",
                        filing.AccessionNumber
                    );
                    failedThisRun.Add(filing.Id);
                    result.Failed++;
                }
                catch (Exception ex)
                    when (ex is not OperationCanceledException
                        || !cancellationToken.IsCancellationRequested
                    )
                {
                    // One bad filing must not abort the run OR the attempt ledger. This arm also
                    // owns EDGAR's per-request HttpClient timeout: it surfaces as
                    // TaskCanceledException while our token stays uncancelled (the fetch takes no
                    // token), and letting it escape would restart the run with a fresh
                    // failedThisRun — the same slow filing re-selected first, forever, with no
                    // attempt burned. Only a genuine shutdown (our token cancelled) propagates.
                    // Drop whatever partial graph the failure left tracked, then record the
                    // attempt on a freshly-loaded row; at the ceiling the filing is stamped
                    // current so it stops re-selecting itself.
                    _dbContext.ChangeTracker.Clear();
                    await RecordFailedAttempt(filing.Id, ex, cancellationToken);
                    failedThisRun.Add(filing.Id);
                    result.Failed++;
                }
                finally
                {
                    // Release the filing's tracked graph before loading the next one.
                    _dbContext.ChangeTracker.Clear();
                }

                if ((result.Processed + result.Failed) % BatchSize == 0)
                    _logger.LogInformation(
                        "NPORT-P reprocess: {Processed}/{Total} filings, holdings added={HoldingsAdded}, failed={Failed}",
                        result.Processed,
                        result.Total,
                        result.HoldingsAdded,
                        result.Failed
                    );
            }
        }

        _logger.LogInformation(
            "NPORT-P reprocess pass complete: {Processed}/{Total} filings, holdings added={HoldingsAdded}, failed={Failed}",
            result.Processed,
            result.Total,
            result.HoldingsAdded,
            result.Failed
        );
        return result;
    }

    // Drops the parser stamp on opted-in filings whose stored schedule is still narrowed, so the
    // pass below re-derives them from EDGAR. Bumping CurrentParserVersion would do the same job for
    // the whole table — a six-figure re-fetch through the shared SEC budget — where the opt-in only
    // invalidates the series it names.
    //
    // Two guards keep it from looping. The selection is self-clearing: a filing re-derived at full
    // fidelity ends with its schedule matching its reported count and stops being selected. And a
    // filing EDGAR will never return advances to the current version at the attempt ceiling, so
    // this skips it rather than resetting the stamp the ceiling just set.
    private async Task ReenrollNarrowedFullFidelityFilings(
        IReadOnlySet<string> fullFidelitySeriesIds,
        CancellationToken cancellationToken
    )
    {
        if (fullFidelitySeriesIds.Count == 0)
            return;

        var narrowed = await _filingRepository
            .GetNarrowedBelowReportedCount(fullFidelitySeriesIds.ToList())
            .Where(f => f.ParserVersion >= NportFiling.CurrentParserVersion)
            .Where(f => f.ReprocessAttempts < MaxReprocessAttempts)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        if (narrowed.Count == 0)
            return;

        // Version 0 is the pre-versioning marker the reprocess pass already treats as "re-derive
        // me", so it needs no separate sentinel.
        if (_dbContext.Database.IsRelational())
        {
            await _filingRepository
                .GetAll()
                .Where(f => narrowed.Contains(f.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.ParserVersion, 0), cancellationToken);
        }
        else
        {
            // The in-memory provider (tests) has no ExecuteUpdate; the seeded set is tiny.
            var filings = await _filingRepository
                .GetAll()
                .Where(f => narrowed.Contains(f.Id))
                .ToListAsync(cancellationToken);
            foreach (var filing in filings)
                filing.ParserVersion = 0;
            await _filingRepository.SaveChanges();
        }

        _dbContext.ChangeTracker.Clear();

        _logger.LogInformation(
            "NPORT-P reprocess: re-enrolled {Count} filings of full-fidelity series still holding a narrowed schedule",
            narrowed.Count
        );
    }

    // Only a unique violation is trusted as "another writer got there first" and retried without
    // burning an attempt; every other database failure (e.g. a length or constraint violation) is
    // deterministic for the filing and must count toward the ceiling.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    // Returns the number of holdings parsed onto the filing. Throws on any fetch/parse failure so
    // the caller can record the attempt and retry the filing on a later run. The EDGAR fetch and
    // parse run before any database write, so a failure never leaves the filing half-replaced.
    private async Task<int> ReprocessFiling(
        NportFiling filing,
        IReadOnlySet<string> fullFidelitySeriesIds,
        CancellationToken cancellationToken
    )
    {
        // A sweep-discovered filing has no tracked stock; its registrant CIK is the one to re-fetch
        // from. A feed-crawled filing carries no registrant CIK and re-fetches via its stock's.
        var cik = filing.RegistrantCik ?? filing.CommonStock?.Cik;
        if (string.IsNullOrEmpty(cik))
            throw new InvalidOperationException(
                $"NPORT-P filing {filing.AccessionNumber} has no issuer CIK to re-fetch from EDGAR."
            );

        var content = await _secEdgarClient.GetDocumentContent(filing.AccessionNumber, cik);
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException(
                $"EDGAR returned empty content for NPORT-P {filing.AccessionNumber}."
            );

        var filingData = new FilingData
        {
            Cik = cik,
            AccessionNumber = filing.AccessionNumber,
            FilingDate = filing.FilingDate,
            ReportDate = filing.ReportPeriodDate,
        };

        var root = await EdgarXmlSubmissionParser.TryParseSubmission(
            content,
            filingData,
            filing.CommonStock?.Ticker,
            "NPORT-P",
            "Nport.Reprocess",
            _logger,
            _errorReporter
        );
        if (root == null)
            throw new InvalidOperationException(
                $"NPORT-P {filing.AccessionNumber} content was not parseable XML."
            );

        var parsed = NportFilingProcessor.ParseEntity(root, filing.CommonStockId, filingData);
        if (parsed == null)
            throw new InvalidOperationException(
                $"NPORT-P {filing.AccessionNumber} is missing its genInfo section."
            );

        // Sweep-discovered (registrant-only) filings keep only positions in stocks we track — they
        // exist solely to answer the reverse "who holds this stock" lookup. Re-derive that same
        // filtered schedule so reprocess doesn't re-inflate the filing with the fund's full
        // portfolio of bonds, derivatives and untracked equities. A series opted into full fidelity
        // is the exception and keeps everything — re-narrowing it here would silently undo the
        // opt-in on the next parser-version bump. The freshly parsed id wins over the stored one so
        // a filing whose series id was never captured can still be recognised.
        var seriesId = string.IsNullOrEmpty(parsed.SeriesId) ? filing.SeriesId : parsed.SeriesId;
        var fullFidelity =
            !string.IsNullOrEmpty(seriesId) && fullFidelitySeriesIds.Contains(seriesId);

        var reparsedHoldings = parsed.Holdings;
        if (filing.CommonStockId == null && !fullFidelity)
        {
            var trackedCusips = await GetTrackedCusips();
            reparsedHoldings = parsed
                .Holdings.Where(h =>
                    !string.IsNullOrEmpty(h.Cusip) && trackedCusips.Contains(h.Cusip)
                )
                .ToList();
        }

        foreach (var holding in reparsedHoldings)
            holding.NportFilingId = filing.Id;

        // Refresh the header facts and stamp the version so the filing drops out of the work-set;
        // these travel in the same save as the replaced schedule.
        filing.RegistrantName = parsed.RegistrantName;
        filing.SeriesName = parsed.SeriesName;
        filing.SeriesId = parsed.SeriesId;
        filing.SeriesLei = parsed.SeriesLei;
        filing.ReportPeriodDate = parsed.ReportPeriodDate;
        filing.ReportPeriodEnd = parsed.ReportPeriodEnd;
        filing.TotalAssets = parsed.TotalAssets;
        filing.TotalLiabilities = parsed.TotalLiabilities;
        filing.NetAssets = parsed.NetAssets;
        filing.IsFinalFiling = parsed.IsFinalFiling;
        filing.ReportedHoldingCount = parsed.ReportedHoldingCount;
        filing.ParserVersion = NportFiling.CurrentParserVersion;
        filing.ReprocessAttempts = 0;

        await ReplaceSchedule(filing, reparsedHoldings, cancellationToken);

        return reparsedHoldings.Count;
    }

    // Replaces the stored schedule without ever materializing the old rows: the set-based delete
    // and the insert save commit together, so a failure rolls the filing back whole. The in-memory
    // provider (tests) supports neither ExecuteDelete nor transactions; its fallback removes the
    // tiny seeded schedule through the change tracker instead.
    private async Task ReplaceSchedule(
        NportFiling filing,
        List<NportHolding> reparsedHoldings,
        CancellationToken cancellationToken
    )
    {
        if (_dbContext.Database.IsRelational())
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );
            await _dbContext
                .Set<NportHolding>()
                .Where(h => h.NportFilingId == filing.Id)
                .ExecuteDeleteAsync(cancellationToken);
            // The parser never sets the inverse NportFiling reference, so AddRange cannot reach
            // (and mark Added) the untracked filing entity the parser built.
            _dbContext.Set<NportHolding>().AddRange(reparsedHoldings);
            await _filingRepository.SaveChanges();
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            var oldHoldings = await _dbContext
                .Set<NportHolding>()
                .Where(h => h.NportFilingId == filing.Id)
                .ToListAsync(cancellationToken);
            _dbContext.Set<NportHolding>().RemoveRange(oldHoldings);
            _dbContext.Set<NportHolding>().AddRange(reparsedHoldings);
            await _filingRepository.SaveChanges();
        }
    }

    // Persists a failed attempt against a freshly-loaded filing row — the failed graph was just
    // dropped from the change tracker, so the stale instance must not be reused. At the attempt
    // ceiling the filing is advanced to the current version, keeping whatever holdings it already
    // has, so it can't keep re-selecting itself. Guarded so a failure while recording (row deleted
    // concurrently, a save conflict) never aborts the run or masks the original exception.
    private async Task RecordFailedAttempt(
        Guid filingId,
        Exception ex,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var filing = await _filingRepository
                .GetAll()
                .FirstOrDefaultAsync(f => f.Id == filingId, cancellationToken);
            if (filing == null)
                return;

            filing.ReprocessAttempts++;
            if (filing.ReprocessAttempts >= MaxReprocessAttempts)
            {
                filing.ParserVersion = NportFiling.CurrentParserVersion;
                _logger.LogWarning(
                    ex,
                    "NPORT-P reprocess gave up on {AccessionNumber} after {Attempts} attempts; advancing it to the current version without re-deriving holdings",
                    filing.AccessionNumber,
                    filing.ReprocessAttempts
                );
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "NPORT-P reprocess failed for {AccessionNumber} (attempt {Attempts}); retrying next run",
                    filing.AccessionNumber,
                    filing.ReprocessAttempts
                );
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception recordEx)
            when (recordEx is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested
            )
        {
            _logger.LogWarning(
                recordEx,
                "NPORT-P reprocess could not record the failed attempt for filing {FilingId}",
                filingId
            );
        }
    }

    // The set of CUSIPs we track, loaded once per run and cached. Only consulted when a
    // sweep-discovered filing is re-derived, so it stays unloaded on runs without one.
    private async Task<HashSet<string>> GetTrackedCusips()
    {
        if (_trackedCusips != null)
            return _trackedCusips;

        var cusips = await _commonStockRepository
            .GetAll()
            .Where(c => c.Cusip != null && c.Cusip != "")
            .Select(c => c.Cusip)
            .ToListAsync();

        _trackedCusips = new HashSet<string>(cusips, StringComparer.OrdinalIgnoreCase);
        return _trackedCusips;
    }
}
