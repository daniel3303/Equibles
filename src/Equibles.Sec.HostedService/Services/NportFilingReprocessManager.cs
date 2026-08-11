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
    // Cadence of the progress log line; each filing commits individually, so this is presentation
    // only.
    private const int ProgressLogInterval = 32;

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

    public async Task<NportFilingReprocessResult> Run(CancellationToken cancellationToken = default)
    {
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
            var filing = await _filingRepository
                .GetAll()
                .Where(f => f.ParserVersion < NportFiling.CurrentParserVersion)
                .Where(f => !failedThisRun.Contains(f.Id))
                .OrderBy(f => f.FilingDate)
                .Include(f => f.CommonStock)
                .FirstOrDefaultAsync(cancellationToken);

            if (filing == null)
                break;

            try
            {
                result.HoldingsAdded += await ReprocessFiling(filing, cancellationToken);
                result.Processed++;
            }
            catch (DbUpdateException ex)
            {
                // A concurrent ingest insert of the same filing or a similar conflict — the
                // filing's replace rolled back; retry it next run without burning an attempt.
                _logger.LogWarning(
                    ex,
                    "NPORT-P reprocess save failed for {AccessionNumber}; retrying next run",
                    filing.AccessionNumber
                );
                failedThisRun.Add(filing.Id);
                result.Failed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad filing (e.g. a transient EDGAR 429/timeout) must not abort the run.
                // Drop whatever partial graph the failure left tracked, then record the attempt
                // on a freshly-loaded row; at the ceiling the filing is stamped current so it
                // stops re-selecting itself forever.
                _dbContext.ChangeTracker.Clear();
                await RecordFailedAttempt(filing.Id, ex);
                failedThisRun.Add(filing.Id);
                result.Failed++;
            }
            finally
            {
                // Release the filing's tracked graph before selecting the next one.
                _dbContext.ChangeTracker.Clear();
            }

            if ((result.Processed + result.Failed) % ProgressLogInterval == 0)
                _logger.LogInformation(
                    "NPORT-P reprocess: {Processed}/{Total} filings, holdings added={HoldingsAdded}, failed={Failed}",
                    result.Processed,
                    result.Total,
                    result.HoldingsAdded,
                    result.Failed
                );
        }

        return result;
    }

    // Returns the number of holdings parsed onto the filing. Throws on any fetch/parse failure so
    // the caller can record the attempt and retry the filing on a later run. The EDGAR fetch and
    // parse run before any database write, so a failure never leaves the filing half-replaced.
    private async Task<int> ReprocessFiling(NportFiling filing, CancellationToken cancellationToken)
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
        // portfolio of bonds, derivatives and untracked equities.
        var reparsedHoldings = parsed.Holdings;
        if (filing.CommonStockId == null)
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

        // Replace the schedule without ever materializing the old rows: the set-based delete and
        // the insert save commit together, so a failure rolls the filing back whole. The in-memory
        // provider (tests) supports neither ExecuteDelete nor transactions; its fallback removes
        // the tiny seeded schedule through the change tracker instead.
        if (_dbContext.Database.IsRelational())
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );
            await _dbContext
                .Set<NportHolding>()
                .Where(h => h.NportFilingId == filing.Id)
                .ExecuteDeleteAsync(cancellationToken);
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

        return reparsedHoldings.Count;
    }

    // Persists a failed attempt against a freshly-loaded filing row — the failed graph was just
    // dropped from the change tracker, so the stale instance must not be reused. At the attempt
    // ceiling the filing is advanced to the current version, keeping whatever holdings it already
    // has, so it can't keep re-selecting itself.
    private async Task RecordFailedAttempt(Guid filingId, Exception ex)
    {
        var filing = await _filingRepository.GetAll().FirstAsync(f => f.Id == filingId);

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

        await _filingRepository.SaveChanges();
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
