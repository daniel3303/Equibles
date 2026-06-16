using System.Globalization;
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
/// Discovers NPORT-P portfolio reports across <em>all</em> EDGAR filers — not just tracked issuers —
/// by sweeping the daily index, so the giant multi-series fund-family trusts ("Vanguard Index
/// Funds", "Fidelity Concord Street Trust", "iShares Trust") that file one report per series under a
/// single non-listed registrant are no longer missed. These trusts are not tracked stocks, so the
/// issuer-feed crawler never reaches them; without this sweep their funds — the largest holders of
/// the most widely held stocks — never appear under "held by funds".
///
/// Each swept filing keeps only the positions in stocks we already track (matched by CUSIP, the
/// authoritative identifier — never name text); the rest of the fund's portfolio is dropped because
/// the only consumer is the reverse "who holds this stock" lookup. A registrant that is itself a
/// tracked stock is left to the issuer-feed crawler, which stores its reports at full fidelity, so
/// the two paths never produce the same series twice.
/// </summary>
[Service]
public class NportRealtimeIngestionService
{
    // Commit progress often so a throttled or interrupted cycle keeps what it managed to fetch.
    private const int BatchSize = 50;

    private readonly ISecEdgarClient _edgarClient;
    private readonly CommonStockRepository _commonStockRepository;
    private readonly NportFilingRepository _nportFilingRepository;
    private readonly ProcessedNportFilingRepository _processedRepository;
    private readonly EquiblesFinancialDbContext _dbContext;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<NportRealtimeIngestionService> _logger;

    // NPORT-P and NPORT-P/A both start with this prefix in the daily index.
    private static readonly string[] NportFormPrefixes = ["NPORT-P"];

    public NportRealtimeIngestionService(
        ISecEdgarClient edgarClient,
        CommonStockRepository commonStockRepository,
        NportFilingRepository nportFilingRepository,
        ProcessedNportFilingRepository processedRepository,
        EquiblesFinancialDbContext dbContext,
        ErrorReporter errorReporter,
        ILogger<NportRealtimeIngestionService> logger
    )
    {
        _edgarClient = edgarClient;
        _commonStockRepository = commonStockRepository;
        _nportFilingRepository = nportFilingRepository;
        _processedRepository = processedRepository;
        _dbContext = dbContext;
        _errorReporter = errorReporter;
        _logger = logger;
    }

    /// <summary>
    /// Sweeps the last <paramref name="lookbackDays"/> days of EDGAR's daily index (inclusive of
    /// <paramref name="today"/>) for NPORT-P submissions, ingesting the trust-only ones that hold a
    /// stock we track. At most <paramref name="maxFetchesPerCycle"/> submissions are downloaded per
    /// cycle; when more remain the result flags it so the worker drains the rest promptly.
    /// </summary>
    public async Task<NportRealtimeResult> IngestRecentFilings(
        DateOnly today,
        int lookbackDays,
        int maxFetchesPerCycle,
        CancellationToken cancellationToken
    )
    {
        var trackedCusips = await LoadTrackedCusips(cancellationToken);
        if (trackedCusips.Count == 0)
        {
            _logger.LogInformation(
                "NPORT-P sweep: no tracked stock CUSIPs yet; nothing to match. Retrying soon."
            );
            return new NportRealtimeResult(0, 0, MoreWorkQueued: false, NotReady: true);
        }

        var trackedCiks = await LoadTrackedCiks(cancellationToken);

        var entries = await DiscoverEntries(today, lookbackDays, cancellationToken);
        if (entries.Count == 0)
        {
            _logger.LogInformation("NPORT-P sweep: no submissions in the daily-index window");
            return new NportRealtimeResult(0, 0, MoreWorkQueued: false, NotReady: false);
        }

        var candidates = await SelectCandidates(entries, cancellationToken);
        if (candidates.Count == 0)
            return new NportRealtimeResult(0, 0, MoreWorkQueued: false, NotReady: false);

        var stored = 0;
        var fetched = 0;
        var moreWorkQueued = false;
        var pending = 0;

        foreach (var entry in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A registrant that is itself a tracked stock is crawled at full fidelity through the
            // issuer feed; record it so the sweep doesn't re-examine it, and never download it.
            if (trackedCiks.Contains(NormalizeCik(entry.Cik)))
            {
                RecordSkipped(entry.AccessionNumber);
                pending++;
            }
            else
            {
                if (fetched >= maxFetchesPerCycle)
                {
                    moreWorkQueued = true;
                    break;
                }

                fetched++;
                var outcome = await ProcessTrustFiling(entry, trackedCusips, cancellationToken);
                if (outcome == FilingOutcome.Stored)
                    stored++;
                if (outcome != FilingOutcome.TransientFailure)
                    pending++;
            }

            if (pending >= BatchSize)
            {
                await FlushBatch(cancellationToken);
                pending = 0;
            }
        }

        await FlushBatch(cancellationToken);

        _logger.LogInformation(
            "NPORT-P sweep cycle complete: {Stored} trust filings stored, {Fetched} submissions examined{More}",
            stored,
            fetched,
            moreWorkQueued ? " (more queued)" : string.Empty
        );

        return new NportRealtimeResult(stored, fetched, moreWorkQueued, NotReady: false);
    }

    private enum FilingOutcome
    {
        // Parsed and stored as a trust-only filing (with or without tracked holdings).
        Stored,

        // Examined and deliberately not stored (no tracked holdings, unseen series, or malformed) —
        // recorded so it is not re-downloaded.
        SkippedRecorded,

        // Fetch returned nothing / threw after the client's own retries — left unrecorded so a later
        // cycle retries it until it ages out of the sweep window.
        TransientFailure,
    }

    private async Task<FilingOutcome> ProcessTrustFiling(
        EdgarDailyIndexEntry entry,
        HashSet<string> trackedCusips,
        CancellationToken cancellationToken
    )
    {
        string content;
        try
        {
            content = await _edgarClient.GetDocumentContent(entry.AccessionNumber, entry.Cik);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One submission failing must not abort the sweep; leave it unrecorded so it retries.
            _logger.LogWarning(
                ex,
                "NPORT-P sweep: failed to fetch {Accession} (CIK {Cik}); will retry",
                entry.AccessionNumber,
                entry.Cik
            );
            return FilingOutcome.TransientFailure;
        }

        if (string.IsNullOrWhiteSpace(content))
            return FilingOutcome.TransientFailure;

        var filingData = new FilingData
        {
            Cik = entry.Cik,
            AccessionNumber = entry.AccessionNumber,
            FilingDate = entry.DateFiled,
            ReportDate = entry.DateFiled,
            Form = entry.FormType,
        };

        var root = await EdgarXmlSubmissionParser.TryParseSubmission(
            content,
            filingData,
            companyTicker: null,
            "NPORT-P",
            "Nport.Sweep",
            _logger,
            _errorReporter
        );
        if (root == null)
        {
            // Non-empty content that is not parseable XML is deterministic — record it so the sweep
            // does not re-download it every cycle.
            RecordSkipped(entry.AccessionNumber);
            return FilingOutcome.SkippedRecorded;
        }

        var filing = NportFilingProcessor.ParseEntity(root, companyId: null, filingData);
        if (filing == null)
        {
            RecordSkipped(entry.AccessionNumber);
            return FilingOutcome.SkippedRecorded;
        }

        filing.RegistrantCik = Truncate(entry.Cik, 16);

        var trackedHoldings = filing
            .Holdings.Where(h => !string.IsNullOrEmpty(h.Cusip) && trackedCusips.Contains(h.Cusip))
            .ToList();

        // Store the report when it holds one of our stocks, or when this series has been stored
        // before — so a later report that exited every tracked position still advances the series'
        // latest report and the exited position stops reading as current. Otherwise the registrant
        // holds nothing we track and never has: record it and move on.
        if (trackedHoldings.Count == 0 && !await IsSeriesKnown(filing, cancellationToken))
        {
            RecordSkipped(entry.AccessionNumber);
            return FilingOutcome.SkippedRecorded;
        }

        filing.Holdings = trackedHoldings;
        _nportFilingRepository.Add(filing);
        return FilingOutcome.Stored;
    }

    private Task<bool> IsSeriesKnown(NportFiling filing, CancellationToken cancellationToken) =>
        _nportFilingRepository
            .GetByRegistrantCikAndSeries(filing.RegistrantCik, filing.SeriesId)
            .AnyAsync(cancellationToken);

    private void RecordSkipped(string accessionNumber) =>
        _processedRepository.Add(new ProcessedNportFiling { AccessionNumber = accessionNumber });

    // Commits the staged filings/skip-records and clears the tracker so the cycle's memory stays
    // flat. A batch conflict (e.g. a concurrent issuer-feed insert of the same accession) drops only
    // that batch — those candidates are simply re-examined next cycle.
    private async Task FlushBatch(CancellationToken cancellationToken)
    {
        if (!_dbContext.ChangeTracker.HasChanges())
            return;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "NPORT-P sweep: batch save failed; re-examining next cycle");
        }
        finally
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    // The distinct candidate set: daily-index NPORT-P entries we have neither stored nor skipped
    // before, oldest first so a capped cycle drains the backlog in filing order.
    private async Task<List<EdgarDailyIndexEntry>> SelectCandidates(
        List<EdgarDailyIndexEntry> entries,
        CancellationToken cancellationToken
    )
    {
        var accessions = entries.Select(e => e.AccessionNumber).ToList();

        var stored = await _nportFilingRepository
            .GetByAccessionNumbers(accessions)
            .Select(f => f.AccessionNumber)
            .ToListAsync(cancellationToken);
        var skipped = await _processedRepository
            .GetByAccessionNumbers(accessions)
            .Select(p => p.AccessionNumber)
            .ToListAsync(cancellationToken);

        var known = new HashSet<string>(stored, StringComparer.OrdinalIgnoreCase);
        known.UnionWith(skipped);

        return entries
            .Where(e => !known.Contains(e.AccessionNumber))
            .OrderBy(e => e.DateFiled)
            .ThenBy(e => e.AccessionNumber, StringComparer.Ordinal)
            .ToList();
    }

    // Walks the trailing window of daily indexes, deduping by accession across overlapping days. A
    // day that fails to fetch is logged and skipped — the full window is re-swept each cycle, so a
    // transient failure self-heals next time without a watermark.
    private async Task<List<EdgarDailyIndexEntry>> DiscoverEntries(
        DateOnly today,
        int lookbackDays,
        CancellationToken cancellationToken
    )
    {
        var byAccession = new Dictionary<string, EdgarDailyIndexEntry>(
            StringComparer.OrdinalIgnoreCase
        );

        for (var offset = 0; offset < lookbackDays; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var date = today.AddDays(-offset);

            List<EdgarDailyIndexEntry> dayEntries;
            try
            {
                dayEntries = await _edgarClient.GetDailyIndexForForms(
                    date,
                    NportFormPrefixes,
                    cancellationToken
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "NPORT-P sweep: failed to fetch the {Date:yyyy-MM-dd} daily index; skipping this day",
                    date
                );
                continue;
            }

            foreach (var entry in dayEntries)
            {
                if (!string.IsNullOrEmpty(entry.AccessionNumber))
                    byAccession[entry.AccessionNumber] = entry;
            }
        }

        return byAccession.Values.ToList();
    }

    private async Task<HashSet<string>> LoadTrackedCusips(CancellationToken cancellationToken)
    {
        var cusips = await _commonStockRepository
            .GetAll()
            .Where(c => c.Cusip != null && c.Cusip != "")
            .Select(c => c.Cusip)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(cusips, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HashSet<string>> LoadTrackedCiks(CancellationToken cancellationToken)
    {
        var rows = await _commonStockRepository
            .GetAll()
            .Select(c => new { c.Cik, c.SecondaryCiks })
            .ToListAsync(cancellationToken);

        var ciks = new HashSet<string>();
        foreach (var row in rows)
        {
            if (!string.IsNullOrEmpty(row.Cik))
                ciks.Add(NormalizeCik(row.Cik));

            foreach (var secondary in row.SecondaryCiks)
            {
                if (!string.IsNullOrEmpty(secondary))
                    ciks.Add(NormalizeCik(secondary));
            }
        }

        return ciks;
    }

    // EDGAR's daily index reports CIKs without leading zeros; CommonStock may store them padded.
    // Normalise both sides to the unpadded numeric form so the membership test never misses.
    private static string NormalizeCik(string cik)
    {
        if (string.IsNullOrEmpty(cik))
            return cik;
        return long.TryParse(cik, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n.ToString(CultureInfo.InvariantCulture)
            : cik.TrimStart('0');
    }

    private static string Truncate(string value, int maxLength) =>
        value != null && value.Length > maxLength ? value[..maxLength] : value;
}
