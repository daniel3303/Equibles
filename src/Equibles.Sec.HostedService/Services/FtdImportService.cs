using System.Globalization;
using System.IO.Compression;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Sec.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Sec.HostedService.Models;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

[Service]
public class FtdImportService {
    private const string BaseUrl = "https://www.sec.gov/files/data/fails-deliver-data";
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<FtdImportService> _logger;
    private readonly ErrorReporter _errorReporter;
    private readonly FtdScraperOptions _options;
    private readonly WorkerOptions _workerOptions;

    public FtdImportService(
        IServiceScopeFactory scopeFactory,
        ISecEdgarClient secEdgarClient,
        ILogger<FtdImportService> logger,
        ErrorReporter errorReporter,
        IOptions<FtdScraperOptions> options,
        IOptions<WorkerOptions> workerOptions
    ) {
        _scopeFactory = scopeFactory;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
        _errorReporter = errorReporter;
        _options = options.Value;
        _workerOptions = workerOptions.Value;
    }

    public async Task Import(CancellationToken cancellationToken) {
        // Determine start date
        DateOnly startDate;
        using (var scope = _scopeFactory.CreateScope()) {
            var repo = scope.ServiceProvider.GetRequiredService<FailToDeliverRepository>();
            var latestDate = await repo.GetLatestDate().FirstOrDefaultAsync(cancellationToken);

            if (latestDate != default) {
                startDate = latestDate.AddDays(1);
            } else {
                var minDate = _workerOptions.MinSyncDate ?? new DateTime(2020, 1, 1);
                startDate = DateOnly.FromDateTime(minDate);
            }
        }

        var fileNames = GetFileNames(startDate);

        if (fileNames.Count == 0) {
            _logger.LogInformation("FTD data is up to date");
            return;
        }

        _logger.LogInformation("Downloading {Count} FTD files from {Start}", fileNames.Count, startDate);

        // Build ticker map for matching
        var tickerMap = await BuildTickerMap(cancellationToken);
        var cusipsSeeded = 0;

        foreach (var fileName in fileNames) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                var records = await DownloadAndParse(fileName, cancellationToken);
                if (records.Count == 0) continue;

                // Seed CUSIPs from the FTD data (ticker → CUSIP mapping)
                cusipsSeeded += await SeedCusips(records, tickerMap, cancellationToken);

                // Import FTD data
                var imported = await ImportRecords(records, tickerMap, cancellationToken);

                _logger.LogInformation("FTD {File}: imported {Count} records", fileName, imported);
            } catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
                if (IsRecentFtdFile(fileName)) {
                    _logger.LogInformation("FTD file {File} not yet available (404), skipping", fileName);
                } else {
                    _logger.LogWarning(ex, "FTD file {File} returned 404 but is older than 2 months — possible URL change", fileName);
                }
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to download FTD file {File}, skipping", fileName);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing FTD file {File}", fileName);
                await _errorReporter.Report(ErrorSource.FtdScraper, "FtdImport.ProcessFile", ex.Message, ex.StackTrace, $"file: {fileName}");
            }
        }

        if (cusipsSeeded > 0) {
            _logger.LogInformation("Seeded {Count} new CUSIPs from FTD data", cusipsSeeded);
        }
    }

    /// <summary>
    /// Seeds CUSIP values on CommonStock records by matching FTD ticker→CUSIP pairs.
    /// </summary>
    private async Task<int> SeedCusips(
        List<FtdRecord> records,
        Dictionary<string, Guid> tickerMap,
        CancellationToken cancellationToken
    ) {
        // Collect unique ticker→CUSIP pairs where we have a matching stock
        var tickerToCusip = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records) {
            if (string.IsNullOrEmpty(record.Cusip) || string.IsNullOrEmpty(record.Symbol)) continue;
            if (!tickerMap.ContainsKey(record.Symbol)) continue;
            tickerToCusip.TryAdd(record.Symbol, record.Cusip);
        }

        if (tickerToCusip.Count == 0) return 0;

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        // Load stocks that don't have CUSIPs yet
        var tickers = tickerToCusip.Keys.ToList();
        var stocks = await stockRepo.GetByTickers(tickers)
            .Where(s => s.Cusip == null)
            .ToListAsync(cancellationToken);

        var seeded = 0;
        foreach (var stock in stocks) {
            if (tickerToCusip.TryGetValue(stock.Ticker, out var cusip)) {
                stock.Cusip = cusip;
                seeded++;
            }
        }

        if (seeded > 0) {
            await stockRepo.SaveChanges();
        }

        return seeded;
    }

    private async Task<int> ImportRecords(
        List<FtdRecord> records,
        Dictionary<string, Guid> tickerMap,
        CancellationToken cancellationToken
    ) {
        // Group by stock+date, keeping the latest record per day (FTD is cumulative)
        var grouped = new Dictionary<(Guid StockId, DateOnly Date), FailToDeliver>();

        foreach (var record in records) {
            if (string.IsNullOrEmpty(record.Symbol)
                || !tickerMap.TryGetValue(record.Symbol, out var stockId)) {
                continue;
            }

            var key = (stockId, record.SettlementDate);
            grouped[key] = new FailToDeliver {
                CommonStockId = stockId,
                SettlementDate = record.SettlementDate,
                Quantity = record.Quantity,
                Price = record.Price,
            };
        }

        var batch = new List<FailToDeliver>(InsertBatchSize);
        var totalInserted = 0;

        foreach (var item in grouped.Values) {
            batch.Add(item);

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

        return totalInserted;
    }

    private async Task FlushBatch(List<FailToDeliver> items) {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<FailToDeliverRepository>();

        await repo.GetDbSet()
            .UpsertRange(items)
            .On(f => new { f.CommonStockId, f.SettlementDate })
            .WhenMatched(f => new FailToDeliver {
                Quantity = f.Quantity,
                Price = f.Price,
            })
            .RunAsync();
    }

    private async Task<Dictionary<string, Guid>> BuildTickerMap(CancellationToken cancellationToken) {
        using var scope = _scopeFactory.CreateScope();
        var tickerMapService = scope.ServiceProvider.GetRequiredService<TickerMapService>();
        return await tickerMapService.Build(_options.TickersToSync, cancellationToken);
    }

    private async Task<List<FtdRecord>> DownloadAndParse(string fileName, CancellationToken cancellationToken) {
        var url = $"{BaseUrl}/{fileName}";
        await using var zipStream = await _secEdgarClient.DownloadStream(url);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var entry = archive.Entries.FirstOrDefault();
        if (entry == null) {
            _logger.LogError("Empty zip archive for FTD file {File} — SEC format may have changed", fileName);
            await _errorReporter.Report(ErrorSource.FtdScraper, "FtdImport.EmptyArchive",
                $"Zip archive for {fileName} contains no entries — SEC format may have changed",
                null, $"file: {fileName}");
            return [];
        }

        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);

        var records = new List<FtdRecord>();

        // Skip header line
        await reader.ReadLineAsync(cancellationToken);

        while (await reader.ReadLineAsync(cancellationToken) is { } line) {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split('|');
            if (parts.Length < 6) continue;

            // Fields: SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE
            if (!DateOnly.TryParseExact(parts[0], "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date)) {
                continue;
            }

            long.TryParse(parts[3], out var quantity);
            decimal.TryParse(parts[5], CultureInfo.InvariantCulture, out var price);

            records.Add(new FtdRecord {
                SettlementDate = date,
                Cusip = parts[1].Trim(),
                Symbol = parts[2].Trim(),
                Quantity = quantity,
                Price = price,
            });
        }

        return records;
    }

    /// <summary>
    /// Generates FTD file names from a start date to now.
    /// Format: cnsfails{YYYYMM}{a|b}.zip (a = first half, b = second half)
    /// </summary>
    private static List<string> GetFileNames(DateOnly startDate) {
        var fileNames = new List<string>();
        var now = DateOnly.FromDateTime(DateTime.UtcNow);

        // Start from the beginning of the start month
        var current = new DateOnly(startDate.Year, startDate.Month, 1);

        while (current <= now) {
            var yearMonth = current.ToString("yyyyMM");
            fileNames.Add($"cnsfails{yearMonth}a.zip");
            fileNames.Add($"cnsfails{yearMonth}b.zip");

            current = current.AddMonths(1);
        }

        return fileNames;
    }

    /// <summary>
    /// Returns true if the FTD file is for a month within the last 2 months (404 is expected — SEC has 45 days to publish).
    /// </summary>
    private static bool IsRecentFtdFile(string fileName) {
        // Format: cnsfails{YYYYMM}{a|b}.zip — "cnsfails" is 8 chars
        if (fileName.Length >= 17
            && int.TryParse(fileName.AsSpan(8, 4), out var year)
            && int.TryParse(fileName.AsSpan(12, 2), out var month)
            && month is >= 1 and <= 12) {
            var fileMonth = new DateOnly(year, month, 1);
            var twoMonthsAgo = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
            return fileMonth >= twoMonthsAgo;
        }
        return false;
    }

}
