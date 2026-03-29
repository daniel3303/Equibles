using System.Globalization;
using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Cftc.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Cftc.HostedService.Services;

[Service]
public class CftcImportService {
    private const int InsertBatchSize = 1000;
    private const int EarliestCftcYear = 1986;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CftcImportService> _logger;
    private readonly ICftcClient _cftcClient;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;

    public CftcImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<CftcImportService> logger,
        ICftcClient cftcClient,
        IOptions<WorkerOptions> workerOptions,
        ErrorReporter errorReporter
    ) {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cftcClient = cftcClient;
        _workerOptions = workerOptions.Value;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken) {
        // Build a lookup of curated contract codes
        var curatedLookup = CuratedContractRegistry.Contracts
            .ToDictionary(c => c.MarketCode.Trim(), StringComparer.OrdinalIgnoreCase);

        // Ensure all curated contracts exist in DB
        await EnsureContractsExist(curatedLookup, cancellationToken);

        // Determine start year from global latest date or MinSyncDate
        var startYear = await DetermineStartYear(cancellationToken);
        var endYear = DateTime.UtcNow.Year;

        _logger.LogInformation("CFTC import: syncing years {StartYear} to {EndYear}", startYear, endYear);

        for (var year = startYear; year <= endYear; year++) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                await ImportYear(year, curatedLookup, cancellationToken);
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to download CFTC COT report for year {Year}, skipping", year);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error importing CFTC COT report for year {Year}", year);
                await _errorReporter.Report(ErrorSource.CftcScraper, "CftcImport.ImportYear", ex.Message, ex.StackTrace, $"year: {year}");
            }
        }
    }

    private async Task EnsureContractsExist(Dictionary<string, CuratedContract> curatedLookup, CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var contractRepo = scope.ServiceProvider.GetRequiredService<CftcContractRepository>();
        var existingCodes = await contractRepo.GetAll()
            .Select(c => c.MarketCode)
            .ToListAsync(cancellationToken);

        var existingSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var curated in curatedLookup.Values) {
            if (existingSet.Contains(curated.MarketCode)) continue;

            contractRepo.Add(new CftcContract {
                MarketCode = curated.MarketCode.Trim(),
                MarketName = curated.DisplayName,
                Category = curated.Category
            });

            _logger.LogInformation("Created CFTC contract {MarketCode} ({DisplayName})", curated.MarketCode, curated.DisplayName);
        }

        await contractRepo.SaveChanges();
    }

    private async Task<int> DetermineStartYear(CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var reportRepo = scope.ServiceProvider.GetRequiredService<CftcPositionReportRepository>();
        var latestDate = await reportRepo.GetGlobalLatestDate().FirstOrDefaultAsync(cancellationToken);

        if (latestDate != default) return latestDate.Year;

        var minDate = _workerOptions.MinSyncDate != null
            ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
            : new DateOnly(2020, 1, 1);

        return Math.Max(minDate.Year, EarliestCftcYear);
    }

    private async Task ImportYear(int year, Dictionary<string, CuratedContract> curatedLookup, CancellationToken cancellationToken) {
        var records = await _cftcClient.DownloadYearlyReport(year);
        _logger.LogDebug("CFTC year {Year}: downloaded {Count} raw records", year, records.Count);

        // Filter to curated contracts only
        var filtered = records.Where(r =>
            r.ContractMarketCode != null &&
            curatedLookup.ContainsKey(r.ContractMarketCode.Trim())).ToList();

        _logger.LogDebug("CFTC year {Year}: {Count} records match curated contracts", year, filtered.Count);

        if (filtered.Count == 0) return;

        // Load contract ID map
        Dictionary<string, Guid> contractIdMap;
        using (var scope = _scopeFactory.CreateScope()) {
            var contractRepo = scope.ServiceProvider.GetRequiredService<CftcContractRepository>();
            contractIdMap = await contractRepo.GetAll()
                .ToDictionaryAsync(c => c.MarketCode, c => c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        }

        // Load existing dates per contract for deduplication
        var allDates = filtered
            .Select(r => ParseDate(r.ReportDate))
            .Where(d => d.HasValue)
            .Select(d => d.Value)
            .ToList();

        if (allDates.Count == 0) return;

        var minDate = allDates.Min();
        var maxDate = allDates.Max();

        HashSet<(Guid, DateOnly)> existingKeys;
        using (var scope = _scopeFactory.CreateScope()) {
            var reportRepo = scope.ServiceProvider.GetRequiredService<CftcPositionReportRepository>();
            existingKeys = (await reportRepo.GetAll()
                .Where(r => r.ReportDate >= minDate && r.ReportDate <= maxDate)
                .Select(r => new { r.CftcContractId, r.ReportDate })
                .ToListAsync(cancellationToken))
                .Select(r => (r.CftcContractId, r.ReportDate))
                .ToHashSet();
        }

        var batch = new List<CftcPositionReport>(InsertBatchSize);
        var totalInserted = 0;

        foreach (var record in filtered) {
            var date = ParseDate(record.ReportDate);
            if (date == null) continue;

            var code = record.ContractMarketCode.Trim();
            if (!contractIdMap.TryGetValue(code, out var contractId)) continue;

            if (existingKeys.Contains((contractId, date.Value))) continue;

            batch.Add(new CftcPositionReport {
                CftcContractId = contractId,
                ReportDate = date.Value,
                OpenInterest = record.OpenInterest ?? 0,
                NonCommLong = record.NonCommLong ?? 0,
                NonCommShort = record.NonCommShort ?? 0,
                NonCommSpreads = record.NonCommSpreads ?? 0,
                CommLong = record.CommLong ?? 0,
                CommShort = record.CommShort ?? 0,
                TotalRptLong = record.TotalRptLong ?? 0,
                TotalRptShort = record.TotalRptShort ?? 0,
                NonRptLong = record.NonRptLong ?? 0,
                NonRptShort = record.NonRptShort ?? 0,
                ChangeOpenInterest = record.ChangeOpenInterest,
                ChangeNonCommLong = record.ChangeNonCommLong,
                ChangeNonCommShort = record.ChangeNonCommShort,
                ChangeCommLong = record.ChangeCommLong,
                ChangeCommShort = record.ChangeCommShort,
                PctNonCommLong = record.PctNonCommLong,
                PctNonCommShort = record.PctNonCommShort,
                PctCommLong = record.PctCommLong,
                PctCommShort = record.PctCommShort,
                TradersTotal = record.TradersTotal,
                TradersNonCommLong = record.TradersNonCommLong,
                TradersNonCommShort = record.TradersNonCommShort,
                TradersCommLong = record.TradersCommLong,
                TradersCommShort = record.TradersCommShort
            });

            if (batch.Count >= InsertBatchSize) {
                await FlushBatch(batch);
                totalInserted += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0) {
            await FlushBatch(batch);
            totalInserted += batch.Count;
            batch.Clear();
        }

        // Update contract metadata
        if (totalInserted > 0) {
            await UpdateContractMetadata(contractIdMap, cancellationToken);
        }

        _logger.LogInformation("CFTC year {Year}: imported {Count} position reports", year, totalInserted);
    }

    private async Task UpdateContractMetadata(Dictionary<string, Guid> contractIdMap, CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var contractRepo = scope.ServiceProvider.GetRequiredService<CftcContractRepository>();
        var reportRepo = scope.ServiceProvider.GetRequiredService<CftcPositionReportRepository>();

        foreach (var contractId in contractIdMap.Values) {
            var latestDate = await reportRepo.GetAll()
                .Where(r => r.CftcContractId == contractId)
                .Select(r => r.ReportDate)
                .OrderByDescending(d => d)
                .FirstOrDefaultAsync(cancellationToken);

            if (latestDate == default) continue;

            var contract = await contractRepo.Get(contractId);
            contract.LatestReportDate = latestDate;
            contract.LastUpdated = DateTime.UtcNow;
        }

        await contractRepo.SaveChanges();
    }

    private async Task FlushBatch(List<CftcPositionReport> items) {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<CftcPositionReportRepository>();
        repo.AddRange(items);
        await repo.SaveChanges();
    }

    private static DateOnly? ParseDate(string value) {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // Try YYYY-MM-DD first (standard format)
        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        // Try YYMMDD (legacy format)
        if (DateOnly.TryParseExact(value.Trim(), "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date;

        return null;
    }
}
