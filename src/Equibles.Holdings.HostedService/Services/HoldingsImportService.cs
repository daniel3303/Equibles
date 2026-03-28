using System.IO.Compression;
using Equibles.Errors.BusinessLogic;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services.ValueNormalizers;
using Equibles.Core.AutoWiring;
using Equibles.Holdings.HostedService.Configuration;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsImportService {
    private const int InsertBatchSize = 1000;
    private const int MaxConsecutiveEmptyBatches = 5;
    private const int MinHoldersForConsensus = 5;
    private static readonly DateOnly CutoffDate = new(2023, 1, 1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldingsImportService> _logger;
    private readonly HoldingsScraperOptions _options;
    private readonly ErrorReporter _errorReporter;

    public HoldingsImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<HoldingsImportService> logger,
        IOptions<HoldingsScraperOptions> options,
        ErrorReporter errorReporter
    ) {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _errorReporter = errorReporter;
    }

    /// <param name="valueInThousands">
    /// True for pre-2023 SEC data sets where the VALUE column is in thousands of dollars.
    /// False for 2023+ data sets where VALUE is in actual dollars.
    /// </param>
    public async Task ImportDataSet(ZipArchive archive, DateOnly minReportDate, bool valueInThousands, CancellationToken cancellationToken) {
        var context = new ImportContext {
            TsvParser = new TsvParser(),
            Archive = archive,
            MinReportDate = minReportDate,
            DataSetValueInThousands = valueInThousands,
        };

        if (!await ParseSubmissions(context, cancellationToken)) return;
        DeduplicateSubmissions(context);
        if (await IsAlreadyImported(context, cancellationToken)) return;
        if (!await ParseCoverPages(context, cancellationToken)) return;
        if (!await BuildCusipMapping(context, cancellationToken)) return;
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

    private static void DeduplicateSubmissions(ImportContext context) {
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

        var query = _options.TickersToSync?.Count > 0
            ? stockRepo.GetByTickers(_options.TickersToSync)
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

            var rawValue = ParseLong(GetValue(row, "VALUE"));
            var shares = ParseLong(GetValue(row, "SSHPRNAMT"));

            var normalizer = await SelectNormalizer(
                context, submission, isAmendment, commonStockId, rawValue, shares, cancellationToken);

            var normalizedValue = normalizer.Normalize(rawValue);
            var otherManagerNumber = ParseNullableInt(GetValue(row, "OTHERMANAGER"));
            var discretion = ParseInvestmentDiscretion(GetValue(row, "INVESTMENTDISCRETION"));

            var managerEntry = new HoldingManagerEntry {
                ManagerNumber = otherManagerNumber,
                ManagerName = ResolveManagerName(context, accession, otherManagerNumber),
                Shares = shares,
                Value = normalizedValue,
                InvestmentDiscretion = discretion,
            };

            if (holdingsMap.TryGetValue(uniqueKey, out var existing)) {
                totalDuplicates++;
                // Aggregate: sum shares, value, and voting authority across sub-manager rows
                existing.Shares += shares;
                existing.Value += normalizedValue;
                existing.VotingAuthSole += ParseLong(GetValue(row, "VOTING_AUTH_SOLE"));
                existing.VotingAuthShared += ParseLong(GetValue(row, "VOTING_AUTH_SHARED"));
                existing.VotingAuthNone += ParseLong(GetValue(row, "VOTING_AUTH_NONE"));
                existing.ManagerEntries.Add(managerEntry);
            } else {
                var holding = new InstitutionalHolding {
                    InstitutionalHolderId = holderId,
                    CommonStockId = commonStockId,
                    FilingDate = filingDate,
                    ReportDate = reportDate,
                    Value = normalizedValue,
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

        // Flush remaining
        if (holdingsMap.Count > 0) {
            var inserted = await FlushBatch(holdingsMap.Values.ToList(), cancellationToken);
            totalInserted += inserted;
            holdingsMap.Clear();
        }

        _logger.LogInformation(
            "Import complete. Inserted: {Inserted}, Skipped (untracked): {Skipped}, Duplicates removed: {Duplicates}",
            totalInserted, totalSkipped, totalDuplicates);
    }

    private async Task<IValueNormalizer> SelectNormalizer(
        ImportContext context,
        SubmissionRow submission,
        bool isAmendment,
        Guid commonStockId,
        long rawValue,
        long shares,
        CancellationToken cancellationToken
    ) {
        // Pre-2023 data set → always thousands
        if (context.DataSetValueInThousands)
            return ThousandsValueNormalizer.Instance;

        // 2023+ non-amendment → dollars
        if (!isAmendment)
            return PassthroughValueNormalizer.Instance;

        // Unparseable report date → can't determine era, default to dollars
        if (!TryParseDateOnly(submission.PeriodOfReport, out var reportDate))
            return PassthroughValueNormalizer.Instance;

        // 2023+ amendment for post-cutoff report date → dollars
        if (reportDate >= CutoffDate)
            return PassthroughValueNormalizer.Instance;

        // 2023+ amendment for pre-cutoff → consensus detection (rare cross-boundary case;
        // each unique (stock, date) pair hits the DB once, then is cached in ConsensusCache)
        if (shares <= 0)
            return PassthroughValueNormalizer.Instance;

        var impliedPrice = (decimal)rawValue / shares;
        var medianPrice = await GetConsensusPrice(context, commonStockId, reportDate, cancellationToken);

        if (medianPrice == null)
            return PassthroughValueNormalizer.Instance;

        return impliedPrice < medianPrice.Value * 0.005m
            ? ThousandsValueNormalizer.Instance
            : PassthroughValueNormalizer.Instance;
    }

    private async Task<decimal?> GetConsensusPrice(
        ImportContext context,
        Guid commonStockId,
        DateOnly reportDate,
        CancellationToken cancellationToken
    ) {
        var cacheKey = (commonStockId, reportDate);
        if (context.ConsensusCache.TryGetValue(cacheKey, out var cached))
            return cached;

        using var scope = _scopeFactory.CreateScope();
        var holdingRepo = scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();

        var pricePerShare = await holdingRepo.GetAll()
            .Where(h => h.CommonStockId == commonStockId
                && h.ReportDate == reportDate
                && h.Shares > 0
                && h.Value > 0)
            .Select(h => (decimal)h.Value / h.Shares)
            .ToListAsync(cancellationToken);

        decimal? result = null;
        if (pricePerShare.Count >= MinHoldersForConsensus) {
            pricePerShare.Sort();
            var mid = pricePerShare.Count / 2;
            result = pricePerShare.Count % 2 == 0
                ? (pricePerShare[mid - 1] + pricePerShare[mid]) / 2m
                : pricePerShare[mid];
        }

        context.ConsensusCache[cacheKey] = result;
        return result;
    }

    private async Task<int> FlushBatch(List<InstitutionalHolding> holdings, CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var holdingRepo = scope.ServiceProvider.GetRequiredService<InstitutionalHoldingRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesDbContext>();

        // Extract manager entries before upsert (FlexLabs doesn't handle owned entities)
        var entriesByKey = new Dictionary<string, List<HoldingManagerEntry>>();
        foreach (var h in holdings) {
            var key = $"{h.CommonStockId}|{h.InstitutionalHolderId}|{h.ReportDate}|{(int)h.ShareType}|{h.OptionType?.ToString() ?? ""}";
            entriesByKey[key] = h.ManagerEntries.ToList();
            h.ManagerEntries.Clear();
        }

        // Upsert main holding rows (without manager entries)
        await holdingRepo.GetDbSet()
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
            })
            .RunAsync(cancellationToken);

        // Load upserted holdings from DB to get IDs and replace manager entries
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

    /// <summary>
    /// Finds a zip entry by filename, handling both flat and nested archives.
    /// SEC changed their 13F zip structure in mid-2025 to nest files inside a subdirectory.
    /// </summary>
    private static ZipArchiveEntry FindEntry(ZipArchive archive, string fileName) {
        return archive.GetEntry(fileName)
            ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetValue(Dictionary<string, string> row, string key) {
        return row.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Parses date strings in both ISO (yyyy-MM-dd) and SEC (dd-MMM-yyyy) formats.
    /// </summary>
    private static bool TryParseDateOnly(string value, out DateOnly result) {
        result = default;
        if (string.IsNullOrEmpty(value)) return false;

        // Try ISO format first (yyyy-MM-dd)
        if (DateOnly.TryParse(value, out result)) return true;

        // Try SEC format (dd-MMM-yyyy, e.g., "31-DEC-2019")
        if (DateOnly.TryParseExact(value, "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result)) {
            return true;
        }

        return false;
    }

    private static long ParseLong(string value) {
        return long.TryParse(value, out var result) ? result : 0;
    }

    private static ShareType ParseShareType(string value) {
        return value?.ToUpperInvariant() switch {
            "SH" => ShareType.Shares,
            "PRN" => ShareType.Principal,
            _ => ShareType.Shares,
        };
    }

    private static Equibles.Holdings.Data.Models.OptionType? ParseOptionType(string value) {
        return value?.ToUpperInvariant() switch {
            "PUT" => Equibles.Holdings.Data.Models.OptionType.Put,
            "CALL" => Equibles.Holdings.Data.Models.OptionType.Call,
            _ => null,
        };
    }

    private static int? ParseNullableInt(string value) {
        return int.TryParse(value, out var result) ? result : null;
    }

    private static string ResolveManagerName(ImportContext context, string accession, int? managerNumber) {
        if (managerNumber == null) return null;
        if (context.OtherManagers.TryGetValue(accession, out var seqMap)
            && seqMap.TryGetValue(managerNumber.Value, out var name)) {
            return name;
        }
        return null;
    }

    private static InvestmentDiscretion ParseInvestmentDiscretion(string value) {
        return value?.ToUpperInvariant() switch {
            "SOLE" => InvestmentDiscretion.Sole,
            "DFND" => InvestmentDiscretion.Defined,
            "OTR" => InvestmentDiscretion.Other,
            _ => InvestmentDiscretion.Sole,
        };
    }

}
