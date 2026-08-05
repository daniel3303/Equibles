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
                    ex,
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
            catch (HttpRequestException ex)
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
            // Nothing to resolve against — still advance so the sweep cannot wedge here.
            await AdvanceSweepFrontier(ListedCusipSweepCursorName, fileNames[^1]);
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
            catch (HttpRequestException ex)
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

        var stocks = await stockRepo
            .GetByIds(byStock.Keys)
            .ToListAsync(cancellationToken);

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

        var map = new Dictionary<string, (Guid StockId, string Ticker)>(
            StringComparer.OrdinalIgnoreCase
        );
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stock in stocks)
        {
            foreach (var ticker in stock.SecondaryTickers.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
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

    // Separate cursor: the alias sweep's frontier has already consumed the archive on
    // long-running deployments, and listed-CUSIP rows were never collected on that pass.
    private const string ListedCusipSweepCursorName = "Ftd.ListedCusipSweep";

    // Twelve fortnightly files ≈ six months of archive per daily cycle, so the whole
    // 2017→today range is swept in about a fortnight of cycles without ever making the
    // FTD worker's run long.
    private const int AliasSweepFilesPerCycle = 12;

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
