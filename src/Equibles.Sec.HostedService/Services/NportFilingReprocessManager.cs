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
/// </summary>
[Service]
public class NportFilingReprocessManager
{
    // Small batches so progress commits often: SaveChanges runs once per batch, so a throttled or
    // interrupted run persists what it managed to fetch rather than losing a large in-flight batch.
    private const int BatchSize = 32;

    private readonly NportFilingRepository _filingRepository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<NportFilingReprocessManager> _logger;

    public NportFilingReprocessManager(
        NportFilingRepository filingRepository,
        ISecEdgarClient secEdgarClient,
        EquiblesFinancialDbContext dbContext,
        ErrorReporter errorReporter,
        ILogger<NportFilingReprocessManager> logger
    )
    {
        _filingRepository = filingRepository;
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

        // The re-fetch + re-parse of a full backlog can run long; lift the per-command timeout so a
        // large batch's SaveChanges doesn't trip the default. Guarded because the timeout is a
        // relational-only facility (the in-memory provider used in tests rejects it).
        if (_dbContext.Database.IsRelational())
            _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        // No DB cursor: a reprocessed filing advances to the current version and drops out of the
        // filter, so each pass takes the next batch. Filings that fail this run are held in-memory
        // and excluded so the run still terminates; they're retried on the next run.
        var failedThisRun = new HashSet<Guid>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await _filingRepository
                .GetAll()
                .Where(f => f.ParserVersion < NportFiling.CurrentParserVersion)
                .Where(f => !failedThisRun.Contains(f.Id))
                .OrderBy(f => f.FilingDate)
                .Include(f => f.CommonStock)
                .Include(f => f.Holdings)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            var processedInBatch = 0;
            foreach (var filing in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                try
                {
                    await ReprocessFiling(filing, result);
                    processedInBatch++;
                }
                catch (Exception ex)
                {
                    // One bad filing (e.g. a transient EDGAR 429/timeout) must not abort the whole
                    // batch. Skip it this run; it's retried on the next.
                    _logger.LogWarning(
                        ex,
                        "NPORT-P reprocess failed for {AccessionNumber}; skipping this run",
                        filing.AccessionNumber
                    );
                    failedThisRun.Add(filing.Id);
                    result.Failed++;
                }
            }

            try
            {
                await _filingRepository.SaveChanges();
                result.Processed += processedInBatch;
            }
            catch (DbUpdateException ex)
            {
                // A concurrent ingest insert of the same filing or a similar conflict — drop the
                // batch's changes and retry these next run.
                _logger.LogWarning(ex, "NPORT-P reprocess batch save failed; retrying next run");
                foreach (var filing in batch)
                    failedThisRun.Add(filing.Id);
            }
            finally
            {
                _dbContext.ChangeTracker.Clear();
            }

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

    private async Task ReprocessFiling(NportFiling filing, NportFilingReprocessResult result)
    {
        var cik = filing.CommonStock?.Cik;
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

        // Replace the schedule of holdings and refresh the header facts, then stamp the version so
        // the filing drops out of the work-set.
        _dbContext.Set<NportHolding>().RemoveRange(filing.Holdings);
        foreach (var holding in parsed.Holdings)
            holding.NportFilingId = filing.Id;
        _dbContext.Set<NportHolding>().AddRange(parsed.Holdings);

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
        filing.ParserVersion = NportFiling.CurrentParserVersion;

        result.HoldingsAdded += parsed.Holdings.Count;
    }
}
