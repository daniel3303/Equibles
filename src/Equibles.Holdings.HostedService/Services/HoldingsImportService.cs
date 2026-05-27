using System.IO.Compression;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.Repositories;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Equibles.Holdings.HostedService.Services.HoldingsParsingHelper;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsImportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldingsImportService> _logger;
    private readonly WorkerOptions _workerOptions;
    private readonly IStockPriceProvider _stockPriceProvider;

    public HoldingsImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldingsImportService> logger,
        IOptions<WorkerOptions> workerOptions,
        IStockPriceProvider stockPriceProvider
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _workerOptions = workerOptions.Value;
        _stockPriceProvider = stockPriceProvider;
    }

    public async Task<ImportResult> ImportDataSet(
        ZipArchive archive,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        var context = new ImportContext
        {
            TsvParser = new TsvParser(),
            Archive = archive,
            MinReportDate = minReportDate,
        };

        var parseResult = await ParseSubmissions(context, cancellationToken);
        if (parseResult == null)
            return new ImportResult(0, IsComplete: false);
        if (parseResult == false)
            return new ImportResult(0, IsComplete: true);
        DeduplicateSubmissions(context);
        var submissionCount = context.Submissions.Count;
        if (!await ParseCoverPages(context, cancellationToken))
            return new ImportResult(submissionCount, IsComplete: false);
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
        await ParseOtherManagers(context, cancellationToken);
        await UpsertInstitutionalHolders(context, cancellationToken);
        await HandleAmendments(context, cancellationToken);
        await StreamAndInsertHoldings(context, cancellationToken);
        return new ImportResult(submissionCount, IsComplete: true);
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
        var submissionEntry = FindEntry(context.Archive, "SUBMISSION.tsv");
        if (submissionEntry == null)
        {
            _logger.LogWarning("SUBMISSION.tsv not found in archive");
            return null;
        }

        var submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in context.TsvParser.ParseEntry(submissionEntry))
        {
            if (TryParseSubmissionRow(row, context.MinReportDate, out var submission))
                submissions[submission.AccessionNumber] = submission;
        }

        if (submissions.Count == 0)
        {
            _logger.LogInformation("No 13F-HR submissions found in data set");
            return false;
        }

        _logger.LogInformation("Found {Count} 13F-HR submissions", submissions.Count);
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
        if (formType is not ("13F-HR" or "13F-HR/A"))
            return false;

        var accession = GetValue(row, "ACCESSION_NUMBER");
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
            // FilingDate is day-granular (and, on the real-time path, derived
            // from the daily-index date), so an original and its same-day
            // amendment can tie. Break ties by accession number — SEC assigns
            // these monotonically per filer agent, so the lexicographically
            // greatest accession is the later submission. Without this the
            // winner is nondeterministic and an amendment can be dropped.
            var latest = group
                .OrderByDescending(s => s.FilingDate, StringComparer.Ordinal)
                .ThenByDescending(s => s.AccessionNumber, StringComparer.Ordinal)
                .First();

            foreach (var s in group.Where(s => s.AccessionNumber != latest.AccessionNumber))
            {
                superseded.Add(s.AccessionNumber);
            }
        }

        foreach (var accession in superseded)
        {
            context.Submissions.Remove(accession);
        }
    }

    private async Task<bool> ParseCoverPages(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var coverPageEntry = FindEntry(context.Archive, "COVERPAGE.tsv");
        if (coverPageEntry == null)
        {
            _logger.LogWarning("COVERPAGE.tsv not found in archive");
            return false;
        }

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

        var accession = Get("ACCESSION_NUMBER");
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
        var infoTableEntry = FindEntry(context.Archive, "INFOTABLE.tsv");
        if (infoTableEntry == null)
        {
            _logger.LogWarning("INFOTABLE.tsv not found in archive");
            return CusipMappingOutcome.NoInfoTable;
        }

        var uniqueCusips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in context.TsvParser.ParseEntry(infoTableEntry))
        {
            var accession = GetValue(row, "ACCESSION_NUMBER");
            if (!context.Submissions.ContainsKey(accession))
                continue;

            var cusip = GetValue(row, "CUSIP");
            if (!string.IsNullOrEmpty(cusip))
            {
                uniqueCusips.Add(cusip);
            }
        }

        _logger.LogInformation("Found {Count} unique CUSIPs in INFOTABLE", uniqueCusips.Count);

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var uniqueCusipsList = uniqueCusips.ToList();

        var query =
            _workerOptions.TickersToSync?.Count > 0
                ? stockRepo.GetByTickers(_workerOptions.TickersToSync)
                : stockRepo.GetAll();

        var stocksWithCusip = await query
            .Where(cs => cs.Cusip != null && uniqueCusipsList.Contains(cs.Cusip))
            .Select(cs => new { cs.Id, cs.Cusip })
            .ToListAsync(cancellationToken);

        var cusipMapping = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var stock in stocksWithCusip)
        {
            cusipMapping[stock.Cusip] = stock.Id;
        }

        _logger.LogInformation(
            "Mapped {Count} CUSIPs to tracked stocks (out of {Total} in data set)",
            cusipMapping.Count,
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
        return CusipMappingOutcome.Mapped;
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

        var stockIds = context.CusipMapping.Values.Distinct().ToList();

        var requests = reportDates.SelectMany(date => stockIds.Select(id => (id, date))).ToList();

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

        var existingHolders = await holderRepo.GetByCiks(allCiks).ToListAsync(cancellationToken);
        foreach (var holder in existingHolders)
        {
            cikToHolderId[holder.Cik] = holder.Id;
        }

        foreach (var holder in existingHolders)
        {
            var submission = context.Submissions.Values.FirstOrDefault(s =>
                string.Equals(s.Cik, holder.Cik, StringComparison.OrdinalIgnoreCase)
            );
            if (submission == null)
                continue;
            context.CoverPages.TryGetValue(submission.AccessionNumber, out var cp);
            if (cp != null)
                holder.ConfidentialTreatmentRequested = IsYes(cp.ConfidentialTreatment);
        }

        var existingCiks = existingHolders
            .Select(h => h.Cik)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var submission in context.Submissions.Values)
        {
            if (string.IsNullOrEmpty(submission.Cik) || existingCiks.Contains(submission.Cik))
                continue;

            context.CoverPages.TryGetValue(submission.AccessionNumber, out var coverPage);

            var holder = new InstitutionalHolder
            {
                Cik = submission.Cik,
                Name = coverPage?.CompanyName,
                City = coverPage?.City,
                StateOrCountry = coverPage?.StateOrCountry,
                Form13FFileNumber = coverPage?.Form13FFileNumber,
                CrdNumber = coverPage?.CrdNumber,
                Classification = FundClassifierService.Classify(coverPage?.CompanyName),
                ConfidentialTreatmentRequested = IsYes(coverPage?.ConfidentialTreatment),
            };

            holderRepo.Add(holder);
            cikToHolderId[submission.Cik] = holder.Id;
            existingCiks.Add(submission.Cik);
        }

        await holderRepo.SaveChanges();

        _logger.LogInformation("Upserted {Count} institutional holders", cikToHolderId.Count);
        context.CikToHolderId = cikToHolderId;
    }

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

        var managers = new Dictionary<string, Dictionary<int, string>>(
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
                    out var name
                )
            )
                continue;

            if (!managers.TryGetValue(accession, out var seqMap))
            {
                seqMap = [];
                managers[accession] = seqMap;
            }

            seqMap[seq] = name;
        }

        context.OtherManagers = managers;
        _logger.LogInformation("Parsed other-manager mappings for {Count} filings", managers.Count);
    }

    private bool TryParseOtherManagerRow(
        Dictionary<string, string> row,
        Dictionary<string, SubmissionRow> submissions,
        out string accession,
        out int seq,
        out string name
    )
    {
        seq = 0;
        name = null;

        accession = GetValue(row, "ACCESSION_NUMBER");
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

        name = GetValue(row, "NAME");
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return true;
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
                    out var reportDate
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

            var existingHoldings = await holdingRepo
                .GetAll()
                .Where(h => h.InstitutionalHolderId == holderId && h.ReportDate == reportDate)
                .ToListAsync(cancellationToken);

            if (existingHoldings.Count > 0)
            {
                holdingRepo.Delete(existingHoldings);
                _logger.LogInformation(
                    "Deleted {Count} holdings for RESTATEMENT amendment {Accession}",
                    existingHoldings.Count,
                    accession
                );
            }
        }

        await holdingRepo.SaveChanges();
    }

    private static bool IsNewHoldingsAmendment(string accession, ImportContext context)
    {
        return context.CoverPages.TryGetValue(accession, out var coverPage)
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
        out DateOnly reportDate
    )
    {
        holderId = default;
        reportDate = default;

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

        return true;
    }

    private async Task StreamAndInsertHoldings(
        ImportContext context,
        CancellationToken cancellationToken
    )
    {
        var infoTableEntry = FindEntry(context.Archive, "INFOTABLE.tsv");
        var holdingsMap = new Dictionary<string, InstitutionalHolding>();
        var totalInserted = 0;
        var totalSkipped = 0;
        var totalDuplicates = 0;
        var totalPending = 0;
        string currentAccession = null;

        await foreach (var row in context.TsvParser.ParseEntry(infoTableEntry))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var accession = GetValue(row, "ACCESSION_NUMBER");

            // Flush at the accession boundary, not at a fixed row count. Every
            // row sharing an upsert key inside one filing lives in that filing's
            // INFOTABLE section (a holder splits a position across otherManager
            // codes so the same security can appear several times with rows
            // scattered hundreds apart). FlushBatch's WhenMatched clause
            // REPLACES — so if a key's rows fall in different flushes, only
            // the last one's sum survives. SEC orders the bulk INFOTABLE by
            // INFOTABLE_SK and the realtime archive by XML element order, so
            // a single accession's rows are always contiguous; flushing only
            // when the accession changes guarantees in-memory aggregation
            // finishes before any UPSERT for that key runs.
            if (currentAccession != null && accession != currentAccession && holdingsMap.Count > 0)
            {
                totalInserted += await FlushBatch(holdingsMap.Values.ToList(), cancellationToken);
                holdingsMap.Clear();
            }
            currentAccession = accession;

            if (!context.Submissions.TryGetValue(accession, out var submission))
                continue;

            var cusip = GetValue(row, "CUSIP");
            if (!context.CusipMapping.TryGetValue(cusip, out var commonStockId))
            {
                totalSkipped++;
                continue;
            }

            if (!context.CikToHolderId.TryGetValue(submission.Cik, out var holderId))
                continue;

            TryParseDateOnly(submission.FilingDate, out var filingDate);
            TryParseDateOnly(submission.PeriodOfReport, out var reportDate);

            var (holding, managerEntry, valuePending) = ParseHoldingRow(
                row,
                accession,
                cusip,
                commonStockId,
                holderId,
                filingDate,
                reportDate,
                context
            );
            var uniqueKey = BuildHoldingKey(holding);

            if (holdingsMap.TryGetValue(uniqueKey, out var existing))
            {
                totalDuplicates++;
                existing.Shares += holding.Shares;
                existing.Value += holding.Value;
                existing.VotingAuthSole += holding.VotingAuthSole;
                existing.VotingAuthShared += holding.VotingAuthShared;
                existing.VotingAuthNone += holding.VotingAuthNone;
                existing.ManagerEntries.Add(managerEntry);
            }
            else
            {
                if (valuePending)
                    totalPending++;

                holding.ManagerEntries.Add(managerEntry);
                holdingsMap[uniqueKey] = holding;
            }
        }

        if (holdingsMap.Count > 0)
        {
            totalInserted += await FlushBatch(holdingsMap.Values.ToList(), cancellationToken);
            holdingsMap.Clear();
        }

        _logger.LogInformation(
            "Import complete. Inserted: {Inserted}, Skipped (untracked): {Skipped}, Duplicates: {Duplicates}, Pending price: {Pending}",
            totalInserted,
            totalSkipped,
            totalDuplicates,
            totalPending
        );
    }

    private static (
        InstitutionalHolding Holding,
        HoldingManagerEntry ManagerEntry,
        bool ValuePending
    ) ParseHoldingRow(
        Dictionary<string, string> row,
        string accession,
        string cusip,
        Guid commonStockId,
        Guid holderId,
        DateOnly filingDate,
        DateOnly reportDate,
        ImportContext context
    )
    {
        var shareType = ParseShareType(GetValue(row, "SSHPRNAMTTYPE"));
        var optionType = ParseOptionType(GetValue(row, "PUTCALL"));

        var isAmendment =
            context.CoverPages.TryGetValue(accession, out var cp)
            && string.Equals(cp.IsAmendment, "Y", StringComparison.OrdinalIgnoreCase);

        long ParseLongField(string field) => ParseLong(GetValue(row, field));

        var shares = ParseLongField("SSHPRNAMT");
        var votingAuthSole = ParseLongField("VOTING_AUTH_SOLE");
        var votingAuthShared = ParseLongField("VOTING_AUTH_SHARED");
        var votingAuthNone = ParseLongField("VOTING_AUTH_NONE");

        var hasPrice = context.StockPrices.TryGetValue(
            (commonStockId, reportDate),
            out var closePrice
        );
        var value = hasPrice ? (long)(shares * closePrice) : 0L;
        var valuePending = !hasPrice;

        var otherManagerNumber = ParseNullableInt(GetValue(row, "OTHERMANAGER"));
        var discretion = ParseInvestmentDiscretion(GetValue(row, "INVESTMENTDISCRETION"));

        var managerEntry = new HoldingManagerEntry
        {
            ManagerNumber = otherManagerNumber,
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
            Shares = shares,
            ShareType = shareType,
            OptionType = optionType,
            InvestmentDiscretion = discretion,
            FilingType = FilingType.Form13F,
            VotingAuthSole = votingAuthSole,
            VotingAuthShared = votingAuthShared,
            VotingAuthNone = votingAuthNone,
            TitleOfClass = GetValue(row, "TITLEOFCLASS"),
            Cusip = cusip,
            AccessionNumber = accession,
            IsAmendment = isAmendment,
            ValuePending = valuePending,
        };

        return (holding, managerEntry, valuePending);
    }

    private async Task<int> FlushBatch(
        List<InstitutionalHolding> holdings,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();

        var entriesByKey = new Dictionary<string, List<HoldingManagerEntry>>();
        foreach (var h in holdings)
        {
            entriesByKey[BuildHoldingKey(h)] = h.ManagerEntries.ToList();
            h.ManagerEntries.Clear();
        }

        await dbContext
            .Set<InstitutionalHolding>()
            .UpsertRange(holdings)
            .On(h => new
            {
                h.CommonStockId,
                h.InstitutionalHolderId,
                h.ReportDate,
                h.ShareType,
                h.OptionType,
                h.FilingType,
            })
            .WhenMatched(
                (existing, incoming) =>
                    new InstitutionalHolding
                    {
                        Value = incoming.Value,
                        Shares = incoming.Shares,
                        FilingDate = incoming.FilingDate,
                        AccessionNumber = incoming.AccessionNumber,
                        InvestmentDiscretion = incoming.InvestmentDiscretion,
                        VotingAuthSole = incoming.VotingAuthSole,
                        VotingAuthShared = incoming.VotingAuthShared,
                        VotingAuthNone = incoming.VotingAuthNone,
                        TitleOfClass = incoming.TitleOfClass,
                        Cusip = incoming.Cusip,
                        IsAmendment = incoming.IsAmendment,
                        ValuePending = incoming.ValuePending,
                    }
            )
            .RunAsync(cancellationToken);

        var accessions = holdings.Select(h => h.AccessionNumber).Distinct().ToList();
        var dbHoldings = await dbContext
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .Where(h => accessions.Contains(h.AccessionNumber))
            .ToListAsync(cancellationToken);

        foreach (var dbHolding in dbHoldings)
        {
            if (entriesByKey.TryGetValue(BuildHoldingKey(dbHolding), out var entries))
            {
                dbHolding.ManagerEntries.Clear();
                dbHolding.ManagerEntries.AddRange(entries);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return holdings.Count;
    }

    private static string BuildHoldingKey(
        Guid commonStockId,
        Guid institutionalHolderId,
        DateOnly reportDate,
        ShareType shareType,
        OptionType? optionType,
        FilingType filingType
    ) =>
        $"{commonStockId}|{institutionalHolderId}|{reportDate}|{(int)shareType}|{optionType?.ToString() ?? ""}|{(int)filingType}";

    private static string BuildHoldingKey(InstitutionalHolding h) =>
        BuildHoldingKey(
            h.CommonStockId,
            h.InstitutionalHolderId,
            h.ReportDate,
            h.ShareType,
            h.OptionType,
            h.FilingType
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
