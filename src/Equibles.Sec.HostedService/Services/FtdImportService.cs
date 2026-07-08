using System.Globalization;
using System.IO.Compression;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.Repositories;
using Equibles.Worker;
using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Sec.HostedService.Services;

[Service]
public class FtdImportService
{
    private const string BaseUrl = "https://www.sec.gov/files/data/fails-deliver-data";
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<FtdImportService> _logger;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;

    public FtdImportService(
        IServiceScopeFactory scopeFactory,
        ISecEdgarClient secEdgarClient,
        ILogger<FtdImportService> logger,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions
    )
    {
        _scopeFactory = scopeFactory;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
        _errorReporter = errorReporter;
        _workerOptions = workerOptions.Value;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var startDate = await SyncStartDate.Resolve<FailToDeliverRepository>(
            _scopeFactory,
            _workerOptions,
            repo => repo.GetLatestDate(),
            cancellationToken
        );

        var fileNames = GetFileNames(startDate);

        if (fileNames.Count == 0)
        {
            _logger.LogInformation("FTD data is up to date");
            return;
        }

        _logger.LogInformation(
            "Downloading {Count} FTD files from {Start}",
            fileNames.Count,
            startDate
        );

        var tickerMap = await BuildTickerMap(cancellationToken);
        var cusipsSeeded = 0;

        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var records = await DownloadAndParse(fileName, cancellationToken);
                if (records.Count == 0)
                    continue;

                cusipsSeeded += await SeedCusips(records, tickerMap, cancellationToken);

                var imported = await ImportRecords(records, tickerMap, cancellationToken);

                _logger.LogInformation("FTD {File}: imported {Count} records", fileName, imported);
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (IsRecentFtdFile(fileName))
                {
                    _logger.LogInformation(
                        "FTD file {File} not yet available (404), skipping",
                        fileName
                    );
                }
                else
                {
                    // Pre-2021 FTD ZIPs (cnsfails20*) routinely return 404 — SEC
                    // moved their archive. The plain warning carries the only
                    // useful signal ("URL may have changed"); the exception's
                    // stack trace adds nothing but noise, so drop it.
                    _logger.LogWarning(
                        "FTD file {File} returned 404 but is older than 2 months — possible URL change",
                        fileName
                    );
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Failed to download FTD file {File}, skipping", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing FTD file {File}", fileName);
                await _errorReporter.Report(
                    ErrorSource.FtdScraper,
                    "FtdImport.ProcessFile",
                    ex.Message,
                    ex.StackTrace,
                    $"file: {fileName}"
                );
            }
        }

        if (cusipsSeeded > 0)
        {
            _logger.LogInformation("Seeded or updated {Count} CUSIPs from FTD data", cusipsSeeded);
        }
    }

    /// <summary>
    /// Seeds and updates CUSIP values on CommonStock records by matching FTD
    /// ticker→CUSIP pairs. Beyond filling stocks that have no CUSIP yet, this is
    /// the pipeline's only detector for issuer-level CUSIP changes (share-class
    /// conversions, reincorporations): the CNS feed keys rows by trading symbol,
    /// so when a symbol's CUSIP moves, the stored stock must follow — otherwise
    /// every new 13F line for the stock references a CUSIP nothing maps and the
    /// stock's holders silently collapse to the laggards still filing the old one.
    /// </summary>
    private async Task<int> SeedCusips(
        List<FtdRecord> records,
        Dictionary<string, Guid> tickerMap,
        CancellationToken cancellationToken
    )
    {
        var strippedAliases = BuildStrippedTickerAliases(tickerMap);
        // Latest settlement date wins: during a CUSIP transition a single FTD
        // file can carry both the retiring and the replacement CUSIP for one
        // symbol, and the most recent trading day reflects the current security.
        var tickerToCusip = new Dictionary<string, (string Cusip, DateOnly SettlementDate)>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.Cusip) || string.IsNullOrEmpty(record.Symbol))
                continue;
            if (!TryResolveSymbol(record.Symbol, tickerMap, strippedAliases, out var ticker))
                continue;
            if (
                !tickerToCusip.TryGetValue(ticker, out var current)
                || record.SettlementDate > current.SettlementDate
            )
            {
                tickerToCusip[ticker] = (record.Cusip, record.SettlementDate);
            }
        }

        if (tickerToCusip.Count == 0)
            return 0;

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var stockManager = scope.ServiceProvider.GetRequiredService<CommonStockManager>();

        var tickers = tickerToCusip.Keys.ToList();
        var stocks = await stockRepo.GetByTickers(tickers).ToListAsync(cancellationToken);

        // Guard against ticker recycling: if a delisted issuer's symbol is
        // reassigned to a different company before CompanySync retires the
        // stale stock, the FTD feed maps the freed symbol to the NEW issuer's
        // CUSIP. Adopting a CUSIP that is currently another tracked stock's
        // identity would leave two stocks sharing one CUSIP (the CommonStock
        // Cusip index is non-unique) and misroute that CUSIP's 13F lines, so
        // such rows are skipped — CompanySync owns ticker reassignment.
        var resolvedCusips = tickerToCusip.Values.Select(v => v.Cusip).Distinct().ToList();
        var cusipOwners = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var owners = await stockRepo
            .GetAll()
            .Where(s => s.Cusip != null && resolvedCusips.Contains(s.Cusip))
            .Select(s => new { s.Id, s.Cusip })
            .ToListAsync(cancellationToken);
        foreach (var owner in owners)
        {
            cusipOwners[owner.Cusip] = owner.Id;
        }

        var seeded = 0;
        foreach (var stock in stocks)
        {
            if (!tickerToCusip.TryGetValue(stock.Ticker, out var resolved))
                continue;
            if (string.Equals(stock.Cusip, resolved.Cusip, StringComparison.OrdinalIgnoreCase))
                continue;
            if (cusipOwners.TryGetValue(resolved.Cusip, out var ownerId) && ownerId != stock.Id)
            {
                _logger.LogWarning(
                    "FTD maps {Ticker} to CUSIP {Cusip}, but that CUSIP already identifies another tracked stock — skipping (possible ticker reuse)",
                    stock.Ticker,
                    resolved.Cusip
                );
                continue;
            }

            // Route through the manager so a StockCusipChanged event is
            // published (outbox) — lets Holdings backfill any 13F data
            // sets processed before this stock had a CUSIP (or while it
            // still carried the retired one, kept as an alias).
            await stockManager.SetCusip(stock, resolved.Cusip);
            seeded++;
        }

        return seeded;
    }

    /// <summary>
    /// The CNS fails feed strips the share-class separator from symbols ("BRKB",
    /// "MOGA") while EDGAR tickers keep it ("BRK-B", "MOG-A"), so exact matching
    /// permanently skips class-share issuers. Alias each stored ticker by its
    /// separator-stripped form — but never shadow a real ticker, and drop a
    /// stripped form two tickers collapse onto rather than guess.
    /// </summary>
    private static Dictionary<string, string> BuildStrippedTickerAliases(
        Dictionary<string, Guid> tickerMap
    )
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in tickerMap.Keys)
        {
            var stripped = string.Concat(ticker.Where(char.IsLetterOrDigit));
            if (
                stripped.Length == 0
                || string.Equals(stripped, ticker, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            if (tickerMap.ContainsKey(stripped))
                continue;
            if (!aliases.TryAdd(stripped, ticker))
                ambiguous.Add(stripped);
        }

        foreach (var key in ambiguous)
            aliases.Remove(key);

        return aliases;
    }

    private static bool TryResolveSymbol(
        string symbol,
        Dictionary<string, Guid> tickerMap,
        Dictionary<string, string> strippedAliases,
        out string ticker
    )
    {
        if (tickerMap.ContainsKey(symbol))
        {
            ticker = symbol;
            return true;
        }

        return strippedAliases.TryGetValue(symbol, out ticker);
    }

    private async Task<int> ImportRecords(
        List<FtdRecord> records,
        Dictionary<string, Guid> tickerMap,
        CancellationToken cancellationToken
    )
    {
        // Group by stock+date, keeping the latest record per day (FTD is cumulative)
        var grouped = new Dictionary<(Guid StockId, DateOnly Date), FailToDeliver>();

        var strippedAliases = BuildStrippedTickerAliases(tickerMap);
        foreach (var record in records)
        {
            if (
                string.IsNullOrEmpty(record.Symbol)
                || !TryResolveSymbol(record.Symbol, tickerMap, strippedAliases, out var ticker)
                || !tickerMap.TryGetValue(ticker, out var stockId)
            )
            {
                continue;
            }

            var key = (stockId, record.SettlementDate);
            grouped[key] = new FailToDeliver
            {
                CommonStockId = stockId,
                SettlementDate = record.SettlementDate,
                Quantity = record.Quantity,
                Price = record.Price,
            };
        }

        return await BatchPersister.Persist(grouped.Values, InsertBatchSize, FlushBatch);
    }

    private async Task FlushBatch(List<FailToDeliver> items)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        // Guards GH-1591: CompanySync can delete a CommonStock between BuildTickerMap and
        // this flush. Without filtering, one stale CommonStockId trips
        // FK_FailToDeliver_CommonStock_CommonStockId and rolls back the entire UpsertRange —
        // dropping rows for surviving stocks alongside the orphan.
        var safeItems = await stockRepo.FilterByExistingStocks(items, i => i.CommonStockId);
        var skipped = items.Count - safeItems.Count;
        if (skipped > 0)
        {
            _logger.LogWarning(
                "FTD batch: skipping {Count} rows whose parent CommonStock was removed before flush",
                skipped
            );
        }
        if (safeItems.Count == 0)
        {
            return;
        }

        await dbContext
            .Set<FailToDeliver>()
            .UpsertRange(safeItems)
            .On(f => new { f.CommonStockId, f.SettlementDate })
            .WhenMatched(
                (existing, incoming) =>
                    new FailToDeliver { Quantity = incoming.Quantity, Price = incoming.Price }
            )
            .RunAsync();
    }

    private async Task<Dictionary<string, Guid>> BuildTickerMap(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var tickerMapService = scope.ServiceProvider.GetRequiredService<TickerMapService>();
        return await tickerMapService.Build(_workerOptions.TickersToSync, cancellationToken);
    }

    private async Task<List<FtdRecord>> DownloadAndParse(
        string fileName,
        CancellationToken cancellationToken
    )
    {
        var url = $"{BaseUrl}/{fileName}";
        await using var zipStream = await _secEdgarClient.DownloadStream(url);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var entry = archive.Entries.FirstOrDefault();
        if (entry == null)
        {
            _logger.LogError(
                "Empty zip archive for FTD file {File} — SEC format may have changed",
                fileName
            );
            await _errorReporter.Report(
                ErrorSource.FtdScraper,
                "FtdImport.EmptyArchive",
                $"Zip archive for {fileName} contains no entries — SEC format may have changed",
                null,
                $"file: {fileName}"
            );
            return [];
        }

        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);

        var records = new List<FtdRecord>();

        // Skip header line
        await reader.ReadLineAsync(cancellationToken);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var record = ParseLine(line);
            if (record != null)
                records.Add(record);
        }

        return records;
    }

    // Fields: SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE
    private static FtdRecord ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var parts = line.Split('|');
        if (parts.Length < 6)
            return null;

        if (
            !DateOnly.TryParseExact(
                parts[0],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
        )
        {
            return null;
        }

        if (!long.TryParse(parts[3], out var quantity))
            return null;

        if (!decimal.TryParse(parts[5], CultureInfo.InvariantCulture, out var price))
            return null;

        return new FtdRecord
        {
            SettlementDate = date,
            Cusip = parts[1].Trim(),
            Symbol = parts[2].Trim(),
            Quantity = quantity,
            Price = price,
        };
    }

    // Oldest FTD file available on SEC EDGAR is cnsfails201706b.zip (second half of June 2017).
    // Some individual files within the range may 404 (handled gracefully above).
    private static readonly DateOnly OldestAvailableDate = new(2017, 6, 1);

    /// <summary>
    /// Generates FTD file names from a start date to now.
    /// Format: cnsfails{YYYYMM}{a|b}.zip (a = first half, b = second half)
    /// </summary>
    internal static List<string> GetFileNames(DateOnly startDate)
    {
        var fileNames = new List<string>();
        var now = DateOnly.FromDateTime(DateTime.UtcNow);

        if (startDate < OldestAvailableDate)
            startDate = OldestAvailableDate;

        var current = new DateOnly(startDate.Year, startDate.Month, 1);

        while (current <= now)
        {
            var yearMonth = current.ToString("yyyyMM", CultureInfo.InvariantCulture);

            // The 'a' file for June 2017 doesn't exist — only 'b' is available
            if (current != OldestAvailableDate)
                fileNames.Add($"cnsfails{yearMonth}a.zip");

            fileNames.Add($"cnsfails{yearMonth}b.zip");

            current = current.AddMonths(1);
        }

        return fileNames;
    }

    /// <summary>
    /// Returns true if the FTD file is for a month within the last 2 months (404 is expected — SEC has 45 days to publish).
    /// </summary>
    internal static bool IsRecentFtdFile(string fileName)
    {
        // Format: cnsfails{YYYYMM}{a|b}.zip — "cnsfails" is 8 chars
        if (
            fileName.Length >= 17
            && int.TryParse(fileName.AsSpan(8, 4), out var year)
            && year is >= 1 and <= 9999
            && int.TryParse(fileName.AsSpan(12, 2), out var month)
            && month is >= 1 and <= 12
        )
        {
            var fileMonth = new DateOnly(year, month, 1);
            var twoMonthsAgo = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-2);
            return fileMonth >= twoMonthsAgo;
        }
        return false;
    }
}
