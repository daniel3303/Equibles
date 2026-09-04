using System.Globalization;
using System.IO.Compression;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;
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
    private const int LiveRecheckMonths = 1;

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
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        var importStartDate = await SyncStartDate.Resolve<FailToDeliverRepository>(
            _scopeFactory,
            _workerOptions,
            repo => repo.GetLatestDate(),
            cancellationToken
        );

        var minimumStartDate = SyncDateResolver.Resolve(default, _workerOptions);
        var replayStartDate = ApplyLiveRecheckWindow(importStartDate, minimumStartDate);
        var replayMonth = asOf.AddMonths(-LiveRecheckMonths)
            .ToString("yyyyMM", CultureInfo.InvariantCulture);
        var replayFiles = GetFileNames(replayStartDate, asOf)
            .Where(fileName => FtdMonthOf(fileName) == replayMonth)
            .ToList();
        var importFiles = GetFileNames(importStartDate, asOf);

        if (replayFiles.Count == 0 && importFiles.Count == 0)
        {
            _logger.LogInformation("FTD data is up to date");
            return;
        }

        _logger.LogInformation(
            "Downloading {ReplayCount} FTD identity replay files and {ImportCount} new-data files from {ImportStart}",
            replayFiles.Count,
            importFiles.Count,
            importStartDate
        );

        var tickerMap = await BuildTickerMap(cancellationToken);
        var listedTickerMap = await BuildListedTickerMap(cancellationToken);
        var replayRecords = new Dictionary<string, List<FtdRecord>>(StringComparer.Ordinal);
        var cusipsSeeded = await ReplayLiveIdentity(
            replayFiles,
            replayRecords,
            tickerMap,
            cancellationToken
        );
        if (cusipsSeeded > 0)
        {
            _logger.LogInformation("Seeded or updated {Count} CUSIPs from FTD data", cusipsSeeded);
        }

        await ImportNewRecords(
            importFiles,
            importStartDate,
            replayRecords,
            listedTickerMap,
            cancellationToken
        );
    }

    private async Task<int> ReplayLiveIdentity(
        List<string> replayFiles,
        Dictionary<string, List<FtdRecord>> replayRecords,
        Dictionary<string, Guid> tickerMap,
        CancellationToken cancellationToken
    )
    {
        if (replayFiles.Count == 0)
            return 0;

        foreach (var fileName in replayFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = await TryDownload(fileName, cancellationToken);
            if (records == null)
                continue;

            replayRecords[fileName] = records;
        }

        if (!replayFiles.All(replayRecords.ContainsKey))
            return 0;

        try
        {
            var secondaryMap = await BuildSecondaryTickerMap(tickerMap, cancellationToken);
            var liveIdentityEvidence = new Dictionary<string, LiveIdentityEvidence>(
                StringComparer.OrdinalIgnoreCase
            );
            foreach (var fileName in replayFiles)
            {
                var records = replayRecords[fileName];
                AccumulateLiveIdentityEvidence(
                    fileName,
                    records,
                    tickerMap,
                    secondaryMap,
                    liveIdentityEvidence
                );
                _logger.LogInformation(
                    "FTD {File}: scanned {Count} records for identity",
                    fileName,
                    records.Count
                );
            }

            if (liveIdentityEvidence.Count == 0)
                return 0;

            var identityRecords = liveIdentityEvidence
                .Values.SelectMany(evidence => evidence.Records)
                .ToList();
            return await SeedCusipsWithSecondaryMap(
                identityRecords,
                tickerMap,
                secondaryMap,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reconciling CUSIPs from live FTD evidence");
            await _errorReporter.Report(
                ErrorSource.FtdScraper,
                "FtdImport.SeedCusips",
                ex,
                $"files: {replayFiles.Count}"
            );
            return 0;
        }
    }

    private async Task ImportNewRecords(
        List<string> importFiles,
        DateOnly importStartDate,
        Dictionary<string, List<FtdRecord>> replayRecords,
        Dictionary<string, ListedSecurityKey> tickerMap,
        CancellationToken cancellationToken
    )
    {
        foreach (var fileName in importFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = replayRecords.TryGetValue(fileName, out var replayed)
                ? replayed
                : await TryDownload(fileName, cancellationToken);
            if (records == null)
                continue;

            var newRecords = records
                .Where(record => record.SettlementDate >= importStartDate)
                .ToList();
            if (newRecords.Count == 0)
            {
                _logger.LogInformation("FTD {File}: no new records to import", fileName);
                continue;
            }

            try
            {
                var imported = await ImportRecords(newRecords, tickerMap, cancellationToken);
                _logger.LogInformation("FTD {File}: imported {Count} records", fileName, imported);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing FTD file {File}", fileName);
                await _errorReporter.Report(
                    ErrorSource.FtdScraper,
                    "FtdImport.ProcessFile",
                    ex,
                    $"file: {fileName}"
                );
            }
        }
    }

    private async Task<List<FtdRecord>> TryDownload(
        string fileName,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await DownloadAndParse(fileName, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            LogMissingFile(fileName);
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
                ex,
                $"file: {fileName}"
            );
        }

        return null;
    }

    private void LogMissingFile(string fileName)
    {
        if (IsRecentFtdFile(fileName))
        {
            _logger.LogInformation("FTD file {File} not yet available (404), skipping", fileName);
            return;
        }

        // Pre-2021 FTD ZIPs (cnsfails20*) routinely return 404 — SEC moved their archive.
        _logger.LogWarning(
            "FTD file {File} returned 404 but is older than 2 months — possible URL change",
            fileName
        );
    }

    /// <summary>
    /// Walks the FTD archive backwards in time recording the CUSIPs each tracked symbol
    /// USED to trade under, as <see cref="CommonStockCusipAlias"/> rows.
    /// <para>
    /// <see cref="SeedCusips"/> only captures a retirement it witnesses live, so every
    /// CUSIP change that predates this pipeline left no alias — and the 13F lines filed
    /// under those values never map. AMC's holders for 2022-12-31 read 4 institutions /
    /// 845 shares because its pre-reverse-split 00165C104 was unmapped; a year later the
    /// same stock shows 274 / 102M.
    /// </para>
    /// <para>
    /// The CNS fails file is the authority: the SEC itself publishes SYMBOL and CUSIP on
    /// one row, so no name matching or guessing is involved. Two guards keep it honest —
    /// the symbol must be a tracked stock's PRIMARY ticker (sibling securities file under
    /// their own symbols, so AMC's preferred units at 00165C203 land on APE, not AMC),
    /// and the CUSIP must share the stock's current ISSUER prefix, which is what stops a
    /// recycled ticker from importing a dead issuer's identity. A merger that changes the
    /// issuer prefix (Merck's 589331107 → 58933Y105) is deliberately NOT recovered:
    /// coverage loss over a wrong link.
    /// </para>
    /// <para>
    /// Bounded per cycle (<see cref="AliasSweepFilesPerCycle"/>) with the frontier in
    /// <see cref="BackfillState"/>, so it never blocks the daily import; one
    /// <see cref="StockCusipChanged"/> per cycle that recorded anything invalidates the
    /// processed-data-set ledger, and the Holdings worker re-imports the history that can
    /// now resolve.
    /// </para>
    /// </summary>
    public async Task BackfillRetiredCusips(CancellationToken cancellationToken)
    {
        var fileNames = await NextSweepFiles(AliasSweepCursorName);
        if (fileNames.Count == 0)
        {
            return;
        }

        var tickerMap = await BuildTickerMap(cancellationToken);
        if (tickerMap.Count == 0)
        {
            return;
        }

        var strippedAliases = BuildStrippedTickerAliases(tickerMap);
        var cusipsByTicker = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var records = await DownloadAndParse(fileName, cancellationToken);
                foreach (var record in records)
                {
                    if (string.IsNullOrEmpty(record.Cusip) || string.IsNullOrEmpty(record.Symbol))
                        continue;
                    if (
                        !TryResolveSymbol(record.Symbol, tickerMap, strippedAliases, out var ticker)
                    )
                        continue;

                    if (!cusipsByTicker.TryGetValue(ticker, out var cusips))
                    {
                        cusips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        cusipsByTicker[ticker] = cusips;
                    }
                    cusips.Add(record.Cusip);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                // A missing archive file costs coverage for that fortnight only; the
                // frontier still advances so the sweep cannot wedge on one bad file.
                _logger.LogWarning(
                    ex,
                    "Retired-CUSIP sweep: failed to download {File}, skipping",
                    fileName
                );
            }
        }

        var recorded = await RecordRetiredCusips(cusipsByTicker, cancellationToken);
        await AdvanceSweepFrontier(AliasSweepCursorName, fileNames[^1]);

        if (recorded > 0)
        {
            _logger.LogInformation(
                "Retired-CUSIP sweep: recorded {Count} alias(es) across {Files} FTD file(s)",
                recorded,
                fileNames.Count
            );
        }
    }

    /// <summary>
    /// Recovers the primary CUSIP for retained inactive identities from the SEC CNS fails
    /// archive. Files are scanned newest-first and a row is admitted only when its settlement
    /// date is on or before that identity's inclusive delisting cutoff. For recycled symbols,
    /// the earliest cutoff covering the row wins; equal cutoffs are ambiguous and refused.
    /// </summary>
    public async Task BackfillInactiveCusips(CancellationToken cancellationToken)
    {
        var sweep = await NextInactiveCusipSweepFiles(cancellationToken);
        if (sweep.StartedAt == null)
        {
            return;
        }
        if (sweep.FileNames.Count == 0)
        {
            await FinalizeInactiveCusips(sweep.StartedAt.Value, cancellationToken);
            return;
        }

        var tickerMap = await BuildInactiveTickerMap(sweep.StartedAt.Value, cancellationToken);
        if (tickerMap.Count == 0)
        {
            await AdvanceSweepFrontier(InactiveCusipSweepCursorName, sweep.FileNames[^1]);
            return;
        }

        var strippedAliases = BuildStrippedTickerAliases(tickerMap.Keys);
        var candidates = new List<HistoricalCusipCandidate>();
        string lastCompletedFile = null;

        foreach (var fileName in sweep.FileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var records = await DownloadAndParse(fileName, cancellationToken);
                foreach (var record in records)
                {
                    if (
                        string.IsNullOrEmpty(record.Cusip)
                        || string.IsNullOrEmpty(record.Symbol)
                        || !TryResolveHistoricalIdentity(
                            record.Symbol,
                            record.SettlementDate,
                            tickerMap,
                            strippedAliases,
                            out var listingId
                        )
                    )
                    {
                        continue;
                    }

                    candidates.Add(
                        new HistoricalCusipCandidate(listingId, record.Cusip, record.SettlementDate)
                    );
                }
                lastCompletedFile = fileName;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode == System.Net.HttpStatusCode.NotFound
                    && !IsRecentFtdFile(fileName)
                )
            {
                // SEC permanently omits a few old fortnight files. A confirmed old 404 has no
                // rows to recover, so it is a completed gap rather than a transient response.
                _logger.LogWarning(
                    "Inactive-CUSIP sweep: archive file {File} is unavailable (404), advancing",
                    fileName
                );
                lastCompletedFile = fileName;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                // Stop at the gap and retry it next cycle. Evidence from completed newer files
                // stays staged, but nothing is seeded until the full archive pass completes.
                _logger.LogWarning(
                    ex,
                    "Inactive-CUSIP sweep: failed to read {File}; leaving it retryable",
                    fileName
                );
                break;
            }
        }

        await StageInactiveCusipEvidence(candidates, sweep.StartedAt.Value, cancellationToken);
        if (lastCompletedFile != null)
        {
            await AdvanceSweepFrontier(InactiveCusipSweepCursorName, lastCompletedFile);
        }

        if (IsOldestArchiveFile(lastCompletedFile))
        {
            await FinalizeInactiveCusips(sweep.StartedAt.Value, cancellationToken);
        }
    }

    /// <summary>
    /// Walks the FTD archive recording the CUSIPs of tracked stocks' SECONDARY listings —
    /// sibling share classes, units, fund series — as <see cref="CommonStockListedCusip"/> rows.
    /// <para>
    /// The retired-CUSIP sweep above deliberately admits only PRIMARY symbols, so a sibling
    /// class's CUSIP (Alphabet Class C, 02079K107 under symbol GOOG) was never captured and
    /// every 13F line filed under it dropped at import. This sweep resolves the CNS feed's
    /// symbol against the secondary-ticker space instead, pairing each hit's CUSIP with the
    /// exact listed ticker so the holdings lane can key the class's positions separately.
    /// </para>
    /// <para>
    /// Same authority and guards as the alias sweep: the SEC publishes SYMBOL and CUSIP on one
    /// row (no name matching); a symbol that is any stock's PRIMARY ticker is left to the alias
    /// sweep; a symbol two stocks' secondary lists collapse onto is dropped rather than guessed;
    /// and the CUSIP must share the stock's issuer prefix, which blocks recycled symbols.
    /// Sibling classes SHARE the issuer prefix — here that is the point, not a trap.
    /// </para>
    /// <para>
    /// Own frontier (<see cref="ListedCusipSweepCursorName"/>) starting at the archive origin:
    /// the alias sweep's cursor has already consumed the archive on long-running deployments,
    /// and these rows were never collected on that pass.
    /// </para>
    /// </summary>
    public async Task BackfillListedTickerCusips(CancellationToken cancellationToken)
    {
        var fileNames = await NextSweepFiles(ListedCusipSweepCursorName);
        if (fileNames.Count == 0)
        {
            return;
        }

        var primaryMap = await BuildTickerMap(cancellationToken);
        var secondaryMap = await BuildSecondaryTickerMap(primaryMap, cancellationToken);
        if (secondaryMap.Count == 0)
        {
            // Nothing to resolve against (fresh install, company sync not yet run). Do NOT
            // advance: consuming the archive now would permanently skip these files' rows,
            // and the frontier has no reset path. Wedging is already prevented per-file by
            // the download catch below; this state clears itself once stocks exist.
            return;
        }

        var strippedAliases = BuildStrippedSecondaryAliases(secondaryMap, primaryMap);
        // stockId → (listedTicker → cusips seen for it)
        var byStock = new Dictionary<Guid, Dictionary<string, HashSet<string>>>();

        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var records = await DownloadAndParse(fileName, cancellationToken);
                foreach (var record in records)
                {
                    if (string.IsNullOrEmpty(record.Cusip) || string.IsNullOrEmpty(record.Symbol))
                        continue;
                    // A primary symbol is the alias sweep's business, and letting it
                    // through here would record the primary's own CUSIP as a listing.
                    if (primaryMap.ContainsKey(record.Symbol))
                        continue;
                    if (
                        !secondaryMap.TryGetValue(record.Symbol, out var listing)
                        && !(
                            strippedAliases.TryGetValue(record.Symbol, out var spelled)
                            && secondaryMap.TryGetValue(spelled, out listing)
                        )
                    )
                        continue;

                    if (!byStock.TryGetValue(listing.StockId, out var byTicker))
                    {
                        byTicker = new Dictionary<string, HashSet<string>>(
                            StringComparer.OrdinalIgnoreCase
                        );
                        byStock[listing.StockId] = byTicker;
                    }
                    if (!byTicker.TryGetValue(listing.Ticker, out var cusips))
                    {
                        cusips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        byTicker[listing.Ticker] = cusips;
                    }
                    cusips.Add(record.Cusip);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                // A missing archive file costs coverage for that fortnight only; the
                // frontier still advances so the sweep cannot wedge on one bad file.
                _logger.LogWarning(
                    ex,
                    "Listed-CUSIP sweep: failed to download {File}, skipping",
                    fileName
                );
            }
        }

        var recorded = await RecordListedCusips(byStock, cancellationToken);
        await AdvanceSweepFrontier(ListedCusipSweepCursorName, fileNames[^1]);

        if (recorded > 0)
        {
            _logger.LogInformation(
                "Listed-CUSIP sweep: recorded {Count} listing CUSIP(s) across {Files} FTD file(s)",
                recorded,
                fileNames.Count
            );
        }
    }

    /// <summary>
    /// Replays the SEC archive into the exact-listing FTD key. The former importer admitted only
    /// primary tickers, so a schema backfill cannot recover secondary ETF rows that were skipped.
    /// A durable oldest-first frontier makes the repair bounded and restart-safe.
    /// </summary>
    public async Task BackfillListedRecords(CancellationToken cancellationToken)
    {
        var fileNames = await NextSweepFiles(ListedRecordSweepCursorName);
        if (fileNames.Count == 0)
            return;

        var tickerMap = await BuildListedTickerMap(cancellationToken);
        if (tickerMap.Count == 0)
            return;

        string lastCompletedFile = null;
        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var records = await DownloadAndParse(fileName, cancellationToken);
                await ImportRecords(records, tickerMap, cancellationToken);
                lastCompletedFile = fileName;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode == System.Net.HttpStatusCode.NotFound
                    && !IsRecentFtdFile(fileName))
            {
                _logger.LogWarning(
                    "Listed-record FTD sweep: archive file {File} is unavailable (404), advancing",
                    fileName
                );
                lastCompletedFile = fileName;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                _logger.LogWarning(
                    ex,
                    "Listed-record FTD sweep: failed to import {File}; retrying from this file",
                    fileName
                );
                break;
            }
        }

        if (lastCompletedFile != null)
            await AdvanceSweepFrontier(ListedRecordSweepCursorName, lastCompletedFile);
    }

    private async Task<int> RecordListedCusips(
        Dictionary<Guid, Dictionary<string, HashSet<string>>> byStock,
        CancellationToken cancellationToken
    )
    {
        if (byStock.Count == 0)
        {
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var stockManager = scope.ServiceProvider.GetRequiredService<CommonStockManager>();

        var stocks = await stockRepo.GetByIds(byStock.Keys).ToListAsync(cancellationToken);

        var recorded = 0;
        foreach (var stock in stocks)
        {
            // Without a current primary CUSIP there is no issuer prefix to anchor the
            // recycled-symbol guard against — skip rather than record unverifiable identity.
            if (stock.Cusip == null || !byStock.TryGetValue(stock.Id, out var byTicker))
                continue;

            var candidates = byTicker
                .SelectMany(kv =>
                    kv.Value.Where(c => CusipIdentity.SameIssuer(c, stock.Cusip))
                        .Select(c => (ListedTicker: kv.Key, Cusip: c))
                )
                .ToList();
            if (candidates.Count == 0)
                continue;

            recorded += await stockManager.RecordListedTickerCusips(stock, candidates);
        }

        return recorded;
    }

    /// <summary>
    /// Secondary ticker → owning stock. A symbol that is any stock's primary ticker is
    /// excluded (the primary space wins), and a symbol two stocks' secondary lists collapse
    /// onto is dropped rather than guessed — same refusal the stripped-alias builder applies.
    /// </summary>
    private async Task<Dictionary<string, (Guid StockId, string Ticker)>> BuildSecondaryTickerMap(
        Dictionary<string, Guid> primaryMap,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();

        var query =
            _workerOptions.TickersToSync?.Count > 0
                ? stockRepo.GetByTickers(_workerOptions.TickersToSync)
                : stockRepo.GetAll();
        var stocks = await query
            .Where(cs => cs.SecondaryTickers.Count > 0)
            .Select(cs => new { cs.Id, cs.SecondaryTickers })
            .ToListAsync(cancellationToken);
        var stockIds = stocks.Select(stock => stock.Id).ToList();
        var delistedRows = await stockRepo
            .GetDelistedListings()
            .Where(listing => stockIds.Contains(listing.CommonStockId))
            .Select(listing => new { listing.CommonStockId, listing.ListedTicker })
            .ToListAsync(cancellationToken);
        var delistedByStock = delistedRows
            .GroupBy(listing => listing.CommonStockId)
            .ToDictionary(
                group => group.Key,
                group =>
                    group
                        .Select(listing => listing.ListedTicker)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
            );

        var map = new Dictionary<string, (Guid StockId, string Ticker)>(
            StringComparer.OrdinalIgnoreCase
        );
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stock in stocks)
        {
            foreach (var ticker in stock.SecondaryTickers.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                if (
                    delistedByStock.TryGetValue(stock.Id, out var delisted)
                    && delisted.Contains(ticker)
                )
                    continue;
                if (primaryMap.ContainsKey(ticker))
                    continue;
                if (!map.TryAdd(ticker, (stock.Id, ticker)) && map[ticker].StockId != stock.Id)
                    ambiguous.Add(ticker);
            }
        }

        foreach (var key in ambiguous)
            map.Remove(key);

        return map;
    }

    /// <summary>
    /// CNS separator-stripped spellings of the secondary tickers ("BRKA" → "BRK-A"), never
    /// shadowing a real primary or secondary symbol, ambiguous collapses dropped — the same
    /// rules <see cref="BuildStrippedTickerAliases"/> applies to the primary space.
    /// </summary>
    private static Dictionary<string, string> BuildStrippedSecondaryAliases(
        Dictionary<string, (Guid StockId, string Ticker)> secondaryMap,
        Dictionary<string, Guid> primaryMap
    )
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in secondaryMap.Keys)
        {
            var stripped = string.Concat(ticker.Where(char.IsLetterOrDigit));
            if (
                stripped.Length == 0
                || string.Equals(stripped, ticker, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            if (primaryMap.ContainsKey(stripped) || secondaryMap.ContainsKey(stripped))
                continue;
            if (!aliases.TryAdd(stripped, ticker))
                ambiguous.Add(stripped);
        }

        foreach (var key in ambiguous)
            aliases.Remove(key);

        return aliases;
    }

    private async Task<int> RecordRetiredCusips(
        Dictionary<string, HashSet<string>> cusipsByTicker,
        CancellationToken cancellationToken
    )
    {
        if (cusipsByTicker.Count == 0)
        {
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var stockManager = scope.ServiceProvider.GetRequiredService<CommonStockManager>();

        var stocks = await stockRepo
            .GetByTickers(cusipsByTicker.Keys.ToList())
            .ToListAsync(cancellationToken);

        var recorded = 0;
        foreach (var stock in stocks)
        {
            // Only the stock's OWN symbol may contribute: a secondary ticker names a
            // different security sharing this filer's row, and its CUSIP is not this
            // security's retired identity.
            if (stock.Cusip == null || !cusipsByTicker.TryGetValue(stock.Ticker, out var seen))
                continue;

            var retired = seen.Where(c => CusipIdentity.SameIssuer(c, stock.Cusip)).ToList();
            if (retired.Count == 0)
                continue;

            recorded += await stockManager.RecordRetiredCusipAliases(stock, retired);
        }

        return recorded;
    }

    private async Task<Dictionary<string, List<HistoricalTickerIdentity>>> BuildInactiveTickerMap(
        DateTime sweepStartedAt,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var query = stockRepo
            .GetDelistedListings()
            .Where(listing =>
                listing.Cusip == null
                && (
                    listing.HistoricalCusipBackfillRequestedAt == null
                    || listing.HistoricalCusipBackfillRequestedAt <= sweepStartedAt
                )
            );
        if (_workerOptions.TickersToSync?.Count > 0)
        {
            query = query.Where(listing =>
                _workerOptions.TickersToSync.Contains(listing.ListedTicker)
            );
        }

        var listings = await query.ToListAsync(cancellationToken);
        foreach (
            var listing in listings.Where(listing =>
                listing.HistoricalCusipBackfillSweepStartedAt != sweepStartedAt
            )
        )
        {
            listing.HistoricalCusipBackfillCandidates = [];
            listing.HistoricalCusipBackfillCandidateOn = null;
            listing.HistoricalCusipBackfillAmbiguous = false;
            listing.HistoricalCusipBackfillSweepStartedAt = sweepStartedAt;
        }
        await stockRepo.SaveChanges();

        var identities = listings.Select(listing => new HistoricalTickerIdentity(
            listing.Id,
            listing.ListedTicker,
            listing.DelistedOn
        ));

        return identities
            .Where(identity => !string.IsNullOrWhiteSpace(identity.Ticker))
            .GroupBy(identity => identity.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(identity => identity.DelistedOn).ToList(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    internal static bool TryResolveHistoricalIdentity(
        string symbol,
        DateOnly settlementDate,
        Dictionary<string, List<HistoricalTickerIdentity>> tickerMap,
        Dictionary<string, string> strippedAliases,
        out Guid listingId
    )
    {
        listingId = default;
        var ticker = tickerMap.ContainsKey(symbol)
            ? symbol
            : strippedAliases.GetValueOrDefault(symbol);
        if (ticker == null || !tickerMap.TryGetValue(ticker, out var identities))
        {
            return false;
        }

        var eligible = identities
            .Where(identity => settlementDate <= identity.DelistedOn)
            .OrderBy(identity => identity.DelistedOn)
            .Take(2)
            .ToList();
        if (
            eligible.Count == 0
            || (eligible.Count > 1 && eligible[0].DelistedOn == eligible[1].DelistedOn)
        )
        {
            return false;
        }

        listingId = eligible[0].ListingId;
        return true;
    }

    private async Task<int> SeedInactiveCusips(
        Dictionary<Guid, (string Cusip, DateOnly SettlementDate)> latestByListing,
        DateTime sweepStartedAt,
        CancellationToken cancellationToken
    )
    {
        if (latestByListing.Count == 0)
        {
            return 0;
        }

        using var scope = _scopeFactory.CreateScope();
        var stockManager = scope.ServiceProvider.GetRequiredService<CommonStockManager>();
        var seeded = 0;
        foreach (var candidate in latestByListing.OrderBy(candidate => candidate.Key))
        {
            var result = await stockManager.SeedDelistedListingCusip(
                candidate.Key,
                candidate.Value.Cusip,
                candidate.Value.SettlementDate,
                sweepStartedAt,
                cancellationToken
            );
            if (result == DelistedListingCusipSeedResult.ClaimedByAnotherStock)
            {
                _logger.LogWarning(
                    "Inactive FTD listing {ListingId} resolved to CUSIP {Cusip}, but that CUSIP is already claimed by another stock — skipping",
                    candidate.Key,
                    candidate.Value.Cusip
                );
            }
            if (result == DelistedListingCusipSeedResult.Seeded)
            {
                seeded++;
            }
        }

        return seeded;
    }

    private async Task StageInactiveCusipEvidence(
        IEnumerable<HistoricalCusipCandidate> candidates,
        DateTime sweepStartedAt,
        CancellationToken cancellationToken
    )
    {
        var batch = candidates.ToList();
        if (batch.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var listings = await stockRepo
            .GetDelistedListings()
            .Where(listing =>
                batch.Select(candidate => candidate.ListingId).Distinct().Contains(listing.Id)
                && listing.Cusip == null
                && listing.HistoricalCusipBackfillSweepStartedAt == sweepStartedAt
            )
            .ToListAsync(cancellationToken);
        foreach (var listing in listings)
        {
            ApplyHistoricalCusipEvidence(
                listing,
                batch.Where(candidate => candidate.ListingId == listing.Id)
            );
        }
        await stockRepo.SaveChanges();

        var staged = await stockRepo
            .GetDelistedListings()
            .Where(listing =>
                listing.Cusip == null
                && listing.HistoricalCusipBackfillSweepStartedAt == sweepStartedAt
            )
            .ToListAsync(cancellationToken);
        RejectContestedHistoricalCusips(staged);
        await stockRepo.SaveChanges();
    }

    internal static void RejectContestedHistoricalCusips(
        IEnumerable<CommonStockDelistedListing> listings
    )
    {
        var contestedListingIds = listings
            .SelectMany(listing =>
                listing
                    .HistoricalCusipBackfillCandidates.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(cusip => new { listing.Id, Cusip = cusip })
            )
            .GroupBy(claim => claim.Cusip, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(claim => claim.Id).Distinct().Count() > 1)
            .SelectMany(group => group.Select(claim => claim.Id))
            .ToHashSet();
        foreach (
            var contested in listings.Where(listing => contestedListingIds.Contains(listing.Id))
        )
        {
            contested.HistoricalCusipBackfillAmbiguous = true;
        }
    }

    internal static void ApplyHistoricalCusipEvidence(
        CommonStockDelistedListing listing,
        IEnumerable<HistoricalCusipCandidate> candidates
    )
    {
        foreach (var candidate in candidates)
        {
            if (
                listing.HistoricalCusipBackfillCandidateOn == null
                || candidate.SettlementDate > listing.HistoricalCusipBackfillCandidateOn
            )
            {
                listing.HistoricalCusipBackfillCandidates = [candidate.Cusip];
                listing.HistoricalCusipBackfillCandidateOn = candidate.SettlementDate;
                listing.HistoricalCusipBackfillAmbiguous = false;
                continue;
            }
            if (
                candidate.SettlementDate == listing.HistoricalCusipBackfillCandidateOn
                && !listing.HistoricalCusipBackfillCandidates.Contains(
                    candidate.Cusip,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                listing.HistoricalCusipBackfillCandidates.Add(candidate.Cusip);
                listing.HistoricalCusipBackfillAmbiguous = true;
            }
        }
    }

    private async Task FinalizeInactiveCusips(
        DateTime sweepStartedAt,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var staged = await stockRepo
            .GetDelistedListings()
            .Where(listing =>
                listing.Cusip == null
                && listing.HistoricalCusipBackfillSweepStartedAt == sweepStartedAt
                && listing.HistoricalCusipBackfillCandidateOn != null
            )
            .ToListAsync(cancellationToken);
        var candidates = staged
            .Where(listing =>
                !listing.HistoricalCusipBackfillAmbiguous
                && listing.HistoricalCusipBackfillCandidates.Count == 1
            )
            .ToDictionary(
                listing => listing.Id,
                listing =>
                    (
                        listing.HistoricalCusipBackfillCandidates[0],
                        listing.HistoricalCusipBackfillCandidateOn!.Value
                    )
            );

        var seeded = await SeedInactiveCusips(candidates, sweepStartedAt, cancellationToken);
        if (seeded > 0)
        {
            _logger.LogInformation(
                "Inactive-CUSIP sweep: seeded {Count} identity CUSIP(s) after the complete archive pass",
                seeded
            );
        }
    }

    private static bool IsOldestArchiveFile(string fileName) =>
        fileName != null
        && string.Equals(fileName, GetFileNames(OldestAvailableDate)[0], StringComparison.Ordinal);

    private async Task<(List<string> FileNames, DateTime? StartedAt)> NextInactiveCusipSweepFiles(
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<BackfillStateRepository>();
        var stockRepo = scope.ServiceProvider.GetRequiredService<CommonStockRepository>();
        var state = await stateRepo.GetByName(InactiveCusipSweepCursorName);

        var pending = stockRepo.GetDelistedListings().Where(listing => listing.Cusip == null);
        if (_workerOptions.TickersToSync?.Count > 0)
        {
            pending = pending.Where(listing =>
                _workerOptions.TickersToSync.Contains(listing.ListedTicker)
            );
        }

        if (state == null)
        {
            if (!await pending.AnyAsync(cancellationToken))
            {
                return ([], null);
            }

            state = new BackfillState
            {
                Name = InactiveCusipSweepCursorName,
                LastFullRescanAt = DateTime.UtcNow,
            };
            stateRepo.Add(state);
            await stateRepo.SaveChanges();
        }

        var all = GetStableHistoricalFileNames(
            OldestAvailableDate,
            DateOnly.FromDateTime(state.LastFullRescanAt ?? DateTime.UtcNow)
        );
        all.Reverse();
        var startIndex = 0;
        if (state.Floor != null)
        {
            var index = all.IndexOf(FileNameOf(state.Floor.Value));
            if (index < 0)
            {
                return ([], state.LastFullRescanAt);
            }
            startIndex = index + 1;
        }

        if (startIndex >= all.Count)
        {
            var startedAt = state.LastFullRescanAt ?? DateTime.MinValue;
            var requestedAfterStart = await pending.AnyAsync(
                listing =>
                    listing.HistoricalCusipBackfillRequestedAt != null
                    && listing.HistoricalCusipBackfillRequestedAt > startedAt,
                cancellationToken
            );
            if (!requestedAfterStart)
            {
                return ([], state.LastFullRescanAt);
            }

            state.Floor = null;
            state.LastFullRescanAt = DateTime.UtcNow;
            await stateRepo.SaveChanges();
            all = GetStableHistoricalFileNames(
                OldestAvailableDate,
                DateOnly.FromDateTime(state.LastFullRescanAt.Value)
            );
            all.Reverse();
            startIndex = 0;
        }

        return (
            all.Skip(startIndex).Take(AliasSweepFilesPerCycle).ToList(),
            state.LastFullRescanAt
        );
    }

    // The sweeps run oldest-first from the start of the archive and stop once their
    // frontier passes the newest file — the live import owns everything from there.
    private async Task<List<string>> NextSweepFiles(string cursorName)
    {
        using var scope = _scopeFactory.CreateScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<BackfillStateRepository>();
        var state = await stateRepo.GetByName(cursorName);

        var all = GetFileNames(OldestAvailableDate);
        var startIndex = 0;
        if (state?.Floor != null)
        {
            // A frontier no current file name matches means the archive naming moved on;
            // sweeping from the start again would re-download years for nothing.
            var index = all.IndexOf(FileNameOf(state.Floor.Value));
            if (index < 0)
            {
                return [];
            }
            startIndex = index + 1;
        }

        return all.Skip(startIndex).Take(AliasSweepFilesPerCycle).ToList();
    }

    private async Task AdvanceSweepFrontier(string cursorName, string lastFileName)
    {
        using var scope = _scopeFactory.CreateScope();
        var stateRepo = scope.ServiceProvider.GetRequiredService<BackfillStateRepository>();
        var state = await stateRepo.GetByName(cursorName);
        if (state == null)
        {
            state = new BackfillState { Name = cursorName };
            stateRepo.Add(state);
        }

        state.Floor = FrontierOf(lastFileName);
        await stateRepo.SaveChanges();
    }

    // The frontier is stored as the swept file's own timestamp so the cursor round-trips
    // through BackfillState's DateTime column: the month at midnight, plus a day for the
    // second-half ("b") file so the two halves of one month stay ordered.
    internal static DateTime FrontierOf(string fileName)
    {
        var yearMonth = fileName["cnsfails".Length..];
        var month = DateTime.ParseExact(
            yearMonth[..6],
            "yyyyMM",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal
        );
        return yearMonth[6] == 'b' ? month.AddDays(1) : month;
    }

    internal static string FileNameOf(DateTime frontier)
    {
        var half = frontier.Day > 1 ? "b" : "a";
        return $"cnsfails{frontier:yyyyMM}{half}.zip";
    }

    private const string AliasSweepCursorName = "Ftd.RetiredCusipSweep";

    private const string InactiveCusipSweepCursorName = "Ftd.InactiveCusipSweep";

    // Separate cursor: the alias sweep's frontier has already consumed the archive on
    // long-running deployments, and listed-CUSIP rows were never collected on that pass.
    private const string ListedCusipSweepCursorName = "Ftd.ListedCusipSweep";
    private const string ListedRecordSweepCursorName = "Ftd.ListedRecordSweepV1";

    // Twelve fortnightly files ≈ six months of archive per daily cycle, so the whole
    // 2017→today range is swept in about a fortnight of cycles without ever making the
    // FTD worker's run long.
    private const int AliasSweepFilesPerCycle = 12;

    private sealed record LiveIdentityEvidence(
        string SourceFile,
        DateOnly LatestPrimaryDate,
        List<FtdRecord> PrimaryRecords,
        List<FtdRecord> Records
    );

    private sealed record SecondaryIdentityObservation(
        Guid StockId,
        string Ticker,
        FtdRecord Record
    );

    private static string FtdMonthOf(string fileName) => fileName[8..14];

    private static void AccumulateLiveIdentityEvidence(
        string fileName,
        List<FtdRecord> records,
        Dictionary<string, Guid> primaryMap,
        Dictionary<string, (Guid StockId, string Ticker)> secondaryMap,
        Dictionary<string, LiveIdentityEvidence> evidenceByTicker
    )
    {
        var strippedPrimaryAliases = BuildStrippedTickerAliases(primaryMap);
        var primaryObservations = new Dictionary<string, List<FtdRecord>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var record in records)
        {
            if (
                string.IsNullOrEmpty(record.Cusip)
                || string.IsNullOrEmpty(record.Symbol)
                || !TryResolveSymbol(
                    record.Symbol,
                    primaryMap,
                    strippedPrimaryAliases,
                    out var ticker
                )
            )
            {
                continue;
            }

            if (!primaryObservations.TryGetValue(ticker, out var rows))
            {
                rows = [];
                primaryObservations[ticker] = rows;
            }
            rows.Add(record);
        }

        var secondaryObservations = BuildLatestSecondaryObservations(
            records,
            primaryMap,
            secondaryMap
        );

        foreach (var pair in primaryObservations)
        {
            var latestDate = pair.Value.Max(record => record.SettlementDate);
            var latestPrimaryRecords = pair
                .Value.Where(record => record.SettlementDate == latestDate)
                .ToList();
            var stockId = primaryMap[pair.Key];
            var candidateRecords = latestPrimaryRecords
                .Concat(
                    secondaryObservations.TryGetValue(stockId, out var secondaryRows)
                        ? secondaryRows.Select(observation => observation.Record)
                        : []
                )
                .ToList();
            var candidate = new LiveIdentityEvidence(
                fileName,
                latestDate,
                latestPrimaryRecords,
                candidateRecords
            );

            MergeLiveIdentityEvidence(pair.Key, candidate, evidenceByTicker);
        }
    }

    private static void MergeLiveIdentityEvidence(
        string ticker,
        LiveIdentityEvidence candidate,
        Dictionary<string, LiveIdentityEvidence> evidenceByTicker
    )
    {
        if (!evidenceByTicker.TryGetValue(ticker, out var current))
        {
            evidenceByTicker[ticker] = candidate;
            return;
        }
        if (candidate.LatestPrimaryDate > current.LatestPrimaryDate)
        {
            evidenceByTicker[ticker] = candidate;
            return;
        }
        if (candidate.LatestPrimaryDate < current.LatestPrimaryDate)
            return;

        var latestCusips = current
            .PrimaryRecords.Concat(candidate.PrimaryRecords)
            .Select(record => record.Cusip.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        if (latestCusips.Count > 1)
        {
            var ambiguousPrimaryRecords = current
                .PrimaryRecords.Concat(candidate.PrimaryRecords)
                .ToList();
            evidenceByTicker[ticker] = new LiveIdentityEvidence(
                string.CompareOrdinal(candidate.SourceFile, current.SourceFile) > 0
                    ? candidate.SourceFile
                    : current.SourceFile,
                candidate.LatestPrimaryDate,
                ambiguousPrimaryRecords,
                ambiguousPrimaryRecords
            );
            return;
        }

        if (string.CompareOrdinal(candidate.SourceFile, current.SourceFile) > 0)
            evidenceByTicker[ticker] = candidate;
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
        var secondaryMap = await BuildSecondaryTickerMap(tickerMap, cancellationToken);
        return await SeedCusipsWithSecondaryMap(
            records,
            tickerMap,
            secondaryMap,
            cancellationToken
        );
    }

    private async Task<int> SeedCusipsWithSecondaryMap(
        List<FtdRecord> records,
        Dictionary<string, Guid> tickerMap,
        Dictionary<string, (Guid StockId, string Ticker)> secondaryMap,
        CancellationToken cancellationToken
    )
    {
        var strippedAliases = BuildStrippedTickerAliases(tickerMap);
        // Latest settlement date wins: during a CUSIP transition a single FTD
        // file can carry both the retiring and the replacement CUSIP for one
        // symbol, and the most recent trading day reflects the current security.
        var primaryObservations = new Dictionary<string, List<FtdRecord>>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.Cusip) || string.IsNullOrEmpty(record.Symbol))
                continue;
            if (!TryResolveSymbol(record.Symbol, tickerMap, strippedAliases, out var ticker))
                continue;
            if (!primaryObservations.TryGetValue(ticker, out var rows))
            {
                rows = [];
                primaryObservations[ticker] = rows;
            }
            rows.Add(record);
        }

        var tickerToCusip = primaryObservations
            .Select(pair =>
            {
                var latestDate = pair.Value.Max(record => record.SettlementDate);
                var latestCusips = pair
                    .Value.Where(record => record.SettlementDate == latestDate)
                    .Select(record => record.Cusip.Trim().ToUpperInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(2)
                    .ToList();
                return (
                    Ticker: pair.Key,
                    Cusip: latestCusips.Count == 1 ? latestCusips[0] : null,
                    SettlementDate: latestDate
                );
            })
            .Where(candidate => candidate.Cusip != null)
            .ToDictionary(
                candidate => candidate.Ticker,
                candidate => (candidate.Cusip, candidate.SettlementDate),
                StringComparer.OrdinalIgnoreCase
            );

        if (tickerToCusip.Count == 0)
            return 0;

        var secondaryCusips = BuildLatestSecondaryCusips(records, tickerMap, secondaryMap);

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
        var cusipOwners = new Dictionary<string, HashSet<Guid>>(StringComparer.OrdinalIgnoreCase);
        void AddOwner(string cusip, Guid ownerId)
        {
            if (!cusipOwners.TryGetValue(cusip, out var ownerIds))
            {
                ownerIds = [];
                cusipOwners[cusip] = ownerIds;
            }
            ownerIds.Add(ownerId);
        }

        var owners = await stockRepo
            .GetAllIncludingInactive()
            .Where(s => s.Cusip != null && resolvedCusips.Contains(s.Cusip))
            .Select(s => new { s.Id, s.Cusip })
            .ToListAsync(cancellationToken);
        foreach (var owner in owners)
        {
            AddOwner(owner.Cusip, owner.Id);
        }
        var aliasOwners = await stockRepo
            .GetCusipAliases()
            .Where(alias => resolvedCusips.Contains(alias.Cusip))
            .Select(alias => new { alias.CommonStockId, alias.Cusip })
            .ToListAsync(cancellationToken);
        foreach (var owner in aliasOwners)
        {
            AddOwner(owner.Cusip, owner.CommonStockId);
        }
        var listedClaimRows = await stockRepo
            .GetListedCusips()
            .Where(listing => resolvedCusips.Contains(listing.Cusip))
            .Select(listing => new
            {
                listing.CommonStockId,
                listing.ListedTicker,
                listing.Cusip,
            })
            .ToListAsync(cancellationToken);

        var seeded = 0;
        foreach (var stock in stocks)
        {
            if (!tickerToCusip.TryGetValue(stock.Ticker, out var resolved))
                continue;
            if (string.Equals(stock.Cusip, resolved.Cusip, StringComparison.OrdinalIgnoreCase))
                continue;
            var listedClaim = listedClaimRows.FirstOrDefault(listing =>
                string.Equals(listing.Cusip, resolved.Cusip, StringComparison.OrdinalIgnoreCase)
            );
            var promotesExactListing =
                listedClaim?.CommonStockId == stock.Id
                && string.Equals(
                    listedClaim.ListedTicker,
                    stock.Ticker,
                    StringComparison.OrdinalIgnoreCase
                );
            var displacedTicker = promotesExactListing
                ? ResolveDisplacedListedTicker(stock, secondaryCusips)
                : null;
            if (
                (listedClaim != null && !promotesExactListing)
                || (promotesExactListing && displacedTicker == null)
                || (
                    cusipOwners.TryGetValue(resolved.Cusip, out var ownerIds)
                    && ownerIds.Any(ownerId => ownerId != stock.Id)
                )
            )
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
            if (await stockManager.SetCusip(stock, resolved.Cusip, displacedTicker))
            {
                AddOwner(resolved.Cusip, stock.Id);
                seeded++;
            }
        }

        return seeded;
    }

    private static Dictionary<Guid, List<(string Ticker, string Cusip)>> BuildLatestSecondaryCusips(
        IEnumerable<FtdRecord> records,
        Dictionary<string, Guid> primaryMap,
        Dictionary<string, (Guid StockId, string Ticker)> secondaryMap
    )
    {
        var observations = BuildLatestSecondaryObservations(records, primaryMap, secondaryMap);
        var unambiguous = observations
            .SelectMany(pair =>
                pair.Value.GroupBy(
                        observation => observation.Ticker,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Select(group =>
                    {
                        var latestCusips = group
                            .Select(observation =>
                                observation.Record.Cusip.Trim().ToUpperInvariant()
                            )
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(2)
                            .ToList();
                        return (
                            StockId: pair.Key,
                            Ticker: group.Key,
                            Cusip: latestCusips.Count == 1 ? latestCusips[0] : null
                        );
                    })
            )
            .Where(pair => pair.Cusip != null)
            .ToList();

        return unambiguous
            .GroupBy(pair => pair.StockId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => (pair.Ticker, pair.Cusip)).ToList()
            );
    }

    private static Dictionary<
        Guid,
        List<SecondaryIdentityObservation>
    > BuildLatestSecondaryObservations(
        IEnumerable<FtdRecord> records,
        Dictionary<string, Guid> primaryMap,
        Dictionary<string, (Guid StockId, string Ticker)> secondaryMap
    )
    {
        var strippedAliases = BuildStrippedSecondaryAliases(secondaryMap, primaryMap);
        var observations = new Dictionary<(Guid StockId, string Ticker), List<FtdRecord>>();
        foreach (var record in records)
        {
            if (
                string.IsNullOrEmpty(record.Cusip)
                || string.IsNullOrEmpty(record.Symbol)
                || primaryMap.ContainsKey(record.Symbol)
            )
                continue;

            if (
                !secondaryMap.TryGetValue(record.Symbol, out var listing)
                && !(
                    strippedAliases.TryGetValue(record.Symbol, out var spelled)
                    && secondaryMap.TryGetValue(spelled, out listing)
                )
            )
                continue;

            var key = (listing.StockId, listing.Ticker);
            if (!observations.TryGetValue(key, out var rows))
            {
                rows = [];
                observations[key] = rows;
            }
            rows.Add(record);
        }

        return observations
            .SelectMany(pair =>
            {
                var latestDate = pair.Value.Max(record => record.SettlementDate);
                return pair
                    .Value.Where(record => record.SettlementDate == latestDate)
                    .Select(record => new SecondaryIdentityObservation(
                        pair.Key.StockId,
                        pair.Key.Ticker,
                        record
                    ));
            })
            .GroupBy(observation => observation.StockId)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private static string ResolveDisplacedListedTicker(
        CommonStock stock,
        IReadOnlyDictionary<Guid, List<(string Ticker, string Cusip)>> secondaryCusips
    )
    {
        if (stock.Cusip == null || !secondaryCusips.TryGetValue(stock.Id, out var candidates))
            return null;

        var matches = candidates
            .Where(candidate =>
                string.Equals(candidate.Cusip, stock.Cusip, StringComparison.OrdinalIgnoreCase)
            )
            .Select(candidate => candidate.Ticker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
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
    ) => BuildStrippedTickerAliases(tickerMap.Keys);

    private static Dictionary<string, string> BuildStrippedTickerAliases(
        IEnumerable<string> tickers
    )
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tickerSet = new HashSet<string>(tickers, StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in tickerSet)
        {
            var stripped = string.Concat(ticker.Where(char.IsLetterOrDigit));
            if (
                stripped.Length == 0
                || string.Equals(stripped, ticker, StringComparison.OrdinalIgnoreCase)
            )
                continue;
            if (tickerSet.Contains(stripped))
                continue;
            if (!aliases.TryAdd(stripped, ticker))
                ambiguous.Add(stripped);
        }

        foreach (var key in ambiguous)
            aliases.Remove(key);

        return aliases;
    }

    internal sealed record HistoricalTickerIdentity(
        Guid ListingId,
        string Ticker,
        DateOnly DelistedOn
    );

    internal sealed record HistoricalCusipCandidate(
        Guid ListingId,
        string Cusip,
        DateOnly SettlementDate
    );

    private static bool TryResolveSymbol<TValue>(
        string symbol,
        Dictionary<string, TValue> tickerMap,
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
        Dictionary<string, ListedSecurityKey> tickerMap,
        CancellationToken cancellationToken
    )
    {
        // Group by stock+date, keeping the latest record per day (FTD is cumulative)
        var grouped = new Dictionary<(Guid StockId, string ListedTicker, DateOnly Date), FailToDeliver>();

        var strippedAliases = BuildStrippedTickerAliases(tickerMap.Keys);
        foreach (var record in records)
        {
            if (
                string.IsNullOrEmpty(record.Symbol)
                || !TryResolveSymbol(record.Symbol, tickerMap, strippedAliases, out var ticker)
                || !tickerMap.TryGetValue(ticker, out var listing)
            )
            {
                continue;
            }

            var key = (listing.CommonStockId, listing.ListedTicker, record.SettlementDate);
            grouped[key] = new FailToDeliver
            {
                CommonStockId = listing.CommonStockId,
                ListedTicker = listing.ListedTicker,
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
            .On(f => new { f.CommonStockId, f.ListedTicker, f.SettlementDate })
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

    private async Task<Dictionary<string, ListedSecurityKey>> BuildListedTickerMap(
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var tickerMapService = scope.ServiceProvider.GetRequiredService<TickerMapService>();
        return await tickerMapService.BuildListed(_workerOptions.TickersToSync, cancellationToken);
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
            throw new InvalidDataException($"FTD archive {fileName} contains no entries.");
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

        if (records.Count == 0)
        {
            throw new InvalidDataException($"FTD archive {fileName} contains no valid rows.");
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
    /// Replays the prior calendar month so late identity corrections can be applied from
    /// recent FTD evidence without turning the live import into an unbounded backfill.
    /// </summary>
    internal static DateOnly ApplyLiveRecheckWindow(
        DateOnly syncStartDate,
        DateOnly minimumStartDate
    )
    {
        var syncMonth = new DateOnly(syncStartDate.Year, syncStartDate.Month, 1);
        var configuredMinimumMonth = new DateOnly(minimumStartDate.Year, minimumStartDate.Month, 1);
        var minimumMonth =
            configuredMinimumMonth < OldestAvailableDate
                ? OldestAvailableDate
                : configuredMinimumMonth;

        if (syncMonth <= minimumMonth)
            return minimumMonth;

        var recheckStart = syncMonth.AddMonths(-LiveRecheckMonths);
        return recheckStart < minimumMonth ? minimumMonth : recheckStart;
    }

    /// <summary>
    /// Generates FTD file names from a start date to now.
    /// Format: cnsfails{YYYYMM}{a|b}.zip (a = first half, b = second half)
    /// </summary>
    internal static List<string> GetFileNames(DateOnly startDate) =>
        GetFileNames(startDate, DateOnly.FromDateTime(DateTime.UtcNow));

    private static List<string> GetFileNames(DateOnly startDate, DateOnly asOf)
    {
        var fileNames = new List<string>();

        if (startDate < OldestAvailableDate)
            startDate = OldestAvailableDate;

        var current = new DateOnly(startDate.Year, startDate.Month, 1);

        while (current <= asOf)
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
    /// Returns only archive months that were already outside the SEC's 45-day publication
    /// window when a historical sweep started. Freezing this ceiling prevents a moving,
    /// unpublished current-month file from pinning a newest-first sweep forever.
    /// </summary>
    internal static List<string> GetStableHistoricalFileNames(DateOnly startDate, DateOnly asOf)
    {
        var firstUnstableMonth = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(-2);
        var lastStableMonth = firstUnstableMonth.AddMonths(-1);
        return GetFileNames(startDate)
            .Where(fileName =>
            {
                var frontier = FrontierOf(fileName);
                return new DateOnly(frontier.Year, frontier.Month, 1) <= lastStableMonth;
            })
            .ToList();
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
