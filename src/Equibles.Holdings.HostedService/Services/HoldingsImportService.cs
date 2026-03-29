using System.IO.Compression;
using Equibles.Errors.BusinessLogic;
using Equibles.Data;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.HostedService.Models;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Equibles.Holdings.HostedService.Services.HoldingsParsingHelper;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsImportService {
    private const int InsertBatchSize = 1000;
    private const int MaxConsecutiveEmptyBatches = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldingsImportService> _logger;
    private readonly WorkerOptions _workerOptions;
    private readonly IStockPriceProvider _stockPriceProvider;

    public HoldingsImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldingsImportService> logger,
        IOptions<WorkerOptions> workerOptions,
        IStockPriceProvider stockPriceProvider
    ) {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _workerOptions = workerOptions.Value;
        _stockPriceProvider = stockPriceProvider;
    }

    public async Task ImportDataSet(ZipArchive archive, DateOnly minReportDate, CancellationToken cancellationToken) {
        var context = new ImportContext {
            TsvParser = new TsvParser(),
            Archive = archive,
            MinReportDate = minReportDate,
        };

        if (!await ParseSubmissions(context, cancellationToken)) return;
        DeduplicateSubmissions(context);
        if (await IsAlreadyImported(context, cancellationToken)) return;
        if (!await ParseCoverPages(context, cancellationToken)) return;
        if (!await BuildCusipMapping(context, cancellationToken)) return;
        await BuildPriceMap(context, cancellationToken);
        await ParseOtherManagers(context, cancellationToken);
        await UpsertInstitutionalHolders(context, cancellationToken);
        await HandleAmendments(context, cancellationToken);
        await StreamAndInsertHoldings(context, cancellationToken);
    }

    private async Task<bool> ParseSubmissions(ImportContext context, CancellationToken cancellationToken) {
        var submissionEntry = FindEntry(context.Archive, "SUBMISSION.tsv");
        if (submissionEntry == null) {
            _logger.LogWarning("SUBMISSION.tsv not found in archive");
            return false;
        }

        var submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in context.TsvParser.ParseEntry(submissionEntry)) {
            var formType = GetValue(row, "SUBMISSIONTYPE");
            if (formType is not ("13F-HR" or "13F-HR/A")) continue;

            var accession = GetValue(row, "ACCESSION_NUMBER");
            if (string.IsNullOrEmpty(accession)) continue;

            var periodOfReport = GetValue(row, "PERIODOFREPORT");
            if (TryParseDateOnly(periodOfReport, out var reportDateCheck) && reportDateCheck < context.MinReportDate) {
                continue;
            }

            submissions[accession] = new SubmissionRow {
                AccessionNumber = accession,
                FilingDate = GetValue(row, "FILING_DATE"),
                PeriodOfReport = periodOfReport,
                FormType = formType,
                Cik = GetValue(row, "CIK"),
            };
        }

        if (submissions.Count == 0) {
            _logger.LogInformation("No 13F-HR submissions found in data set");
            return false;
        }

        _logger.LogInformation("Found {Count} 13F-HR submissions", submissions.Count);
        context.Submissions = submissions;
        return true;
    }

    internal static void DeduplicateSubmissions(ImportContext context) {
        var superseded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byCikAndPeriod = context.Submissions.Values
            .Where(s => !string.IsNullOrEmpty(s.Cik) && !string.IsNullOrEmpty(s.PeriodOfReport))
            .GroupBy(s => $"{s.Cik}|{s.PeriodOfReport}")
            .Where(g => g.Count() > 1);

        foreach (var group in byCikAndPeriod) {
            var latest = group
                .OrderByDescending(s => s.FilingDate)
                .First();

            foreach (var s in group.Where(s => s.AccessionNumber != latest.AccessionNumber)) {
                superseded.Add(s.AccessionNumber);
            }
        }

        foreach (var accession in superseded) {
            context.Submissions.Remove(accession);
        }
    }

    private async Task<bool> IsAlreadyImported(ImportContext context, CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var holdingRepo = scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();

        var allAccessions = context.Submissions.Keys.ToList();
        var sampleSize = Math.Min(20, allAccessions.Count);
        var random = new Random();
        var sampleAccessions = allAccessions.OrderBy(_ => random.Next()).Take(sampleSize).ToList();

        var matchedCount = await holdingRepo.GetAll()
            .Where(h => sampleAccessions.Contains(h.AccessionNumber))
            .Select(h => h.AccessionNumber)
            .Distinct()
            .CountAsync(cancellationToken);

        var threshold = (int)Math.Ceiling(sampleSize * 0.8);
        if (matchedCount >= threshold) {
            _logger.LogInformation(
                "Data set appears already imported ({Matched}/{Sample} accessions found, threshold {Threshold}), skipping",
                matchedCount, sampleSize, threshold);
            return true;
        }

        _logger.LogInformation(
            "Already-imported check: {Matched}/{Sample} accessions found (threshold {Threshold}), proceeding with import",
            matchedCount, sampleSize, threshold);
        return false;
    }

    private async Task<bool> ParseCoverPages(ImportContext context, CancellationToken cancellationToken) {
        var coverPageEntry = FindEntry(context.Archive, "COVERPAGE.tsv");
        if (coverPageEntry == null) {
            _logger.LogWarning("COVERPAGE.tsv not found in archive");
            return false;
        }

        var coverPages = new Dictionary<string, CoverPageRow>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in context.TsvParser.ParseEntry(coverPageEntry)) {
            var accession = GetValue(row, "ACCESSION_NUMBER");
            if (string.IsNullOrEmpty(accession) || !context.Submissions.ContainsKey(accession)) continue;

            coverPages[accession] = new CoverPageRow {
                AccessionNumber = accession,
                IsAmendment = GetValue(row, "ISAMENDMENT"),
                CompanyName = GetValue(row, "FILINGMANAGER_NAME"),
                City = GetValue(row, "FILINGMANAGER_CITY"),
                StateOrCountry = GetValue(row, "FILINGMANAGER_STATEORCOUNTRY"),
                Form13FFileNumber = GetValue(row, "FORM13FFILENUMBER"),
                CrdNumber = GetValue(row, "CRDNUMBER"),
            };
        }

        _logger.LogInformation("Parsed {Count} cover pages", coverPages.Count);
        context.CoverPages = coverPages;
        return true;
    }

    private async Task<bool> BuildCusipMapping(ImportContext context, CancellationToken cancellationToken) {
        var infoTableEntry = FindEntry(context.Archive, "INFOTABLE.tsv");
        if (infoTableEntry == null) {
            _logger.LogWarning("INFOTABLE.tsv not found in archive");
            return false;
        }

        var uniqueCusips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in context.TsvParser.ParseEntry(infoTableEntry)) {
            var accession = GetValue(row, "ACCESSION_NUMBER");
            if (!context.Submissions.ContainsKey(accession)) continue;

            var cusip = GetValue(row, "CUSIP");
            if (!string.IsNullOrEmpty(cusip)) {
                uniqueCusips.Add(cusip);
            }
        }

        _logger.LogInformation("Found {Count} unique CUSIPs in INFOTABLE", uniqueCusips.Count);

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var uniqueCusipsList = uniqueCusips.ToList();

        var query = _workerOptions.TickersToSync?.Count > 0
            ? stockRepo.GetByTickers(_workerOptions.TickersToSync)
            : stockRepo.GetAll();

        var stocksWithCusip = await query
            .Where(cs => cs.Cusip != null && uniqueCusipsList.Contains(cs.Cusip))
            .Select(cs => new { cs.Id, cs.Cusip })
            .ToListAsync(cancellationToken);

        var cusipMapping = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var stock in stocksWithCusip) {
            cusipMapping[stock.Cusip] = stock.Id;
        }

        _logger.LogInformation("Mapped {Count} CUSIPs to tracked stocks (out of {Total} in data set)",
            cusipMapping.Count, uniqueCusips.Count);

        if (cusipMapping.Count == 0) {
            _logger.LogInformation("No tracked stocks found for this data set, skipping");
            return false;
        }

        context.CusipMapping = cusipMapping;
        return true;
    }

    /// <summary>
    /// Pre-fetches Yahoo closing prices for all (stock, reportDate) pairs in this dataset.
    /// Holdings without an available price will be marked as ValuePending during import.
    /// </summary>
    private async Task BuildPriceMap(ImportContext context, CancellationToken cancellationToken) {
        var reportDates = context.Submissions.Values
            .Select(s => s.PeriodOfReport)
            .Where(p => TryParseDateOnly(p, out _))
            .Select(p => { TryParseDateOnly(p, out var d); return d; })
            .Distinct()
            .ToList();

        var stockIds = context.CusipMapping.Values.Distinct().ToList();

        var requests = reportDates
            .SelectMany(date => stockIds.Select(id => (id, date)))
            .ToList();

        context.StockPrices = await _stockPriceProvider.GetClosingPrices(requests, cancellationToken);

        _logger.LogInformation(
            "Fetched Yahoo prices for {Found}/{Requested} (stock, date) pairs",
            context.StockPrices.Count, requests.Count);
    }

    private async Task UpsertInstitutionalHolders(ImportContext context, CancellationToken cancellationToken) {
        var cikToHolderId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        using var scope = _scopeFactory.CreateScope();
        var holderRepo = scope.ServiceProvider.GetRequiredService<InstitutionalHolderRepository>();

        var allCiks = context.Submissions.Values
            .Where(s => !string.IsNullOrEmpty(s.Cik))
            .Select(s => s.Cik)
            .Distinct()
            .ToList();

        var existingHolders = await holderRepo.GetByCiks(allCiks).ToListAsync(cancellationToken);
        foreach (var holder in existingHolders) {
            cikToHolderId[holder.Cik] = holder.Id;
        }

        var existingCiks = existingHolders.Select(h => h.Cik).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var submission in context.Submissions.Values) {
            if (string.IsNullOrEmpty(submission.Cik) || existingCiks.Contains(submission.Cik)) continue;

            context.CoverPages.TryGetValue(submission.AccessionNumber, out var coverPage);

            var holder = new InstitutionalHolder {
                Cik = submission.Cik,
                Name = coverPage?.CompanyName,
                City = coverPage?.City,
                StateOrCountry = coverPage?.StateOrCountry,
                Form13FFileNumber = coverPage?.Form13FFileNumber,
                CrdNumber = coverPage?.CrdNumber,
            };

            holderRepo.Add(holder);
            cikToHolderId[submission.Cik] = holder.Id;
            existingCiks.Add(submission.Cik);
        }

        await holderRepo.SaveChanges();

        _logger.LogInformation("Upserted {Count} institutional holders", cikToHolderId.Count);
        context.CikToHolderId = cikToHolderId;
    }

    private async Task ParseOtherManagers(ImportContext context, CancellationToken cancellationToken) {
        var entry = FindEntry(context.Archive, "OTHERMANAGER2.tsv");
        if (entry == null) {
            _logger.LogInformation("OTHERMANAGER2.tsv not found, skipping other-manager parsing");
            return;
        }

        var managers = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        await foreach (var row in context.TsvParser.ParseEntry(entry)) {
            var accession = GetValue(row, "ACCESSION_NUMBER");
            if (string.IsNullOrEmpty(accession) || !context.Submissions.ContainsKey(accession)) continue;

            var seqStr = GetValue(row, "SEQUENCENUMBER");
            if (!int.TryParse(seqStr, out var seq)) {
                _logger.LogDebug("Failed to parse sequence number '{SeqStr}' in OTHERMANAGER2.tsv", seqStr);
                continue;
            }

            var name = GetValue(row, "NAME");
            if (string.IsNullOrEmpty(name)) continue;

            if (!managers.TryGetValue(accession, out var seqMap)) {
                seqMap = [];
                managers[accession] = seqMap;
            }

            seqMap[seq] = name;
        }

        context.OtherManagers = managers;
        _logger.LogInformation("Parsed other-manager mappings for {Count} filings", managers.Count);
    }

    private async Task HandleAmendments(ImportContext context, CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var holdingRepo = scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();

        foreach (var (accession, submission) in context.Submissions) {
            if (!context.CoverPages.TryGetValue(accession, out var coverPage)) continue;
            if (!string.Equals(coverPage.IsAmendment, "Y", StringComparison.OrdinalIgnoreCase)) continue;
            if (!context.CikToHolderId.TryGetValue(submission.Cik, out var holderId)) continue;
            if (!TryParseDateOnly(submission.PeriodOfReport, out var reportDate)) continue;

            var existingHoldings = await holdingRepo.GetAll()
                .Where(h => h.InstitutionalHolderId == holderId && h.ReportDate == reportDate)
                .ToListAsync(cancellationToken);

            if (existingHoldings.Count > 0) {
                holdingRepo.Delete(existingHoldings);
                _logger.LogInformation(
                    "Deleted {Count} holdings for amendment {Accession}",
                    existingHoldings.Count, accession);
            }
        }

        await holdingRepo.SaveChanges();
    }

    private async Task StreamAndInsertHoldings(ImportContext context, CancellationToken cancellationToken) {
        var infoTableEntry = FindEntry(context.Archive, "INFOTABLE.tsv");
        var holdingsMap = new Dictionary<string, InstitutionalHolding>();
        var totalInserted = 0;
        var totalSkipped = 0;
        var totalDuplicates = 0;
        var totalPending = 0;
        var consecutiveEmptyBatches = 0;

        await foreach (var row in context.TsvParser.ParseEntry(infoTableEntry)) {
            cancellationToken.ThrowIfCancellationRequested();

            var accession = GetValue(row, "ACCESSION_NUMBER");
            if (!context.Submissions.TryGetValue(accession, out var submission)) continue;

            var cusip = GetValue(row, "CUSIP");
            if (!context.CusipMapping.TryGetValue(cusip, out var commonStockId)) {
                totalSkipped++;
                continue;
            }

            if (!context.CikToHolderId.TryGetValue(submission.Cik, out var holderId)) continue;

            TryParseDateOnly(submission.FilingDate, out var filingDate);
            TryParseDateOnly(submission.PeriodOfReport, out var reportDate);

            var shareType = ParseShareType(GetValue(row, "SSHPRNAMTTYPE"));
            var optionType = ParseOptionType(GetValue(row, "PUTCALL"));
            var uniqueKey = $"{commonStockId}|{holderId}|{reportDate}|{(int)shareType}|{optionType?.ToString() ?? ""}";

            var isAmendment = context.CoverPages.TryGetValue(accession, out var cp)
                && string.Equals(cp.IsAmendment, "Y", StringComparison.OrdinalIgnoreCase);

            var shares = ParseLong(GetValue(row, "SSHPRNAMT"));

            // Calculate value from Yahoo stock price
            var hasPrice = context.StockPrices.TryGetValue((commonStockId, reportDate), out var closePrice);
            var value = hasPrice ? (long)(shares * closePrice) : 0L;
            var valuePending = !hasPrice;

            var otherManagerNumber = ParseNullableInt(GetValue(row, "OTHERMANAGER"));
            var discretion = ParseInvestmentDiscretion(GetValue(row, "INVESTMENTDISCRETION"));

            var managerEntry = new HoldingManagerEntry {
                ManagerNumber = otherManagerNumber,
                ManagerName = ResolveManagerName(context, accession, otherManagerNumber),
                Shares = shares,
                Value = value,
                InvestmentDiscretion = discretion,
            };

            if (holdingsMap.TryGetValue(uniqueKey, out var existing)) {
                totalDuplicates++;
                existing.Shares += shares;
                existing.Value += value;
                existing.VotingAuthSole += ParseLong(GetValue(row, "VOTING_AUTH_SOLE"));
                existing.VotingAuthShared += ParseLong(GetValue(row, "VOTING_AUTH_SHARED"));
                existing.VotingAuthNone += ParseLong(GetValue(row, "VOTING_AUTH_NONE"));
                existing.ManagerEntries.Add(managerEntry);
            } else {
                if (valuePending) totalPending++;

                var holding = new InstitutionalHolding {
                    InstitutionalHolderId = holderId,
                    CommonStockId = commonStockId,
                    FilingDate = filingDate,
                    ReportDate = reportDate,
                    Value = value,
                    Shares = shares,
                    ShareType = shareType,
                    OptionType = optionType,
                    InvestmentDiscretion = discretion,
                    VotingAuthSole = ParseLong(GetValue(row, "VOTING_AUTH_SOLE")),
                    VotingAuthShared = ParseLong(GetValue(row, "VOTING_AUTH_SHARED")),
                    VotingAuthNone = ParseLong(GetValue(row, "VOTING_AUTH_NONE")),
                    TitleOfClass = GetValue(row, "TITLEOFCLASS"),
                    Cusip = cusip,
                    AccessionNumber = accession,
                    IsAmendment = isAmendment,
                    ValuePending = valuePending,
                };
                holding.ManagerEntries.Add(managerEntry);
                holdingsMap[uniqueKey] = holding;
            }

            if (holdingsMap.Count >= InsertBatchSize) {
                var inserted = await FlushBatch(holdingsMap.Values.ToList(), cancellationToken);
                totalInserted += inserted;
                holdingsMap.Clear();

                if (inserted == 0) {
                    consecutiveEmptyBatches++;
                    if (consecutiveEmptyBatches >= MaxConsecutiveEmptyBatches) {
                        _logger.LogInformation(
                            "Stopping early: {Count} consecutive batches had no new holdings — data set appears fully imported",
                            consecutiveEmptyBatches);
                        break;
                    }
                } else {
                    consecutiveEmptyBatches = 0;
                }
            }
        }

        if (holdingsMap.Count > 0) {
            var inserted = await FlushBatch(holdingsMap.Values.ToList(), cancellationToken);
            totalInserted += inserted;
            holdingsMap.Clear();
        }

        _logger.LogInformation(
            "Import complete. Inserted: {Inserted}, Skipped (untracked): {Skipped}, Duplicates: {Duplicates}, Pending price: {Pending}",
            totalInserted, totalSkipped, totalDuplicates, totalPending);
    }

    private async Task<int> FlushBatch(List<InstitutionalHolding> holdings, CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

        var entriesByKey = new Dictionary<string, List<HoldingManagerEntry>>();
        foreach (var h in holdings) {
            var key = $"{h.CommonStockId}|{h.InstitutionalHolderId}|{h.ReportDate}|{(int)h.ShareType}|{h.OptionType?.ToString() ?? ""}";
            entriesByKey[key] = h.ManagerEntries.ToList();
            h.ManagerEntries.Clear();
        }

        await dbContext.Set<InstitutionalHolding>()
            .UpsertRange(holdings)
            .On(h => new { h.CommonStockId, h.InstitutionalHolderId, h.ReportDate, h.ShareType, h.OptionType })
            .WhenMatched(h => new InstitutionalHolding {
                Value = h.Value,
                Shares = h.Shares,
                FilingDate = h.FilingDate,
                AccessionNumber = h.AccessionNumber,
                InvestmentDiscretion = h.InvestmentDiscretion,
                VotingAuthSole = h.VotingAuthSole,
                VotingAuthShared = h.VotingAuthShared,
                VotingAuthNone = h.VotingAuthNone,
                TitleOfClass = h.TitleOfClass,
                Cusip = h.Cusip,
                IsAmendment = h.IsAmendment,
                ValuePending = h.ValuePending,
            })
            .RunAsync(cancellationToken);

        var accessions = holdings.Select(h => h.AccessionNumber).Distinct().ToList();
        var dbHoldings = await dbContext.Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .Where(h => accessions.Contains(h.AccessionNumber))
            .ToListAsync(cancellationToken);

        foreach (var dbHolding in dbHoldings) {
            var key = $"{dbHolding.CommonStockId}|{dbHolding.InstitutionalHolderId}|{dbHolding.ReportDate}|{(int)dbHolding.ShareType}|{dbHolding.OptionType?.ToString() ?? ""}";
            if (entriesByKey.TryGetValue(key, out var entries)) {
                dbHolding.ManagerEntries.Clear();
                dbHolding.ManagerEntries.AddRange(entries);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return holdings.Count;
    }

}
