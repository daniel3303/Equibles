using System.Text;
using System.Xml.Linq;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.InsiderTrading.BusinessLogic.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Media.BusinessLogic;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Re-derives insider-transaction data for filings whose rows sit below
/// <see cref="InsiderTransaction.CurrentParserVersion"/>. For each such filing it
/// replays the parse from the cached ownership XML — fetching and caching the XML
/// from EDGAR the first time — then updates each row's authoritative
/// <see cref="InsiderTransaction.SecurityKind"/>, re-runs price validity from the
/// as-filed price, and stamps the current parser version.
///
/// The parser version is the single selector: once a filing's rows are stamped at
/// the current version they drop out, so the run terminates and is resumable —
/// an interrupted run continues where it left off on the next invocation. Bumping
/// <see cref="InsiderTransaction.CurrentParserVersion"/> after a parser change
/// re-enrolls every filing automatically.
/// </summary>
[Service]
public class InsiderFilingReprocessManager
{
    private const int BatchSize = 200;
    private const int CloseLookbackDays = 10;

    // After this many failed fetch/parse attempts a filing is marked NotPresent and
    // its rows are advanced to the current version, so a permanently-unfetchable
    // filing can't keep the run from terminating.
    private const int MaxCaptureAttempts = 3;

    private readonly InsiderTransactionRepository _transactionRepository;
    private readonly InsiderFilingRepository _filingRepository;
    private readonly DailyStockPriceRepository _dailyStockPriceRepository;
    private readonly InsiderTransactionPriceValidator _validator;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly IFileManager _fileManager;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ILogger<InsiderFilingReprocessManager> _logger;

    public InsiderFilingReprocessManager(
        InsiderTransactionRepository transactionRepository,
        InsiderFilingRepository filingRepository,
        DailyStockPriceRepository dailyStockPriceRepository,
        InsiderTransactionPriceValidator validator,
        ISecEdgarClient secEdgarClient,
        IFileManager fileManager,
        EquiblesFinancialDbContext dbContext,
        ILogger<InsiderFilingReprocessManager> logger
    )
    {
        _transactionRepository = transactionRepository;
        _filingRepository = filingRepository;
        _dailyStockPriceRepository = dailyStockPriceRepository;
        _validator = validator;
        _secEdgarClient = secEdgarClient;
        _fileManager = fileManager;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InsiderFilingReprocessResult> Run(
        Func<InsiderFilingReprocessResult, Task> onProgress = null,
        CancellationToken cancellationToken = default
    )
    {
        // Snapshot of the work-set for the progress bar. The live ingest worker may
        // stamp new rows at the current version while this runs; harmless — Processed
        // can briefly nudge past Total and self-corrects.
        var result = new InsiderFilingReprocessResult
        {
            Total = await _transactionRepository
                .GetAll()
                .Where(t => t.ParserVersion < InsiderTransaction.CurrentParserVersion)
                .Select(t => t.AccessionNumber)
                .Distinct()
                .CountAsync(),
        };

        if (result.Total == 0)
            return result;

        _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        // No DB cursor: a reprocessed filing's rows advance to the current version and
        // drop out of the filter, so each pass takes the next batch of unprocessed
        // accessions. Filings that fail this run are held in-memory and excluded so the
        // run still terminates; they're retried on the next run (string ordering would
        // be collation-dependent, so a textual keyset is deliberately avoided).
        var failedThisRun = new HashSet<string>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var accessions = await _transactionRepository
                .GetAll()
                .Where(t => t.ParserVersion < InsiderTransaction.CurrentParserVersion)
                .Where(t => !failedThisRun.Contains(t.AccessionNumber))
                .Select(t => t.AccessionNumber)
                .Distinct()
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (accessions.Count == 0)
                break;

            foreach (var accession in accessions)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                try
                {
                    await ReprocessFiling(accession, result);
                }
                catch (Exception ex)
                {
                    // One bad filing (e.g. a transient EDGAR 429/timeout) must not abort
                    // the whole batch. Skip it this run; it's retried on the next.
                    _logger.LogWarning(
                        ex,
                        "Insider filing reprocess failed for {AccessionNumber}; skipping this run",
                        accession
                    );
                    failedThisRun.Add(accession);
                    result.Failed++;
                }
            }

            try
            {
                await _transactionRepository.SaveChanges();
                result.Processed += accessions.Count - accessions.Count(failedThisRun.Contains);
            }
            catch (DbUpdateException ex)
            {
                // A concurrent ingest insert of the same filing (unique accession) or a
                // similar conflict — drop the batch's changes and retry these next run.
                _logger.LogWarning(
                    ex,
                    "Insider filing reprocess batch save failed; retrying next run"
                );
                foreach (var accession in accessions)
                    failedThisRun.Add(accession);
            }
            finally
            {
                _dbContext.ChangeTracker.Clear();
            }

            _logger.LogInformation(
                "Insider filing reprocess: {Processed}/{Total} filings, reclassified={Reclassified}, repaired={Repaired}, failed={Failed}",
                result.Processed,
                result.Total,
                result.Reclassified,
                result.Repaired,
                result.Failed
            );

            if (onProgress != null)
                await onProgress(result);
        }

        return result;
    }

    private async Task ReprocessFiling(string accession, InsiderFilingReprocessResult result)
    {
        // Eager-load the issuer so the (possible) EDGAR re-fetch reads CommonStock.Cik
        // without a per-filing lazy-load query.
        var rows = await _transactionRepository
            .GetByAccessionNumber(accession)
            .Include(t => t.CommonStock)
            .OrderBy(t => t.TransactionOrder)
            .ToListAsync();
        if (rows.Count == 0)
            return;

        var root = await GetOwnershipRoot(accession, rows, result);
        if (root == null)
            return;

        var first = rows[0];
        var filing = new FilingData
        {
            AccessionNumber = accession,
            FilingDate = first.FilingDate,
            ReportDate = first.TransactionDate,
        };

        // Re-parse in the same document order the ingest used; map back onto the
        // stored rows by TransactionOrder so a kind lands on the right row even if
        // the parsed and stored counts ever differ.
        var parsed = InsiderFilingParser.ParseTransactions(
            root,
            new InsiderOwner { Id = first.InsiderOwnerId },
            first.CommonStockId,
            filing,
            first.IsAmendment
        );
        // TransactionOrder is unique within a parse by construction, so a direct
        // dictionary is safe; a duplicate would be a parser bug worth surfacing.
        var parsedByOrder = parsed.ToDictionary(t => t.TransactionOrder);

        // The re-parse should reproduce the stored rows exactly. If the counts
        // diverge, some stored rows won't map to a parsed row — they keep their
        // prior data but are still advanced to the current version. Rare, but log
        // it so the assumption is observable across a full backlog reprocess.
        if (rows.Count != parsed.Count)
        {
            _logger.LogWarning(
                "Insider reprocess: {AccessionNumber} has {StoredCount} stored rows but re-parsed {ParsedCount}; unmatched rows keep prior data",
                accession,
                rows.Count,
                parsed.Count
            );
        }

        var closes = await FetchCloses(first.CommonStockId, rows);

        foreach (var row in rows)
        {
            if (parsedByOrder.TryGetValue(row.TransactionOrder, out var reparsed))
            {
                if (row.SecurityKind != reparsed.SecurityKind)
                {
                    row.SecurityKind = reparsed.SecurityKind;
                    result.Reclassified++;
                }
                // Re-derive footnotes (added in parser v2); cheap to always copy.
                row.Notes = reparsed.Notes;
            }

            decimal? close = closes.TryGetValue(row.TransactionDate, out var value) ? value : null;

            var evaluation = _validator.Evaluate(
                row.ReportedPricePerShare,
                row.Shares,
                row.SecurityKind,
                row.SecurityTitle,
                close
            );
            row.PricePerShare = evaluation.EffectivePrice;
            row.IsPriceValid = evaluation.IsPriceValid;
            if (evaluation.WasRepaired)
                result.Repaired++;

            row.ParserVersion = InsiderTransaction.CurrentParserVersion;
        }
    }

    // Returns the parsed ownership root for a filing — from the cached XML when
    // present, otherwise fetched from EDGAR (and cached). On failure records an
    // attempt and, past the retry ceiling, gives up: the filing is marked
    // NotPresent and its rows are advanced so the run can terminate.
    private async Task<XElement> GetOwnershipRoot(
        string accession,
        List<InsiderTransaction> rows,
        InsiderFilingReprocessResult result
    )
    {
        // Eager-load the cached blob so a cache hit is a single query, not three lazy ones.
        var filing = await _filingRepository
            .GetByAccessionNumber(accession)
            .Include(f => f.Content)
                .ThenInclude(c => c.FileContent)
            .FirstOrDefaultAsync();

        if (filing is { CaptureStatus: InsiderFilingCaptureStatus.Captured, ContentId: not null })
        {
            var raw = GzipCompressor.Decompress(filing.Content.FileContent.Bytes);
            var cachedRoot = InsiderFilingParser.TryGetOwnershipRoot(Encoding.UTF8.GetString(raw));
            if (cachedRoot != null)
                return cachedRoot;
            // Cached blob is corrupt/unparseable — fall through to a fresh re-fetch rather
            // than returning null forever (which would re-select this filing every run).
        }

        var issuerCik = rows[0].CommonStock?.Cik;
        if (!string.IsNullOrEmpty(issuerCik))
        {
            var fetched = await _secEdgarClient.GetDocumentContent(accession, issuerCik);
            var root = InsiderFilingParser.TryGetOwnershipRoot(fetched);
            if (root != null)
            {
                result.Fetched++;
                await CacheFiling(filing, accession, root);
                return root;
            }
        }

        RecordFailure(filing, accession, rows, result);
        return null;
    }

    private async Task CacheFiling(InsiderFiling filing, string accession, XElement root)
    {
        var rawBytes = Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting));
        var compressed = GzipCompressor.Compress(rawBytes);
        var file = await _fileManager.SaveInternalFile(
            compressed,
            accession,
            "gz",
            "application/gzip"
        );

        if (filing == null)
        {
            _filingRepository.Add(
                new InsiderFiling
                {
                    AccessionNumber = accession,
                    Content = file,
                    UncompressedSize = rawBytes.Length,
                    CaptureStatus = InsiderFilingCaptureStatus.Captured,
                }
            );
        }
        else
        {
            filing.Content = file;
            filing.UncompressedSize = rawBytes.Length;
            filing.CaptureStatus = InsiderFilingCaptureStatus.Captured;
        }
    }

    private void RecordFailure(
        InsiderFiling filing,
        string accession,
        List<InsiderTransaction> rows,
        InsiderFilingReprocessResult result
    )
    {
        result.Failed++;

        if (filing == null)
        {
            filing = new InsiderFiling
            {
                AccessionNumber = accession,
                CaptureStatus = InsiderFilingCaptureStatus.NotChecked,
            };
            _filingRepository.Add(filing);
        }

        filing.CaptureAttempts++;
        if (filing.CaptureAttempts < MaxCaptureAttempts)
            return;

        // Out of retries — stop selecting this filing so the run can finish. Its
        // rows keep their existing (possibly Unknown) classification.
        filing.CaptureStatus = InsiderFilingCaptureStatus.NotPresent;
        foreach (var row in rows)
            row.ParserVersion = InsiderTransaction.CurrentParserVersion;
    }

    private async Task<Dictionary<DateOnly, decimal>> FetchCloses(
        Guid stockId,
        List<InsiderTransaction> rows
    )
    {
        var minDate = rows.Min(r => r.TransactionDate).AddDays(-CloseLookbackDays);
        var maxDate = rows.Max(r => r.TransactionDate);

        var prices = await _dailyStockPriceRepository
            .GetAll()
            .Where(p => p.CommonStockId == stockId && p.Date >= minDate && p.Date <= maxDate)
            .Select(p => new { p.Date, p.Close })
            .OrderByDescending(p => p.Date)
            .ToListAsync();

        var result = new Dictionary<DateOnly, decimal>();
        foreach (var row in rows)
        {
            if (result.ContainsKey(row.TransactionDate))
                continue;
            var match = prices.FirstOrDefault(p => p.Date <= row.TransactionDate);
            if (match != null)
                result[row.TransactionDate] = match.Close;
        }
        return result;
    }
}
