using System.Globalization;
using System.IO.Compression;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Core.Extensions;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Extensions;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.Repositories;
using Equibles.Messaging.Contracts.Holdings;
using FlexLabs.EntityFrameworkCore.Upsert;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Equibles.Holdings.HostedService.Services.HoldingsParsingHelper;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsImportService
{
    private const string AccessionNumberColumn = "ACCESSION_NUMBER";

    // Matches UnmappedCusip.IssuerName's column width; filers occasionally file a name longer
    // than the column, and the name is only ever a human-facing hint.
    private const int MaxIssuerNameLength = 256;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldingsImportService> _logger;
    private readonly WorkerOptions _workerOptions;
    private readonly IStockPriceProvider _stockPriceProvider;
    private readonly IBus _bus;

    public HoldingsImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldingsImportService> logger,
        IOptions<WorkerOptions> workerOptions,
        IStockPriceProvider stockPriceProvider,
        IBus bus
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _workerOptions = workerOptions.Value;
        _stockPriceProvider = stockPriceProvider;
        _bus = bus;
    }

    public virtual Task<ImportResult> ImportDataSet(
        ZipArchive archive,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    ) => ImportDataSet(archive, minReportDate, TimeSpan.Zero, cancellationToken);

    public virtual async Task<ImportResult> ImportDataSet(
        ZipArchive archive,
        DateOnly minReportDate,
        TimeSpan batchPause,
        CancellationToken cancellationToken
    )
    {
        var context = new ImportContext
        {
            TsvParser = new TsvParser(),
            Archive = archive,
            MinReportDate = minReportDate,
            BatchPause = batchPause,
        };

        var parseResult = await ParseSubmissions(context, cancellationToken);
        if (parseResult == null)
            return new ImportResult(0, IsComplete: false);
        if (parseResult == false)
            return new ImportResult(0, IsComplete: true);
        // Cover pages must parse BEFORE the dedup: whether a later filing supersedes
        // its original depends on the amendment type, which lives on the cover page.
        if (!await ParseCoverPages(context, cancellationToken))
            return new ImportResult(context.Submissions.Count, IsComplete: false);
        DeduplicateSubmissions(context);
        var submissionCount = context.Submissions.Count;
        await ParseSummaryPages(context, cancellationToken);
        var cusipResult = await BuildCusipMapping(context, cancellationToken);
        if (cusipResult == CusipMappingOutcome.NoInfoTable)
            // Structural: a missing INFOTABLE.tsv won't appear on re-download —
            // terminal, mark processed so we don't loop on a broken archive.
            return new ImportResult(submissionCount, IsComplete: true);
        if (cusipResult == CusipMappingOutcome.NoTrackedStocks)
            // No tracked stock mapped — typically a cold start where the FTD
            // scraper hasn't seeded CUSIPs yet. NOT terminal: leave the data
            // set unprocessed so a later cycle backfills it once CUSIPs exist.
            return new ImportResult(submissionCount, IsComplete: false);
        await BuildPriceMap(context, cancellationToken);
        await BuildSplitMap(context, cancellationToken);
        await ParseOtherManagers(context, cancellationToken);
        await ParseOtherManagerCoverList(context, cancellationToken);
        await UpsertInstitutionalHolders(context, cancellationToken);
        await HandleAmendments(context, cancellationToken);
        var holdingsResult = await StreamAndInsertHoldings(context, cancellationToken);
        await FlushFilingOtherManagers(context, cancellationToken);
        if (holdingsResult.SkippedStaleParent)
        {
            // CompanySync replaced at least one mapped CommonStock after the CUSIP lookup. Keep
            // the valid positions already written, but leave this data set eligible for retry so
            // the replacement stock is resolved on the next pass. Publishing summaries here
            // would advertise a knowingly partial portfolio before that retry completes.
            return new ImportResult(
                submissionCount,
                IsComplete: false,
                InsertedHoldings: holdingsResult.Inserted
            );
        }
        await SyncFilingSummaries(context, cancellationToken);
        await PublishAffectedQuartersAsync(context, cancellationToken);
        return new ImportResult(
            submissionCount,
            IsComplete: true,
            InsertedHoldings: holdingsResult.Inserted
        );
    }

    // Bulk data sets routinely import many quarters at once, so group submissions
    // by ReportDate and publish one Filings13FImported per distinct quarter. The
    // consumer rebuilds the per-quarter AUM + sector snapshots; the work is
    // bounded per event, and deduplicating here keeps the import path from
    // stampeding the consumer with one event per filing.
    private async Task PublishAffectedQuartersAsync(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var byQuarter = new Dictionary<DateOnly, int>();
        foreach (var submission in context.Submissions.Values)
        {
            // 13F submissions only: a Schedule 13D/G carries an event DATE, not a
            // quarter end. Publishing it would seed a stub AumQuarterlySnapshot
            // (and downstream sector/activity snapshot rows) keyed to that
            // arbitrary day, polluting every market-wide quarterly surface.
            if (submission.FormType.ToHoldingsFilingType() != FilingType.Form13F)
                continue;

            if (TryParseDateOnly(submission.PeriodOfReport, out var reportDate))
            {
                byQuarter[reportDate] = byQuarter.GetValueOrDefault(reportDate) + 1;
            }
        }

        if (byQuarter.Count == 0)
        {
            return;
        }

        foreach (var (reportDate, count) in byQuarter)
        {
            await _bus.Publish(new Filings13FImported(reportDate, count), cancellationToken);
        }

        _logger.LogInformation(
            "Published Filings13FImported for {Quarters} distinct quarter(s)",
            byQuarter.Count
        );
    }

    // Looks up a required archive entry, warning once when it is absent so callers only branch on null.
    private ZipArchiveEntry FindRequiredEntry(ImportContext context, string fileName)
    {
        var entry = FindEntry(context.Archive, fileName);
        if (entry == null)
            _logger.LogWarning("{FileName} not found in archive", fileName);
        return entry;
    }

    /// <summary>
    /// Returns null if SUBMISSION.tsv is missing (structural failure).
    /// Returns false if no 13F-HR submissions match filters (legitimate empty).
    /// Returns true if submissions were parsed successfully.
    /// </summary>
    private async Task<bool?> ParseSubmissions(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var submissionEntry = FindRequiredEntry(context, "SUBMISSION.tsv");
        if (submissionEntry == null)
            return null;

        var submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in context.TsvParser.ParseEntry(submissionEntry))
        {
            if (TryParseSubmissionRow(row, context.MinReportDate, out var submission))
                submissions[submission.AccessionNumber] = submission;
        }

        if (submissions.Count == 0)
        {
            _logger.LogInformation("No supported submissions found in data set");
            return false;
        }

        _logger.LogInformation("Found {Count} submissions", submissions.Count);
        context.Submissions = submissions;
        return true;
    }

    private static bool TryParseSubmissionRow(
        Dictionary<string, string> row,
        DateOnly minReportDate,
        out SubmissionRow submission
    )
    {
        submission = null;

        var formType = GetValue(row, "SUBMISSIONTYPE");
        // Accept every form the holdings pipeline ingests (13F-HR, and the
        // beneficial-ownership Schedules 13D/13G), keyed off the shared map.
        if (formType.ToHoldingsFilingType() is null)
            return false;

        var accession = GetValue(row, AccessionNumberColumn);
        if (string.IsNullOrWhiteSpace(accession))
            return false;

        var periodOfReport = GetValue(row, "PERIODOFREPORT");
        if (
            TryParseDateOnly(periodOfReport, out var reportDateCheck)
            && reportDateCheck < minReportDate
        )
            return false;

        submission = new SubmissionRow
        {
            AccessionNumber = accession,
            FilingDate = GetValue(row, "FILING_DATE"),
            PeriodOfReport = periodOfReport,
            FormType = formType,
            Cik = GetValue(row, "CIK")?.TrimStart('0'),
        };
        return true;
    }

    internal static void DeduplicateSubmissions(ImportContext context)
    {
        var superseded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byCikAndPeriod = context
            .Submissions.Values.Where(s =>
                !string.IsNullOrWhiteSpace(s.Cik) && !string.IsNullOrWhiteSpace(s.PeriodOfReport)
            )
            .GroupBy(s => $"{s.Cik}|{s.PeriodOfReport}")
            .Where(g => g.Count() > 1);

        foreach (var group in byCikAndPeriod)
        {
            // FilingDate must be compared as a parsed date, not as a string: the
            // bulk datasets carry SEC `dd-MMM-yyyy` values ("14-FEB-2025"), so an
            // ordinal string sort compares the day-of-month first and orders
            // "29-JAN-2025" after "14-FEB-2025", dropping the genuinely later
            // amendment in favour of its original whenever the pair spans a month.
            // FilingDate is day-granular, so an original and its same-day amendment
            // can still tie on the parsed date; break those ties by accession
            // number — SEC assigns these monotonically per filer agent, so the
            // lexicographically greatest accession is the later submission.
            var ordered = group
                .OrderBy(s =>
                    TryParseDateOnly(s.FilingDate, out var filingDate)
                        ? filingDate
                        : DateOnly.MinValue
                )
                .ThenBy(s => s.AccessionNumber, StringComparer.Ordinal)
                .ToList();

            // A submission is superseded only by a later filing that REPLACES the
            // whole book: the newest original or RESTATEMENT amendment is the base,
            // and everything before it is dropped. A "NEW HOLDINGS" amendment only
            // ADDS positions, so it supersedes nothing — dropping its original
            // discarded the filer's entire book from every bulk import (all three
            // restructured Vanguard entities filed one for 2026-03-31, and the bulk
            // re-import could never heal what the realtime pass had missed —
            // EquiblesCommercial#7163). An amendment without a typed cover page
            // keeps the historical behaviour and counts as a restatement, matching
            // HandleAmendments' delete rule.
            var baseIndex = ordered.FindLastIndex(s =>
                !IsNewHoldingsAmendment(s.AccessionNumber, context)
            );
            if (baseIndex <= 0)
                continue;

            foreach (var s in ordered.Take(baseIndex))
            {
                superseded.Add(s.AccessionNumber);
            }
        }

        foreach (var accession in superseded)
        {
            context.Submissions.Remove(accession);
        }
    }

    /// <summary>
    /// Parses SUMMARYPAGE.tsv — the filer's own declared totals (<c>tableEntryTotal</c> /
    /// <c>tableValueTotal</c>) — into <see cref="ImportContext.SummaryPages"/>, normalising the
    /// declared value to whole dollars by the filing's era (pre-2023 filings declare thousands,
    /// exactly like the per-position value column). Optional: an archive without the section, or
    /// a 13F-NT row with empty cells, simply contributes nothing, and the filing rollup keeps
    /// null declared figures — a missing declaration is honest, an invented one is not.
    /// </summary>
    private async Task ParseSummaryPages(ImportContext context, CancellationToken cancellationToken)
    {
        var entry = FindEntry(context.Archive, "SUMMARYPAGE.tsv");
        if (entry == null)
        {
            _logger.LogInformation("No SUMMARYPAGE.tsv in this archive; declared totals stay null");
            return;
        }

        var parsed = 0;
        await foreach (var row in context.TsvParser.ParseEntry(entry))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accession = GetValue(row, AccessionNumberColumn);
            if (
                string.IsNullOrEmpty(accession)
                || !context.Submissions.TryGetValue(accession, out var submission)
            )
                continue;

            TryParseDateOnly(submission.FilingDate, out var filingDate);

            int? entryTotal = null;
            if (int.TryParse(GetValue(row, "TABLEENTRYTOTAL"), out var entries) && entries >= 0)
                entryTotal = entries;

            long? valueTotal = null;
            if (long.TryParse(GetValue(row, "TABLEVALUETOTAL"), out var declared) && declared > 0)
            {
                var dollars = FiledValueScale.ToDollars(declared, filingDate);
                if (dollars <= long.MaxValue)
                    valueTotal = (long)dollars;
            }

            if (entryTotal.HasValue || valueTotal.HasValue)
            {
                context.SummaryPages[accession] = (entryTotal, valueTotal);
                parsed++;
            }
        }

        _logger.LogInformation("Parsed {Count} summary pages with declared totals", parsed);
    }

    private async Task<bool> ParseCoverPages(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var coverPageEntry = FindRequiredEntry(context, "COVERPAGE.tsv");
        if (coverPageEntry == null)
            return false;

        var coverPages = new Dictionary<string, CoverPageRow>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in context.TsvParser.ParseEntry(coverPageEntry))
        {
            if (TryParseCoverPageRow(row, context.Submissions, out var coverPage))
                coverPages[coverPage.AccessionNumber] = coverPage;
        }

        _logger.LogInformation("Parsed {Count} cover pages", coverPages.Count);
        context.CoverPages = coverPages;
        return true;
    }

    private static bool TryParseCoverPageRow(
        Dictionary<string, string> row,
        Dictionary<string, SubmissionRow> submissions,
        out CoverPageRow coverPage
    )
    {
        coverPage = null;
        string Get(string field) => GetValue(row, field);

        var accession = Get(AccessionNumberColumn);
        if (string.IsNullOrEmpty(accession) || !submissions.ContainsKey(accession))
            return false;

        coverPage = new CoverPageRow
        {
            AccessionNumber = accession,
            IsAmendment = Get("ISAMENDMENT"),
            AmendmentType = Get("AMENDMENTTYPE"),
            CompanyName = Get("FILINGMANAGER_NAME"),
            City = Get("FILINGMANAGER_CITY"),
            StateOrCountry = Get("FILINGMANAGER_STATEORCOUNTRY"),
            Form13FFileNumber = Get("FORM13FFILENUMBER"),
            CrdNumber = Get("CRDNUMBER"),
            ConfidentialTreatment = Get("CONFIDENTIALTREATMENT"),
        };
        return true;
    }

    // Distinguishes a terminal structural failure (missing INFOTABLE) from a
    // recoverable "no tracked CUSIPs yet" so the caller can decide whether the
    // data set is permanently done or should be retried on a later cycle.
    private enum CusipMappingOutcome
    {
        Mapped,
        NoInfoTable,
        NoTrackedStocks,
    }

    private async Task<CusipMappingOutcome> BuildCusipMapping(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var infoTableEntry = FindRequiredEntry(context, "INFOTABLE.tsv");
        if (infoTableEntry == null)
            return CusipMappingOutcome.NoInfoTable;

        var uniqueCusips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scheduleCusipsByAccession = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase
        );
        await foreach (var row in context.TsvParser.ParseEntry(infoTableEntry))
        {
            var accession = GetValue(row, AccessionNumberColumn);
            if (!context.Submissions.TryGetValue(accession, out var submission))
                continue;

            var cusip = GetValue(row, "CUSIP");
            if (string.IsNullOrEmpty(cusip))
                continue;

            uniqueCusips.Add(cusip);

            // Remember which issuer(s) each Schedule 13D/G filing reports —
            // HandleAmendments scopes those filings' restatement delete to them.
            var filingType = submission.FormType.ToHoldingsFilingType();
            if (filingType is FilingType.Schedule13D or FilingType.Schedule13G)
            {
                if (!scheduleCusipsByAccession.TryGetValue(accession, out var cusips))
                {
                    cusips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    scheduleCusipsByAccession[accession] = cusips;
                }
                cusips.Add(cusip);
            }
        }

        _logger.LogInformation("Found {Count} unique CUSIPs in INFOTABLE", uniqueCusips.Count);

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var uniqueCusipsList = uniqueCusips.ToList();

        // Historical filings must resolve identities that are no longer in the live directory.
        // The default repository surface is active-only by design, so this importer opts into
        // retained inactive rows explicitly.
        var query = stockRepo.GetAllIncludingInactive();
        if (_workerOptions.TickersToSync?.Count > 0)
        {
            query = query.Where(stock =>
                _workerOptions.TickersToSync.Contains(stock.Ticker)
                || stock.SecondaryTickers.Any(ticker =>
                    _workerOptions.TickersToSync.Contains(ticker)
                )
            );
        }

        var stocksWithCusip = await query
            .Where(cs => cs.Cusip != null && uniqueCusipsList.Contains(cs.Cusip))
            .Select(cs => new { cs.Id, cs.Cusip })
            .ToListAsync(cancellationToken);

        // Retired CUSIPs must keep resolving: after an issuer-level CUSIP change,
        // laggard filers reference the old CUSIP for a quarter or two and every
        // historical data set does forever. Map the union of current CUSIPs and
        // aliases, with the current CUSIP winning a collision — otherwise a
        // re-import (the backfill a CUSIP change itself triggers) would drop
        // old-CUSIP lines wherever a restatement amendment rebuilds a quarter.
        var stockIdsQuery = query.Select(cs => cs.Id);
        var cusipAliases = await stockRepo
            .GetCusipAliases()
            .Where(a =>
                uniqueCusipsList.Contains(a.Cusip) && stockIdsQuery.Contains(a.CommonStockId)
            )
            .Select(a => new { a.CommonStockId, a.Cusip })
            .ToListAsync(cancellationToken);

        // The filer's OTHER listed securities (sibling share classes, units) carry their own
        // CUSIPs. They resolve to the same filer row but keep the listed ticker: the class is
        // part of the position's identity, and the exact price series it must be valued from.
        var listedCusips = await stockRepo
            .GetListedCusips()
            .Where(l =>
                uniqueCusipsList.Contains(l.Cusip) && stockIdsQuery.Contains(l.CommonStockId)
            )
            .Select(l => new
            {
                l.CommonStockId,
                l.ListedTicker,
                l.Cusip,
            })
            .ToListAsync(cancellationToken);

        // Precedence on a collision (defended against at write time, kept coherent here):
        // the primary CUSIP wins over a retired alias, which wins over a listing claim —
        // EXCEPT when a CUSIP is claimed as both an alias and a listing. An alias maps to
        // the primary series and a listing to a different security, so preferring either
        // silently merges two securities' positions; the CUSIP is dropped instead (its
        // lines accrue as unmapped) until the write-time guards converge the tables.
        var cusipMapping = new Dictionary<string, CusipTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var listed in listedCusips)
        {
            var listedTicker = string.IsNullOrWhiteSpace(listed.ListedTicker)
                ? null
                : listed.ListedTicker;
            cusipMapping[listed.Cusip] = new CusipTarget(listed.CommonStockId, listedTicker);
        }
        var listedClaims = new HashSet<string>(
            listedCusips.Select(l => l.Cusip),
            StringComparer.OrdinalIgnoreCase
        );
        var contested = new List<string>();
        foreach (var alias in cusipAliases)
        {
            if (listedClaims.Contains(alias.Cusip))
            {
                contested.Add(alias.Cusip);
                cusipMapping.Remove(alias.Cusip);
                continue;
            }
            cusipMapping[alias.Cusip] = new CusipTarget(alias.CommonStockId, null);
        }
        if (contested.Count > 0)
        {
            _logger.LogWarning(
                "Dropped {Count} CUSIP(s) claimed as both a retired alias and a secondary "
                    + "listing ({Cusips}) — resolving either way would merge two securities",
                contested.Count,
                string.Join(", ", contested)
            );
        }
        foreach (var stock in stocksWithCusip)
        {
            cusipMapping[stock.Cusip] = new CusipTarget(stock.Id, null);
        }

        _logger.LogInformation(
            "Mapped {Count} CUSIPs to tracked stocks ({AliasCount} retired aliases, {ListedCount} secondary listings, out of {Total} in data set)",
            cusipMapping.Count,
            cusipAliases.Count,
            listedCusips.Count,
            uniqueCusips.Count
        );

        if (cusipMapping.Count == 0)
        {
            _logger.LogInformation(
                "No tracked stocks mapped for this data set (CUSIPs may not be seeded yet) — will retry on a later cycle"
            );
            return CusipMappingOutcome.NoTrackedStocks;
        }

        context.CusipMapping = cusipMapping;
        var mappedStockIds = cusipMapping.Values.Select(t => t.CommonStockId).Distinct().ToList();
        context.IssuerSizes = await LoadIssuerSizes(stockRepo, mappedStockIds, cancellationToken);
        var tickerIdentities = await stockRepo
            .GetByIdsIncludingInactive(mappedStockIds)
            .Select(cs => new
            {
                cs.Id,
                cs.Ticker,
                cs.SecondaryTickers,
            })
            .ToListAsync(cancellationToken);
        context.PrimaryTickers = tickerIdentities.ToDictionary(cs => cs.Id, cs => cs.Ticker);
        context.SecondaryTickers = tickerIdentities.ToDictionary(
            cs => cs.Id,
            cs => cs.SecondaryTickers ?? []
        );

        foreach (var (accession, cusips) in scheduleCusipsByAccession)
        {
            var targets = new HashSet<CusipTarget>();
            foreach (var cusip in cusips)
            {
                if (cusipMapping.TryGetValue(cusip, out var target))
                    targets.Add(target);
            }
            context.ScheduleAccessionTargets[accession] = targets;
        }

        return CusipMappingOutcome.Mapped;
    }

    /// <summary>
    /// Loads how big each mapped issuer is, so a position can be checked against the company it
    /// claims to be part of (see <see cref="ImpossiblePositionGuard"/>). One query over the stocks
    /// this data set actually references.
    /// </summary>
    private static async Task<Dictionary<Guid, IssuerSize>> LoadIssuerSizes(
        CommonStockRepository stockRepo,
        List<Guid> stockIds,
        CancellationToken cancellationToken
    )
    {
        var sizes = await stockRepo
            .GetAllIncludingInactive()
            .Where(cs => stockIds.Contains(cs.Id))
            .Select(cs => new
            {
                cs.Id,
                cs.SharesOutStanding,
                cs.MarketCapitalization,
            })
            .ToListAsync(cancellationToken);

        return sizes.ToDictionary(
            s => s.Id,
            s => new IssuerSize(s.SharesOutStanding, s.MarketCapitalization)
        );
    }

    /// <summary>
    /// Pre-fetches Yahoo closing prices for all (stock, reportDate) pairs in this dataset.
    /// Holdings without an available price will be marked as ValuePending during import.
    /// </summary>
    private async Task BuildPriceMap(ImportContext context, CancellationToken cancellationToken)
    {
        var reportDates = new HashSet<DateOnly>();
        foreach (var submission in context.Submissions.Values)
        {
            if (TryParseDateOnly(submission.PeriodOfReport, out var date))
                reportDates.Add(date);
        }

        var listings = context.CusipMapping.Values.Distinct().ToList();

        var requests = reportDates
            .SelectMany(date =>
                listings.Select(listing => (listing.CommonStockId, listing.ListedTicker, date))
            )
            .ToList();

        context.StockPrices = await _stockPriceProvider.GetClosingPrices(
            requests,
            cancellationToken
        );

        _logger.LogInformation(
            "Fetched Yahoo prices for {Found}/{Requested} (stock, date) pairs",
            context.StockPrices.Count,
            requests.Count
        );
    }

    /// <summary>
    /// Pre-fetches every split for the stocks in this data set. Stored prices are on today's
    /// post-split basis, so an as-filed share count has to be restated by these before it can be
    /// multiplied into a dollar value — see <see cref="HoldingValueBasis"/> for why, and for what
    /// happens while a split is still awaiting its price adjustment.
    /// </summary>
    private async Task BuildSplitMap(ImportContext context, CancellationToken cancellationToken)
    {
        var stockIds = context.CusipMapping.Values.Select(t => t.CommonStockId).Distinct().ToList();

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var splits = await dbContext
            .Set<StockSplit>()
            .Where(s => stockIds.Contains(s.CommonStockId))
            .ToListAsync(cancellationToken);

        context.StockSplits = splits
            .GroupBy(s => s.CommonStockId)
            .ToDictionary(g => g.Key, g => g.ToList());

        _logger.LogInformation(
            "Loaded {Splits} split(s) across {Stocks} stock(s) for share-basis restatement",
            splits.Count,
            context.StockSplits.Count
        );
    }

    // Reports how the values this import derived compared with the values the filers themselves
    // reported. Advisory: a gross disagreement means a basis is wrong somewhere (a split we never
    // captured, a depositary ratio we do not model), and naming the securities makes it something
    // an operator can chase rather than a number that only trends.
    private void LogValueBasisAudit(ImportContext context)
    {
        var audit = context.ValueBasisAudit;
        if (audit.Disagreed == 0)
        {
            _logger.LogInformation(
                "Value basis check: {Compared} common-stock position(s) compared against their filed value, none disagreeing beyond {Multiple}x. Options (tallied apart — filers often report the premium, not the notional we derive): {OptionDisagreed} of {OptionCompared} apart",
                audit.Compared,
                ValueBasisAudit.DisagreementMultiple,
                audit.OptionDisagreed,
                audit.OptionCompared
            );
            return;
        }

        _logger.LogWarning(
            "Value basis check: {Disagreed} of {Compared} common-stock position(s) disagree with their filed value by more than {Multiple}x. Options (tallied apart — filers often report the premium, not the notional we derive): {OptionDisagreed} of {OptionCompared} apart. Samples: {Samples}",
            audit.Disagreed,
            audit.Compared,
            ValueBasisAudit.DisagreementMultiple,
            audit.OptionDisagreed,
            audit.OptionCompared,
            string.Join(
                "; ",
                audit.Samples.Select(s =>
                    $"{s.Cusip}@{s.ReportDate:yyyy-MM-dd} {s.Shares} sh derived {s.DerivedValue} vs filed {s.FiledValue}"
                )
            )
        );
    }

    // Notes one position the import could not attach to a tracked stock, so the gap is countable
    // instead of vanishing into a skip counter. Accumulated in memory and written once per data
    // set by FlushUnmappedCusips.
    private static void RecordUnmappedCusip(
        ImportContext context,
        Dictionary<string, string> row,
        string cusip,
        string accession,
        SubmissionRow submission
    )
    {
        if (string.IsNullOrWhiteSpace(cusip))
            return;

        // One filing reporting a security across several otherManager legs is ONE position we
        // failed to map, not several. Counting raw rows inflates both the position count and the
        // dollars the queue ranks on — the tracked lane collapses those legs in AddOrMergeHolding,
        // so the untracked lane has to collapse them too or the two are not comparable. Rows for
        // an accession arrive contiguously, so remembering only the current filing's CUSIPs is
        // enough and keeps this bounded.
        if (!string.Equals(context.UnmappedCusipAccession, accession, StringComparison.Ordinal))
        {
            context.UnmappedCusipAccession = accession;
            context.UnmappedCusipsSeenInAccession.Clear();
        }

        if (!context.UnmappedCusipsSeenInAccession.Add(cusip))
            return;

        if (!TryParseDateOnly(submission.PeriodOfReport, out var reportDate))
            return;

        TryParseDateOnly(submission.FilingDate, out var filingDate);
        var filed = ParseLong(GetValue(row, "VALUE"));
        var filedDollars = filed > 0 ? FiledValueScale.ToDollars(filed, filingDate) : 0m;

        var key = (cusip, reportDate);
        if (!context.UnmappedCusips.TryGetValue(key, out var tally))
        {
            tally = new UnmappedCusipTally();
            context.UnmappedCusips[key] = tally;
        }

        tally.Add(GetValue(row, "NAMEOFISSUER"), filedDollars);
    }

    // Writes this import's unmapped identifiers into the queue without destroying what other
    // imports contributed. One report date's filings are spread across SEVERAL data sets — the
    // filing windows straddle quarter boundaries, and amendments land months later — so deleting
    // a whole report-date slice here let the last data set processed wipe every other one's rows:
    // Scion's $13.1M Bruker preferred vanished behind a later data set's $3.3M sighting of the
    // same identifier. Instead, each import replaces only the (CUSIP, quarter) keys it actually
    // saw, and clears rows whose identifier it can now resolve — that is how a newly-added alias
    // empties its backlog from the queue on the forced re-import, since a mapped CUSIP appears in
    // no tally. A key seen by two data sets keeps the most recent import's figures (a floor, not a
    // census); the queue ranks leads by materiality, so surviving with a floor beats vanishing.
    private async Task FlushUnmappedCusips(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var reportDates = new HashSet<DateOnly>();
        foreach (var submission in context.Submissions.Values)
        {
            if (TryParseDateOnly(submission.PeriodOfReport, out var date))
                reportDates.Add(date);
        }

        if (reportDates.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var dates = reportDates.ToList();

        // Identifiers this import resolved are no longer gaps anywhere in the covered window,
        // whichever data set recorded them.
        var mappedCusips = context.CusipMapping.Keys.ToList();
        var cleared =
            mappedCusips.Count == 0
                ? 0
                : await dbContext
                    .Set<UnmappedCusip>()
                    .Where(u => dates.Contains(u.ReportDate) && mappedCusips.Contains(u.Cusip))
                    .ExecuteDeleteAsync(cancellationToken);

        if (context.UnmappedCusips.Count == 0)
        {
            if (cleared > 0)
            {
                _logger.LogInformation(
                    "Cleared {Cleared} parked CUSIP row(s) whose identifier now resolves",
                    cleared
                );
            }
            return;
        }

        // Update-or-insert per key the import saw; rows for keys only other data sets saw are
        // untouched. The candidate load is bounded by this import's own distinct CUSIPs.
        var tallyCusips = context.UnmappedCusips.Keys.Select(key => key.Cusip).Distinct().ToList();
        var existingByKey = (
            await dbContext
                .Set<UnmappedCusip>()
                .Where(u => dates.Contains(u.ReportDate) && tallyCusips.Contains(u.Cusip))
                .ToListAsync(cancellationToken)
        ).ToDictionary(u => (u.Cusip, u.ReportDate));

        foreach (var ((cusip, reportDate), tally) in context.UnmappedCusips)
        {
            if (existingByKey.TryGetValue((cusip, reportDate), out var existing))
            {
                existing.IssuerName = Truncate(tally.IssuerName, MaxIssuerNameLength);
                existing.Positions = tally.Positions;
                existing.FiledValue = tally.FiledValue;
                existing.CreationTime = DateTime.UtcNow;
                continue;
            }

            dbContext
                .Set<UnmappedCusip>()
                .Add(
                    new UnmappedCusip
                    {
                        Cusip = cusip,
                        ReportDate = reportDate,
                        IssuerName = Truncate(tally.IssuerName, MaxIssuerNameLength),
                        Positions = tally.Positions,
                        FiledValue = tally.FiledValue,
                    }
                );
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Parked {Count} unmapped CUSIP(s) carrying {Total:N0} filed dollars; cleared {Cleared} now-resolved row(s). Largest: {Largest}",
            context.UnmappedCusips.Count,
            context.UnmappedCusips.Values.Sum(t => (decimal)t.FiledValue),
            cleared,
            string.Join(
                ", ",
                context
                    .UnmappedCusips.OrderByDescending(pair => pair.Value.FiledValue)
                    .Take(5)
                    .Select(pair =>
                        $"{pair.Key.Cusip} ({pair.Value.IssuerName}) {pair.Value.FiledValue:N0}"
                    )
            )
        );
    }

    private static string Truncate(string value, int maxLength) =>
        value != null && value.Length > maxLength ? value[..maxLength] : value;

    private async Task UpsertInstitutionalHolders(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var cikToHolderId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        using var scope = _scopeFactory.CreateScope();
        var holderRepo = scope.ServiceProvider.GetRequiredService<InstitutionalHolderRepository>();

        var allCiks = context
            .Submissions.Values.Where(s => !string.IsNullOrEmpty(s.Cik))
            .Select(s => s.Cik)
            .Distinct()
            .ToList();

        var existingHolders = await holderRepo.GetByCiks(allCiks, cancellationToken);
        foreach (var holder in existingHolders)
        {
            cikToHolderId[holder.Cik] = holder.Id;
        }

        RefreshExistingHolderConfidentialTreatment(context, existingHolders);
        CreateMissingHolders(context, existingHolders, holderRepo, cikToHolderId);

        await holderRepo.SaveChanges();

        _logger.LogInformation("Upserted {Count} institutional holders", cikToHolderId.Count);
        context.CikToHolderId = cikToHolderId;
    }

    // Refresh the confidential-treatment flag on holders we already track from
    // their latest filing's cover page; their identity columns stay as first seen.
    // Keyed by canonical CIK up front: a linear scan per holder is O(holders × submissions),
    // which a quarterly bulk data set (~8k of each) turns into tens of millions of
    // comparisons — and its first-match pick was import-order-arbitrary when a
    // filer has several submissions (multiple quarters) in one data set.
    internal void RefreshExistingHolderConfidentialTreatment(
        ImportContext context,
        List<InstitutionalHolder> existingHolders
    )
    {
        var latestByCanonicalCik = BuildLatestSubmissionByCanonicalCik(context.Submissions.Values);

        foreach (var holder in existingHolders)
        {
            var canonicalCik = CikNormalizer.Canonicalize(holder.Cik);
            if (
                canonicalCik == null
                || !latestByCanonicalCik.TryGetValue(canonicalCik, out var submission)
            )
                continue;
            context.CoverPages.TryGetValue(submission.AccessionNumber, out var cp);
            if (cp != null)
                holder.ConfidentialTreatmentRequested = IsYes(cp.ConfidentialTreatment);
        }
    }

    internal static Dictionary<string, SubmissionRow> BuildLatestSubmissionByCanonicalCik(
        IEnumerable<SubmissionRow> submissions
    )
    {
        var latestByCik = new Dictionary<string, SubmissionRow>(StringComparer.Ordinal);
        foreach (var submission in submissions)
        {
            var canonicalCik = CikNormalizer.Canonicalize(submission.Cik);
            if (canonicalCik == null)
                continue;
            if (
                !latestByCik.TryGetValue(canonicalCik, out var current)
                || CompareByFilingDateThenAccession(submission, current) > 0
            )
            {
                latestByCik[canonicalCik] = submission;
            }
        }

        return latestByCik;
    }

    // The most recently filed submission per CIK — same ordering contract as
    // DeduplicateSubmissions (parsed FilingDate, accession breaks same-day ties).
    internal static Dictionary<string, SubmissionRow> BuildLatestSubmissionByCik(
        IEnumerable<SubmissionRow> submissions
    )
    {
        var latestByCik = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var submission in submissions)
        {
            if (string.IsNullOrEmpty(submission.Cik))
                continue;
            if (
                !latestByCik.TryGetValue(submission.Cik, out var current)
                || CompareByFilingDateThenAccession(submission, current) > 0
            )
            {
                latestByCik[submission.Cik] = submission;
            }
        }
        return latestByCik;
    }

    private static int CompareByFilingDateThenAccession(SubmissionRow left, SubmissionRow right)
    {
        TryParseDateOnly(left.FilingDate, out var leftDate);
        TryParseDateOnly(right.FilingDate, out var rightDate);
        var byDate = leftDate.CompareTo(rightDate);
        return byDate != 0
            ? byDate
            : string.CompareOrdinal(left.AccessionNumber, right.AccessionNumber);
    }

    // Submissions whose CIK has no holder row yet become new InstitutionalHolder
    // records populated from the cover page where available.
    internal void CreateMissingHolders(
        ImportContext context,
        List<InstitutionalHolder> existingHolders,
        InstitutionalHolderRepository holderRepo,
        Dictionary<string, Guid> cikToHolderId
    )
    {
        var existingByCik = existingHolders.ToDictionary(
            holder => holder.Cik,
            StringComparer.Ordinal
        );

        foreach (var submission in context.Submissions.Values)
        {
            if (string.IsNullOrEmpty(submission.Cik))
                continue;

            existingByCik.TryGetValue(submission.Cik, out var existingHolder);
            if (existingHolder == null)
            {
                var alternateCik = InstitutionalHolderRepository.AlternateCikSpelling(
                    submission.Cik
                );
                if (alternateCik != null)
                    existingByCik.TryGetValue(alternateCik, out existingHolder);
            }
            if (existingHolder != null)
            {
                cikToHolderId[submission.Cik] = existingHolder.Id;
                continue;
            }

            context.CoverPages.TryGetValue(submission.AccessionNumber, out var coverPage);

            // Cover-page strings are unbounded in the source TSV/XML; one over-length
            // value rejects the whole batch flush (22001) and discards the filing's
            // rows, so each is clamped to its column bound.
            var holder = new InstitutionalHolder
            {
                Cik = submission.Cik,
                Name = ClampLength(coverPage?.CompanyName, 512),
                City = ClampLength(coverPage?.City, 128),
                StateOrCountry = ClampLength(coverPage?.StateOrCountry, 64),
                Form13FFileNumber = ClampLength(coverPage?.Form13FFileNumber, 32),
                CrdNumber = ClampLength(coverPage?.CrdNumber, 32),
                Classification = FundClassifierService.Classify(coverPage?.CompanyName),
                ConfidentialTreatmentRequested = IsYes(coverPage?.ConfidentialTreatment),
            };

            holderRepo.Add(holder);
            cikToHolderId[submission.Cik] = holder.Id;
            existingHolders.Add(holder);
            existingByCik[holder.Cik] = holder;
        }
    }

    // Truncates a parsed string to its destination column bound. The cover page is
    // free text; a value past the bound is malformed, and storing the prefix beats
    // losing the filer's whole batch to a 22001 abort.
    internal static string ClampLength(string value, int maxLength) =>
        value.TruncateToFit(maxLength);

    private async Task ParseOtherManagers(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var entry = FindEntry(context.Archive, "OTHERMANAGER2.tsv");
        if (entry == null)
        {
            _logger.LogInformation("OTHERMANAGER2.tsv not found, skipping other-manager parsing");
            return;
        }

        var managers = new Dictionary<string, Dictionary<int, OtherManagerIdentity>>(
            StringComparer.OrdinalIgnoreCase
        );
        await foreach (var row in context.TsvParser.ParseEntry(entry))
        {
            if (
                !TryParseOtherManagerRow(
                    row,
                    context.Submissions,
                    out var accession,
                    out var seq,
                    out var identity
                )
            )
                continue;

            if (!managers.TryGetValue(accession, out var seqMap))
            {
                seqMap = [];
                managers[accession] = seqMap;
            }

            seqMap[seq] = identity;
        }

        context.OtherManagers = managers;
        _logger.LogInformation("Parsed other-manager mappings for {Count} filings", managers.Count);
    }

    /// <summary>
    /// Parses OTHERMANAGER.tsv — the cover page's list of managers who report FOR the filer, the
    /// opposite edge to the summary page's list. Absent from archives built before this lane
    /// existed (and from the Schedule 13D/G synthetic archive), so a missing entry is normal.
    /// </summary>
    private async Task ParseOtherManagerCoverList(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var entry = FindEntry(context.Archive, "OTHERMANAGER.tsv");
        if (entry == null)
        {
            _logger.LogInformation(
                "OTHERMANAGER.tsv not found, skipping cover-page other-manager parsing"
            );
            return;
        }

        // Ordered by the SEC's surrogate key where present so the stored ordinal follows filed
        // order rather than however the archive happened to stream. Rows without one keep their
        // file position, which is the same order for every archive seen so far.
        var ordered = new Dictionary<string, List<(long Sort, OtherManagerIdentity Identity)>>(
            StringComparer.OrdinalIgnoreCase
        );
        var position = 0L;
        await foreach (var row in context.TsvParser.ParseEntry(entry))
        {
            position++;
            var accession = GetValue(row, AccessionNumberColumn);
            if (string.IsNullOrEmpty(accession) || !context.Submissions.ContainsKey(accession))
                continue;

            var name = GetValue(row, "NAME");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var sort = ParseNullableLong(GetValue(row, "OTHERMANAGER_SK")) ?? position;
            if (!ordered.TryGetValue(accession, out var list))
            {
                list = [];
                ordered[accession] = list;
            }

            list.Add((sort, BuildOtherManagerIdentity(row, name)));
        }

        context.CoverPageOtherManagers = ordered.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(item => item.Sort).Select(item => item.Identity).ToList(),
            StringComparer.OrdinalIgnoreCase
        );
        _logger.LogInformation(
            "Parsed cover-page other-manager lists for {Count} filings",
            context.CoverPageOtherManagers.Count
        );
    }

    private bool TryParseOtherManagerRow(
        Dictionary<string, string> row,
        Dictionary<string, SubmissionRow> submissions,
        out string accession,
        out int seq,
        out OtherManagerIdentity identity
    )
    {
        seq = 0;
        identity = null;

        accession = GetValue(row, AccessionNumberColumn);
        if (string.IsNullOrEmpty(accession) || !submissions.ContainsKey(accession))
            return false;

        var seqStr = GetValue(row, "SEQUENCENUMBER");
        if (!int.TryParse(seqStr, out seq))
        {
            _logger.LogDebug(
                "Failed to parse sequence number '{SeqStr}' in OTHERMANAGER2.tsv",
                seqStr
            );
            return false;
        }

        var name = GetValue(row, "NAME");
        if (string.IsNullOrWhiteSpace(name))
            return false;

        identity = BuildOtherManagerIdentity(row, name);
        return true;
    }

    /// <summary>
    /// Reads a manager's filed identifiers off an other-manager row. Every identifier column is
    /// optional at the source and absent entirely from archives written before this lane existed,
    /// so each read tolerates a missing column and yields null rather than failing the row — the
    /// name alone still makes a usable, if unlinkable, entry.
    /// </summary>
    private static OtherManagerIdentity BuildOtherManagerIdentity(
        Dictionary<string, string> row,
        string name
    )
    {
        return new OtherManagerIdentity(
            ClampLength(name, 256),
            NormalizeCik(GetValue(row, "CIK")),
            ClampLength(NormalizeIdentifier(GetValue(row, "FORM13FFILENUMBER")), 32),
            ClampLength(NormalizeIdentifier(GetValue(row, "CRDNUMBER")), 32),
            ClampLength(NormalizeIdentifier(GetValue(row, "SECFILENUMBER")), 32)
        );
    }

    /// <summary>
    /// Persists both other-manager lists for every 13F accession this import covers, replacing
    /// whatever those accessions held before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delete spans every 13F accession in the import, not only the ones that produced rows,
    /// so a filing that no longer declares a manager has its stale list cleared rather than kept
    /// forever. Accessions outside the import are left alone on purpose: a "new holdings"
    /// amendment merges without deleting, so a holder's quarter can span several accessions and
    /// the positions still carrying an older one need its list to resolve. Sweeping orphans the
    /// way the filing summaries do would strand exactly those positions.
    /// </para>
    /// <para>
    /// Restricted to Form 13F because the Schedule 13D/G lane shares this pipeline and ships a
    /// header-only OTHERMANAGER2.tsv — without the filter a 13D/G import would delete a 13F
    /// filing's managers and write nothing back.
    /// </para>
    /// <para>
    /// The write is an upsert on the (accession, direction, sequence) key followed by a stale-row
    /// delete, in that order, so a portal read never catches an accession's list empty
    /// mid-replace. The bulk and realtime importers run in the same process and can flush the
    /// same accession concurrently; the unique index makes that interleave converge on one copy
    /// instead of accumulating duplicates. A crash between the two statements leaves stale rows,
    /// not duplicates, and the re-import heals them — a data set is only marked processed once
    /// the whole import returns.
    /// </para>
    /// <para>
    /// One asymmetry is accepted: a RESTATEMENT amendment re-homes the original filing's
    /// positions under the amendment's accession, so the original's rows here become unreachable
    /// and are never deleted (nothing joins to them). They are small, and sweeping them would
    /// need the very orphan scan that breaks the NEW-HOLDINGS case above.
    /// </para>
    /// </remarks>
    private async Task FlushFilingOtherManagers(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var accessions = context
            .Submissions.Values.Where(s => s.FormType.ToHoldingsFilingType() == FilingType.Form13F)
            .Select(s => s.AccessionNumber)
            .ToList();
        if (accessions.Count == 0)
            return;

        var rows = new List<FilingOtherManager>();
        foreach (var accession in accessions)
        {
            if (context.OtherManagers.TryGetValue(accession, out var seqMap))
            {
                foreach (var (sequence, identity) in seqMap)
                {
                    rows.Add(
                        BuildFilingOtherManager(
                            accession,
                            OtherManagerDirection.IncludedInReport,
                            sequence,
                            identity
                        )
                    );
                }
            }

            if (!context.CoverPageOtherManagers.TryGetValue(accession, out var coverList))
                continue;

            // The cover page files no sequence numbers, so the stored ordinal is positional. It
            // orders the list and keeps the column non-null; nothing points at it.
            for (var index = 0; index < coverList.Count; index++)
            {
                rows.Add(
                    BuildFilingOtherManager(
                        accession,
                        OtherManagerDirection.ReportsForFiler,
                        index + 1,
                        coverList[index]
                    )
                );
            }
        }

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        // Dedupe within the batch on the upsert key: a malformed filing repeating a sequence
        // would otherwise make ON CONFLICT touch the same row twice and abort the statement.
        var deduped = rows.GroupBy(r => (r.AccessionNumber, r.Direction, r.SequenceNumber))
            .Select(g => g.First())
            .ToList();

        if (deduped.Count > 0)
        {
            await dbContext
                .Set<FilingOtherManager>()
                .UpsertRange(deduped)
                .On(m => new
                {
                    m.AccessionNumber,
                    m.Direction,
                    m.SequenceNumber,
                })
                .WhenMatched(
                    (existing, incoming) =>
                        new FilingOtherManager
                        {
                            Cik = incoming.Cik,
                            Form13FFileNumber = incoming.Form13FFileNumber,
                            CrdNumber = incoming.CrdNumber,
                            SecFileNumber = incoming.SecFileNumber,
                            Name = incoming.Name,
                            CreationTime = incoming.CreationTime,
                        }
                )
                .RunAsync(cancellationToken);
        }

        // Rows a covered accession no longer declares. Compared in memory: the survivor set is
        // this import's own rows, and the candidate load is bounded by the covered accessions.
        var keep = deduped
            .Select(r => (r.AccessionNumber, r.Direction, r.SequenceNumber))
            .ToHashSet();
        var existingRows = await dbContext
            .Set<FilingOtherManager>()
            .Where(m => accessions.Contains(m.AccessionNumber))
            .Select(m => new
            {
                m.Id,
                m.AccessionNumber,
                m.Direction,
                m.SequenceNumber,
            })
            .ToListAsync(cancellationToken);
        var staleIds = existingRows
            .Where(m => !keep.Contains((m.AccessionNumber, m.Direction, m.SequenceNumber)))
            .Select(m => m.Id)
            .ToList();
        var removed =
            staleIds.Count == 0
                ? 0
                : await dbContext
                    .Set<FilingOtherManager>()
                    .Where(m => staleIds.Contains(m.Id))
                    .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation(
            "Stored {Count} other-manager row(s) across {Filings} filing(s), removing {Removed} stale row(s)",
            deduped.Count,
            accessions.Count,
            removed
        );
    }

    private static FilingOtherManager BuildFilingOtherManager(
        string accession,
        OtherManagerDirection direction,
        int sequenceNumber,
        OtherManagerIdentity identity
    )
    {
        return new FilingOtherManager
        {
            AccessionNumber = ClampLength(accession, 32),
            Direction = direction,
            SequenceNumber = sequenceNumber,
            Cik = ClampLength(identity.Cik, 16),
            Form13FFileNumber = identity.Form13FFileNumber,
            CrdNumber = identity.CrdNumber,
            SecFileNumber = identity.SecFileNumber,
            Name = identity.Name,
        };
    }

    private async Task HandleAmendments(ImportContext context, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var holdingRepo =
            scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();

        foreach (var (accession, submission) in context.Submissions)
        {
            if (
                !TryResolveAmendmentTarget(
                    accession,
                    submission,
                    context,
                    out var holderId,
                    out var reportDate,
                    out var filingType
                )
            )
                continue;

            // "NEW HOLDINGS" amendments add positions to the existing portfolio;
            // only "RESTATEMENT" amendments (and legacy filings without the field)
            // replace the entire set.
            if (IsNewHoldingsAmendment(accession, context))
            {
                _logger.LogInformation(
                    "Amendment {Accession} is NEW HOLDINGS — merging without deleting existing positions",
                    accession
                );
                continue;
            }

            // Scope the delete to the amendment's OWN filing type. A holder can
            // file a 13F-HR and a Schedule 13D/G whose report dates collide on the
            // same quarter end (BlackRock's monthly 13G/A amendments land on
            // 31 Mar / 31 Dec — exactly the 13F quarter ends), and the upsert key
            // keeps them as distinct rows. Deleting by (holder, reportDate) alone
            // let a 13G/A restatement wipe the entire 13F-HR portfolio at that
            // quarter (#3738), so a $5T filer vanished from the AUM rankings.
            // Restatements only ever replace their own form's rows.
            var existingQuery = holdingRepo
                .GetAll()
                .Where(h =>
                    h.InstitutionalHolderId == holderId
                    && h.ReportDate == reportDate
                    && h.FilingType == filingType
                );

            // A 13F restatement replaces the holder's whole portfolio for the
            // quarter, but a Schedule 13D/G filing covers a SINGLE security — its
            // delete must be scoped to the exact (issuer, listing) pairs the
            // amendment itself reports, not the whole issuer. Passive filers
            // amend many issuers with the same event date (year/quarter end);
            // an unscoped delete let each 13G/A wipe every other issuer's stake
            // at that date, and a stock-grained one wipes the SIBLING CLASS's
            // stake when a filer holds two classes — and since the wiped
            // accessions stay recorded as processed and no bulk data set exists
            // for 13D/G, either loss is permanent and silent.
            var deleted = 0;
            if (filingType != FilingType.Form13F)
            {
                if (
                    !context.ScheduleAccessionTargets.TryGetValue(accession, out var issuerTargets)
                    || issuerTargets.Count == 0
                )
                    continue;

                foreach (var target in issuerTargets)
                {
                    // One DELETE per (issuer, listing) pair — an accession names one
                    // security, at most a handful. A null listing compares as IS NULL.
                    deleted += await existingQuery
                        .Where(h =>
                            h.CommonStockId == target.CommonStockId
                            && h.ListedTicker == target.ListedTicker
                        )
                        .ExecuteDeleteAsync(cancellationToken);
                }
            }
            else
            {
                // Set-based delete: materialising a large filer's whole portfolio
                // into the change tracker (and deleting row-by-id) accumulated
                // hundreds of thousands of tracked entities across a bulk data
                // set's restatements; one DELETE statement per amendment does the
                // same work with zero materialisation.
                deleted = await existingQuery.ExecuteDeleteAsync(cancellationToken);
            }

            if (deleted > 0)
            {
                _logger.LogInformation(
                    "Deleted {Count} {FilingType} holdings for RESTATEMENT amendment {Accession}",
                    deleted,
                    filingType,
                    accession
                );
            }
        }
    }

    private static bool IsNewHoldingsAmendment(string accession, ImportContext context)
    {
        return context.CoverPages != null
            && context.CoverPages.TryGetValue(accession, out var coverPage)
            && string.Equals(
                coverPage.AmendmentType,
                "NEW HOLDINGS",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool TryResolveAmendmentTarget(
        string accession,
        SubmissionRow submission,
        ImportContext context,
        out Guid holderId,
        out DateOnly reportDate,
        out FilingType filingType
    )
    {
        holderId = default;
        reportDate = default;
        // The form the amendment restates — the delete is scoped to this so a
        // Schedule 13D/G amendment never deletes a 13F-HR portfolio (or vice
        // versa) sharing the same (holder, quarter). Defaults to 13F for an
        // unrecognised form so behaviour matches the historical 13F-only path.
        filingType = FilingType.Form13F;

        if (!context.CoverPages.TryGetValue(accession, out var coverPage))
            return false;
        if (!string.Equals(coverPage.IsAmendment, "Y", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(submission.Cik))
            return false;
        if (!context.CikToHolderId.TryGetValue(submission.Cik, out holderId))
            return false;
        if (!TryParseDateOnly(submission.PeriodOfReport, out reportDate))
            return false;

        filingType = submission.FormType.ToHoldingsFilingType() ?? FilingType.Form13F;
        return true;
    }

    private async Task<HoldingsStreamResult> StreamAndInsertHoldings(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var infoTableEntry = FindEntry(context.Archive, "INFOTABLE.tsv");
        var bufferedRows = new List<BufferedHoldingRow>();
        var totalInserted = 0;
        var totalSkipped = 0;
        var totalDuplicates = 0;
        var totalPending = 0;
        var skippedStaleParent = false;
        string currentAccession = null;

        await foreach (var row in context.TsvParser.ParseEntry(infoTableEntry))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accession = GetValue(row, AccessionNumberColumn);

            // Flush at the accession boundary, not at a fixed row count. Every
            // row sharing an upsert key inside one filing lives in that filing's
            // INFOTABLE section (a holder splits a position across otherManager
            // codes so the same security can appear several times with rows
            // scattered hundreds apart). FlushBatch's WhenMatched clause
            // REPLACES — so if a key's rows fall in different flushes, only
            // the last one's sum survives. SEC orders the bulk INFOTABLE by
            // INFOTABLE_SK and the realtime archive by XML element order, so
            // a single accession's rows are always contiguous; flushing only
            // when the accession changes guarantees both the per-filing
            // share-count repair and the in-memory aggregation see the whole
            // filing before any UPSERT for its keys runs.
            if (currentAccession != null && accession != currentAccession && bufferedRows.Count > 0)
            {
                var flushed = await RepairMergeAndFlush(
                    currentAccession,
                    bufferedRows,
                    context,
                    cancellationToken
                );
                totalInserted += flushed.Inserted;
                totalDuplicates += flushed.Duplicates;
                totalPending += flushed.Pending;
                skippedStaleParent |= flushed.SkippedStaleParent;
                bufferedRows.Clear();
            }
            currentAccession = accession;

            if (!context.Submissions.TryGetValue(accession, out var submission))
                continue;

            var cusip = GetValue(row, "CUSIP");
            if (!context.CusipMapping.TryGetValue(cusip, out var target))
            {
                totalSkipped++;
                RecordUnmappedCusip(context, row, cusip, accession, submission);
                continue;
            }

            if (!context.CikToHolderId.TryGetValue(submission.Cik, out var holderId))
                continue;

            TryParseDateOnly(submission.FilingDate, out var filingDate);
            TryParseDateOnly(submission.PeriodOfReport, out var reportDate);

            var (holding, managerEntry, _, reportedValue) = ParseHoldingRow(
                row,
                accession,
                cusip,
                target,
                holderId,
                filingDate,
                reportDate,
                context
            );
            bufferedRows.Add(
                new BufferedHoldingRow
                {
                    Holding = holding,
                    ManagerEntry = managerEntry,
                    ReportedValue = reportedValue,
                }
            );
        }

        if (bufferedRows.Count > 0)
        {
            var flushed = await RepairMergeAndFlush(
                currentAccession,
                bufferedRows,
                context,
                cancellationToken
            );
            totalInserted += flushed.Inserted;
            totalDuplicates += flushed.Duplicates;
            totalPending += flushed.Pending;
            skippedStaleParent |= flushed.SkippedStaleParent;
            bufferedRows.Clear();
        }

        _logger.LogInformation(
            "Import complete. Inserted: {Inserted}, Skipped (untracked): {Skipped}, Duplicates: {Duplicates}, Pending price: {Pending}",
            totalInserted,
            totalSkipped,
            totalDuplicates,
            totalPending
        );

        LogValueBasisAudit(context);
        await FlushUnmappedCusips(context, cancellationToken);

        return new HoldingsStreamResult(totalInserted, skippedStaleParent);
    }

    private readonly record struct HoldingsStreamResult(int Inserted, bool SkippedStaleParent);

    // Runs the per-filing share-count repair over one accession's buffered rows,
    // merges them by upsert key, and flushes the batch. Repair must happen here —
    // after the whole filing is buffered, before any row merges — because the
    // duplicated-column signal is a property of the filing, not of a single row.
    private async Task<(
        int Inserted,
        int Duplicates,
        int Pending,
        bool SkippedStaleParent
    )> RepairMergeAndFlush(
        string accession,
        List<BufferedHoldingRow> bufferedRows,
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        if (Corrupt13FShareCountRepairer.IsSuspect(bufferedRows))
        {
            var outcome = Corrupt13FShareCountRepairer.Repair(bufferedRows, context.StockPrices);
            _logger.LogWarning(
                "Filing {Accession} duplicates its value column into the share-count column; "
                    + "recovered {Repaired} row(s) from the filed value, dropped {Dropped} unrepairable row(s)",
                accession,
                outcome.RepairedRows,
                outcome.DroppedRows
            );
        }

        var holdingsMap = new Dictionary<string, InstitutionalHolding>();
        var duplicates = 0;
        var pending = 0;

        foreach (var row in bufferedRows)
        {
            if (AddOrMergeHolding(holdingsMap, row.Holding, row.ManagerEntry))
            {
                if (row.Holding.ValuePending)
                    pending++;
            }
            else
            {
                duplicates++;
            }
        }

        var flushResult = await HoldingsBatchPacer.Complete(
            holdingsMap.Count > 0
                ? FlushBatch(holdingsMap.Values.ToList(), cancellationToken)
                : Task.FromResult(new HoldingsFlushResult(0, SkippedStaleParent: false)),
            static result => result.Inserted > 0,
            context.BatchPause,
            cancellationToken
        );

        return (flushResult.Inserted, duplicates, pending, flushResult.SkippedStaleParent);
    }

    // Buffers a parsed row by its upsert key. A 13F holder can split one security
    // across several otherManager codes, so the same key recurs within a filing;
    // those rows accumulate into the in-memory position rather than overwriting it.
    // Returns true when the row started a new position, false when it merged into
    // an existing buffered one.
    private static bool AddOrMergeHolding(
        Dictionary<string, InstitutionalHolding> holdingsMap,
        InstitutionalHolding holding,
        HoldingManagerEntry managerEntry
    )
    {
        var uniqueKey = BuildHoldingKey(holding);
        if (holdingsMap.TryGetValue(uniqueKey, out var existing))
        {
            existing.Shares += holding.Shares;
            existing.Value += holding.Value;
            // The filed value has to accumulate with the value it is there to audit. A filer that
            // splits one position across otherManager codes files a value per leg, so keeping only
            // the first leg's figure leaves the merged row claiming a fraction of what was filed —
            // and every such position then reads as a gross derived-vs-filed disagreement that is
            // really an artefact of this merge.
            existing.FiledValue =
                existing.FiledValue is null && holding.FiledValue is null
                    ? null
                    : (existing.FiledValue ?? 0) + (holding.FiledValue ?? 0);
            existing.VotingAuthSole += holding.VotingAuthSole;
            existing.VotingAuthShared += holding.VotingAuthShared;
            existing.VotingAuthNone += holding.VotingAuthNone;
            // One unpriceable leg makes the merged position unpriceable: the surviving legs would
            // otherwise sum to a value that silently omits part of the holding.
            existing.ValueUnavailable |= holding.ValueUnavailable;
            // Likewise one filed-value leg makes the merged figure partly filed: keeping the
            // Derived label would present the mixed sum as a clean derivation — and expose it to
            // the implausible-derivation reset, which must never touch a filer's own figure.
            if (holding.ValueSource == ValueSource.Filed)
            {
                existing.ValueSource = ValueSource.Filed;
            }
            existing.ManagerEntries.Add(managerEntry);
            return false;
        }

        holding.ManagerEntries.Add(managerEntry);
        holdingsMap[uniqueKey] = holding;
        return true;
    }

    private static (
        InstitutionalHolding Holding,
        HoldingManagerEntry ManagerEntry,
        bool ValuePending,
        long ReportedValue
    ) ParseHoldingRow(
        Dictionary<string, string> row,
        string accession,
        string cusip,
        CusipTarget target,
        Guid holderId,
        DateOnly filingDate,
        DateOnly reportDate,
        ImportContext context
    )
    {
        var commonStockId = target.CommonStockId;
        var shareType = ParseShareType(GetValue(row, "SSHPRNAMTTYPE"));
        var optionType = ParseOptionType(GetValue(row, "PUTCALL"));

        var isAmendment =
            context.CoverPages.TryGetValue(accession, out var cp)
            && string.Equals(cp.IsAmendment, "Y", StringComparison.OrdinalIgnoreCase);

        long ParseLongField(string field) => ParseLong(GetValue(row, field));

        var shares = ParseLongField("SSHPRNAMT");
        // The filed market value is not what gets published (Value is always derived from
        // shares × closing price, the only basis 13D/G positions can share), but it is kept
        // alongside it so the derivation can be audited against its source.
        var reportedValue = ParseLongField("VALUE");
        var votingAuthSole = ParseLongField("VOTING_AUTH_SOLE");
        var votingAuthShared = ParseLongField("VOTING_AUTH_SHARED");
        var votingAuthNone = ParseLongField("VOTING_AUTH_NONE");

        var hasPrice = context.StockPrices.TryGetValue(
            (commonStockId, target.ListedTicker, reportDate),
            out var closePrice
        );
        // The count is quoted as of the report date while the stored price is on today's
        // post-split basis, so the count has to be restated before the two are multiplied. While a
        // captured split is still awaiting its price adjustment the series straddles both bases and
        // no honest value exists — the row stays pending for the repricing lane. For a SECONDARY
        // listing the factor must come from that class's own splits; see HoldingValueBasis for the
        // conservative rule while per-class split capture does not exist yet.
        context.StockSplits.TryGetValue(commonStockId, out var splits);
        context.PrimaryTickers.TryGetValue(commonStockId, out var primaryTicker);
        context.SecondaryTickers.TryGetValue(commonStockId, out var secondaryTickers);
        var basisKnown = HoldingValueBasis.TryResolveShareCountFactor(
            reportDate,
            splits,
            target.ListedTicker,
            primaryTicker,
            secondaryTickers,
            out var shareCountFactor
        );
        // An implausible close (a corrupt bar in the price series) is no price at all: deriving
        // from it once stated a $13.7T position for a $1.09M filing. The row stays pending and the
        // repricing lane values it when the series heals — or falls back to the filed value.
        var canValue =
            hasPrice && basisKnown && !HoldingValueSanityGuard.IsImplausibleClose(closePrice);

        // shares comes from filer-controlled SSHPRNAMT; an oversized count makes the decimal
        // product exceed Int64, so range-check before the cast (mirrors Filing13DGXmlParser)
        // instead of throwing OverflowException and aborting the whole filing's import.
        var product = shares * shareCountFactor * closePrice;
        var value =
            canValue && product >= long.MinValue && product <= long.MaxValue ? (long)product : 0L;
        var valuePending = !canValue;

        // A count bigger than the whole issuer is not a position we can price: the shares are in
        // different units from the price (a depositary-share issuer whose filer reports the
        // underlying ordinary shares), and multiplying them together states a holding worth many
        // times the company. Keep the filer's share count, withhold the valuation, and stop the
        // repricing lane from re-deriving the same wrong figure later.
        var issuerSize =
            context.IssuerSizes != null
            && context.IssuerSizes.TryGetValue(commonStockId, out var size)
                ? size
                : null;
        // Shares outstanding is today's figure, so the comparison needs today's count: an as-filed
        // count from before a reverse split is inflated by the ratio and would read as impossible
        // when it is only old. With the basis unknown there is nothing to compare against, and the
        // row is already pending rather than unpriceable.
        var valueUnavailable =
            issuerSize != null
            && basisKnown
            && ImpossiblePositionGuard.ExceedsTheIssuer(
                SplitAdjustment.AdjustShareCount(shares, shareCountFactor),
                issuerSize.SharesOutstanding,
                issuerSize.MarketCapitalization
            );
        if (valueUnavailable)
        {
            value = 0L;
            valuePending = false;
        }

        // Pre-2023 filings report value in thousands, so the filed figure is only comparable to
        // the derived one after being scaled by its own era.
        var filedDollars =
            reportedValue > 0 ? FiledValueScale.ToDollars(reportedValue, filingDate) : 0m;
        var filedValue =
            filedDollars > 0 && filedDollars <= long.MaxValue ? (long?)filedDollars : null;

        if (value > 0 && filedValue.HasValue)
        {
            context.ValueBasisAudit.Record(
                cusip,
                reportDate,
                shares,
                value,
                filedValue.Value,
                isOption: optionType != null
            );
        }

        // A derivation grossly above the filer's own figure is a basis error (a depositary ratio,
        // a split the series never captured) — the audit above measures it; this acts on it.
        // Publish the filed figure instead of the wrong derivation — unless the disagreement is
        // the ~1,000× signature of a filer still reporting thousands, where the filed figure is
        // the wrong one and the derivation stands.
        var valueSource = ValueSource.Derived;
        if (HoldingValueSanityGuard.ShouldPublishFiledInsteadOfDerived(value, shares, filedValue))
        {
            value = filedValue.Value;
            valueSource = ValueSource.Filed;
        }

        var (otherManagerNumber, sharedManagerNumbers) = ParseOtherManagerAttribution(
            GetValue(row, "OTHERMANAGER")
        );
        var discretion = ParseInvestmentDiscretion(GetValue(row, "INVESTMENTDISCRETION"));

        // The filing type follows the submission's form (13F-HR vs Schedule
        // 13D/13G); fall back to 13F if the submission is somehow missing. The
        // percent-of-class column is only present on the 13D/13G archive — it is
        // absent (null) for 13F INFOTABLE rows.
        var filingType =
            context.Submissions != null
            && context.Submissions.TryGetValue(accession, out var submission)
                ? submission.FormType.ToHoldingsFilingType() ?? FilingType.Form13F
                : FilingType.Form13F;
        var percentOfClass = ParseNullableDecimal(GetValue(row, "PERCENTOFCLASS"));

        var managerEntry = new HoldingManagerEntry
        {
            ManagerNumber = otherManagerNumber,
            SharedManagerNumbers = sharedManagerNumbers,
            ManagerName = ResolveManagerName(context, accession, otherManagerNumber),
            Shares = shares,
            Value = value,
            InvestmentDiscretion = discretion,
        };

        var holding = new InstitutionalHolding
        {
            InstitutionalHolderId = holderId,
            CommonStockId = commonStockId,
            FilingDate = filingDate,
            ReportDate = reportDate,
            Value = value,
            FiledValue = filedValue,
            Shares = shares,
            ShareType = shareType,
            OptionType = optionType,
            InvestmentDiscretion = discretion,
            FilingType = filingType,
            PercentOfClass = percentOfClass,
            VotingAuthSole = votingAuthSole,
            VotingAuthShared = votingAuthShared,
            VotingAuthNone = votingAuthNone,
            TitleOfClass = GetValue(row, "TITLEOFCLASS"),
            Cusip = cusip,
            ListedTicker = target.ListedTicker,
            AccessionNumber = accession,
            IsAmendment = isAmendment,
            ValueUnavailable = valueUnavailable,
            ValuePending = valuePending,
            ValueSource = valueSource,
        };

        return (holding, managerEntry, valuePending, reportedValue);
    }

    private readonly record struct HoldingsFlushResult(int Inserted, bool SkippedStaleParent);

    private async Task<HoldingsFlushResult> FlushBatch(
        List<InstitutionalHolding> holdings,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        // CompanySync can replace a CommonStock after BuildCusipMapping cached its id. Remove
        // those stale children immediately before the write so one dangling FK cannot roll back
        // every valid position in the accession. The caller leaves the data set unprocessed when
        // any row is skipped, allowing the replacement stock to resolve on the next pass.
        var safeHoldings = await stockRepo.FilterByExistingStocks(
            holdings,
            h => h.CommonStockId,
            cancellationToken
        );
        var skipped = holdings.Count - safeHoldings.Count;
        if (skipped > 0)
        {
            _logger.LogWarning(
                "Holdings batch: skipping {Count} rows whose parent CommonStock was removed before flush",
                skipped
            );
        }
        if (safeHoldings.Count == 0)
            return new HoldingsFlushResult(0, SkippedStaleParent: skipped > 0);

        var entriesByKey = new Dictionary<string, List<HoldingManagerEntry>>();
        foreach (var h in safeHoldings)
        {
            entriesByKey[BuildHoldingKey(h)] = h.ManagerEntries.ToList();
            h.ManagerEntries.Clear();
        }

        await dbContext
            .Set<InstitutionalHolding>()
            .UpsertRange(safeHoldings)
            .On(h => new
            {
                h.CommonStockId,
                h.InstitutionalHolderId,
                h.ReportDate,
                h.ShareType,
                h.OptionType,
                h.FilingType,
                h.ListedTicker,
            })
            .WhenMatched(
                (existing, incoming) =>
                    new InstitutionalHolding
                    {
                        Value = incoming.Value,
                        FiledValue = incoming.FiledValue,
                        Shares = incoming.Shares,
                        FilingDate = incoming.FilingDate,
                        // A re-import re-derives the value from scratch, so the previous attempt's
                        // backoff must not carry over: a row that had been given up on would
                        // otherwise be abandoned again without ever being retried.
                        ValueRetryCount = incoming.ValueRetryCount,
                        ValueLastRetryAt = incoming.ValueLastRetryAt,
                        AccessionNumber = incoming.AccessionNumber,
                        InvestmentDiscretion = incoming.InvestmentDiscretion,
                        VotingAuthSole = incoming.VotingAuthSole,
                        VotingAuthShared = incoming.VotingAuthShared,
                        VotingAuthNone = incoming.VotingAuthNone,
                        PercentOfClass = incoming.PercentOfClass,
                        TitleOfClass = incoming.TitleOfClass,
                        Cusip = incoming.Cusip,
                        IsAmendment = incoming.IsAmendment,
                        ValuePending = incoming.ValuePending,
                        ValueUnavailable = incoming.ValueUnavailable,
                        // The label must travel with the figure it describes: without it a
                        // re-import that re-derives a previously Filed row keeps the stale Filed
                        // label (shielding the new derivation from the implausible-derivation
                        // reset), and one that publishes filed over a previously derived row
                        // keeps Derived (exposing the filer's own figure to that same reset).
                        ValueSource = incoming.ValueSource,
                    }
            )
            .RunAsync(cancellationToken);

        var accessions = safeHoldings.Select(h => h.AccessionNumber).Distinct().ToList();
        var dbHoldings = await dbContext
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .Where(h => accessions.Contains(h.AccessionNumber))
            .ToListAsync(cancellationToken);

        foreach (var dbHolding in dbHoldings)
        {
            if (!entriesByKey.TryGetValue(BuildHoldingKey(dbHolding), out var entries))
                continue;

            // Replacing the collection deletes every stored row and re-inserts it, and each
            // re-insert draws a fresh value from the owned type's synthetic key sequence. A
            // re-import re-derives identical attribution for almost every position it revisits,
            // so rewriting unconditionally burned roughly 45 keys per surviving row and
            // exhausted the int sequence — halting the entire lane — five months after the table
            // was created. Rewrite only when the attribution actually changed.
            if (ManagerEntriesMatch(dbHolding.ManagerEntries, entries))
                continue;

            dbHolding.ManagerEntries.Clear();
            dbHolding.ManagerEntries.AddRange(entries);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new HoldingsFlushResult(safeHoldings.Count, SkippedStaleParent: skipped > 0);
    }

    /// <summary>
    /// True when the stored attribution already says exactly what the re-parsed filing says.
    /// Stored rows come back in whatever order Postgres returns them, which need not be the
    /// order the filing was parsed in, so the two sides are compared as multisets rather than
    /// as sequences — otherwise a re-ordered read would be mistaken for a real change.
    /// </summary>
    private static bool ManagerEntriesMatch(
        List<HoldingManagerEntry> stored,
        List<HoldingManagerEntry> incoming
    )
    {
        if (stored.Count != incoming.Count)
            return false;

        var remaining = new Dictionary<ManagerEntryIdentity, int>();
        foreach (var entry in stored)
        {
            var identity = ToIdentity(entry);
            remaining[identity] = remaining.GetValueOrDefault(identity) + 1;
        }

        foreach (var entry in incoming)
        {
            var identity = ToIdentity(entry);
            if (!remaining.TryGetValue(identity, out var count) || count == 0)
                return false;

            remaining[identity] = count - 1;
        }

        return true;
    }

    private static ManagerEntryIdentity ToIdentity(HoldingManagerEntry entry) =>
        new(
            entry.ManagerNumber,
            entry.ManagerName,
            entry.SharedManagerNumbers,
            entry.Shares,
            entry.Value,
            entry.InvestmentDiscretion
        );

    /// <summary>
    /// Every persisted column of a manager entry except the synthetic key. Record-struct
    /// equality compares the names ordinally, so no culture rule can report two different
    /// attributions as the same one and suppress a write that was needed.
    /// </summary>
    private readonly record struct ManagerEntryIdentity(
        int? ManagerNumber,
        string ManagerName,
        string SharedManagerNumbers,
        long Shares,
        long Value,
        InvestmentDiscretion InvestmentDiscretion
    );

    // Recomputes the InstitutionalFiling rollup for every (holder, quarter) this
    // import touched, straight from the resulting holdings. The latest-filings feed
    // reads one indexed row per filing from this table instead of grouping the whole
    // holdings table on each request.
    //
    // The unit of consistency is (holder, report date), not accession: every filing
    // an import mutates — a fresh original, a restatement (delete + reinsert under a
    // new accession), or a "new holdings" amendment that overwrites an existing
    // position's accession — files under the submission's period, so recomputing all
    // filings for the touched (holder, quarter) pairs covers all of them in one pass.
    // Any accession that no longer has holdings (e.g. a restated original) is dropped
    // so it can't ghost the feed. Aggregates count only tracked positions — the same
    // grouping the feed used to do inline.
    private async Task SyncFilingSummaries(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var affected = new HashSet<(Guid HolderId, DateOnly ReportDate)>();
        foreach (var submission in context.Submissions.Values)
        {
            if (string.IsNullOrEmpty(submission.Cik))
                continue;
            if (!context.CikToHolderId.TryGetValue(submission.Cik, out var holderId))
                continue;
            if (!TryParseDateOnly(submission.PeriodOfReport, out var reportDate))
                continue;
            affected.Add((holderId, reportDate));
        }
        if (affected.Count == 0)
            return;

        // The two IN lists widen to a (holder × quarter) cross-product in SQL; the
        // in-memory HashSet narrows the result back to the exact pairs we touched.
        var holderIds = affected.Select(a => a.HolderId).Distinct().ToList();
        var reportDates = affected.Select(a => a.ReportDate).Distinct().ToList();

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var rows = await dbContext
            .Set<InstitutionalHolding>()
            .Where(h =>
                holderIds.Contains(h.InstitutionalHolderId) && reportDates.Contains(h.ReportDate)
            )
            .GroupBy(h => new
            {
                h.AccessionNumber,
                h.InstitutionalHolderId,
                h.FilingDate,
                h.ReportDate,
                h.IsAmendment,
                h.FilingType,
            })
            .Select(g => new
            {
                g.Key.AccessionNumber,
                g.Key.InstitutionalHolderId,
                g.Key.FilingDate,
                g.Key.ReportDate,
                g.Key.IsAmendment,
                g.Key.FilingType,
                PositionCount = g.Count(),
                TotalValue = g.Sum(h => h.Value),
            })
            .ToListAsync(cancellationToken);

        // The upsert conflicts on AccessionNumber alone, and Postgres rejects a
        // statement that touches the same conflict key twice (21000). An accession
        // whose holdings rows disagree on grouping metadata (mixed amendment flag,
        // inconsistent dates) yields several groups, so collapse to the dominant
        // variant per accession instead of aborting the whole data set.
        var summaries = rows.Where(r => affected.Contains((r.InstitutionalHolderId, r.ReportDate)))
            .GroupBy(r => r.AccessionNumber, StringComparer.Ordinal)
            .Select(g =>
                g.OrderByDescending(r => r.PositionCount)
                    .ThenByDescending(r => r.FilingDate)
                    .ThenByDescending(r => r.ReportDate)
                    .ThenByDescending(r => r.IsAmendment)
                    .First()
            )
            .Select(r => new InstitutionalFiling
            {
                AccessionNumber = r.AccessionNumber,
                InstitutionalHolderId = r.InstitutionalHolderId,
                FilingDate = r.FilingDate,
                ReportDate = r.ReportDate,
                IsAmendment = r.IsAmendment,
                FilingType = r.FilingType,
                PositionCount = r.PositionCount,
                TotalValue = r.TotalValue,
                DeclaredPositionCount = context.SummaryPages.TryGetValue(
                    r.AccessionNumber,
                    out var declared
                )
                    ? declared.EntryTotal
                    : null,
                DeclaredTotalValue = context.SummaryPages.TryGetValue(
                    r.AccessionNumber,
                    out var declaredValue
                )
                    ? declaredValue.ValueTotal
                    : null,
            })
            .ToList();

        if (summaries.Count > 0)
        {
            await dbContext
                .Set<InstitutionalFiling>()
                .UpsertRange(summaries)
                .On(f => f.AccessionNumber)
                .WhenMatched(
                    (existing, incoming) =>
                        new InstitutionalFiling
                        {
                            InstitutionalHolderId = incoming.InstitutionalHolderId,
                            FilingDate = incoming.FilingDate,
                            ReportDate = incoming.ReportDate,
                            IsAmendment = incoming.IsAmendment,
                            FilingType = incoming.FilingType,
                            PositionCount = incoming.PositionCount,
                            TotalValue = incoming.TotalValue,
                            DeclaredPositionCount = incoming.DeclaredPositionCount,
                            DeclaredTotalValue = incoming.DeclaredTotalValue,
                        }
                )
                .RunAsync(cancellationToken);
        }

        // Within the touched (holder, quarter) pairs, any existing filing row whose
        // accession no longer has holdings (a restated original) must be removed,
        // otherwise it ghosts the feed.
        var present = summaries.Select(s => s.AccessionNumber).ToHashSet(StringComparer.Ordinal);
        var existingFilings = await dbContext
            .Set<InstitutionalFiling>()
            .Where(f =>
                holderIds.Contains(f.InstitutionalHolderId) && reportDates.Contains(f.ReportDate)
            )
            .ToListAsync(cancellationToken);
        var orphans = existingFilings
            .Where(f =>
                affected.Contains((f.InstitutionalHolderId, f.ReportDate))
                && !present.Contains(f.AccessionNumber)
            )
            .ToList();
        if (orphans.Count > 0)
        {
            dbContext.Set<InstitutionalFiling>().RemoveRange(orphans);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Synced {Count} filing summaries across {Pairs} (holder, quarter) pair(s)",
            summaries.Count,
            affected.Count
        );
    }

    private static string BuildHoldingKey(
        Guid commonStockId,
        Guid institutionalHolderId,
        DateOnly reportDate,
        ShareType shareType,
        OptionType? optionType,
        FilingType filingType,
        string listedTicker
    ) =>
        $"{commonStockId}|{institutionalHolderId}|{reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{(int)shareType}|{optionType?.ToString() ?? ""}|{(int)filingType}|{listedTicker ?? ""}";

    private static string BuildHoldingKey(InstitutionalHolding h) =>
        BuildHoldingKey(
            h.CommonStockId,
            h.InstitutionalHolderId,
            h.ReportDate,
            h.ShareType,
            h.OptionType,
            h.FilingType,
            h.ListedTicker
        );

    private static bool IsYes(string raw) =>
        !string.IsNullOrEmpty(raw)
        && (
            raw.Equals("Y", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
            || raw == "1"
        );
}
