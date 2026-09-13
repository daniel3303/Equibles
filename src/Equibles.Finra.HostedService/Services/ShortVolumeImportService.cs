using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.Core.AutoWiring;
using Equibles.Core.Calendars;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Configuration;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Equibles.Finra.HostedService.Services;

[Service]
public class ShortVolumeImportService
{
    // v2: v1 partitions were imported through a case-insensitive ticker map that folded
    // FINRA's lowercase preferred/when-issued symbols onto common tickers (TpC summed into
    // TPC) — the aggregates are corrupt wherever a case-variant sibling traded. The bump
    // orphans every v1 marker, so CandidateDates re-imports the full history newest-first
    // (bounded per cycle) and the upsert REPLACES the corrupted sums.
    // v3 carries exact listing identity, so ETF and other reference tickers no longer collapse
    // into the filer's primary row. The new partition key replays the bounded history.
    private const string Dataset = "daily-short-volume-files-v3";
    private const int CorrectionLookbackDays = 7;
    private static readonly DateOnly FirstConsolidatedFileDate = new(2018, 8, 1);
    private static readonly TimeSpan RecentPartitionRefreshInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShortVolumeImportService> _logger;
    private readonly IFinraClient _finraClient;
    private readonly TickerMapService _tickerMapService;
    private readonly ErrorReporter _errorReporter;
    private readonly WorkerOptions _workerOptions;
    private readonly FinraScraperOptions _finraOptions;
    private readonly FinraImportPartitionTracker _partitionTracker;
    private readonly TimeProvider _timeProvider;

    public ShortVolumeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<ShortVolumeImportService> logger,
        IFinraClient finraClient,
        TickerMapService tickerMapService,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions,
        IOptions<FinraScraperOptions> finraOptions,
        FinraImportPartitionTracker partitionTracker,
        TimeProvider timeProvider
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _finraClient = finraClient;
        _tickerMapService = tickerMapService;
        _errorReporter = errorReporter;
        _workerOptions = workerOptions.Value;
        _finraOptions = finraOptions.Value;
        _partitionTracker = partitionTracker;
        _timeProvider = timeProvider;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var floor = SyncDateResolver.Resolve(default, _workerOptions);
        if (floor < FirstConsolidatedFileDate)
            floor = FirstConsolidatedFileDate;

        var now = _timeProvider.GetUtcNow();
        var endDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (floor > endDate)
        {
            _logger.LogInformation(
                "Short volume sync floor {Floor} is in the future; nothing to import",
                floor
            );
            return;
        }

        // The resolved universe is part of completeness identity. A date checked before a stock
        // was added is not complete for that stock, so a universe change gets a fresh bounded,
        // newest-first pass instead of inheriting the old global "all" markers.
        var tickerMap = await _tickerMapService.BuildNativeListed(
            _workerOptions.TickersToSync,
            cancellationToken,
            StringComparer.Ordinal
        );
        if (tickerMap.Count == 0)
        {
            _logger.LogInformation(
                "No common stocks resolved for FINRA daily short-volume import; leaving partitions retryable"
            );
            return;
        }

        var scopeKey = FinraImportScope.ResolveListingImportScope(
            tickerMap,
            _workerOptions.TickersToSync
        );
        var completed = await _partitionTracker.GetCompleted(
            Dataset,
            scopeKey,
            floor,
            endDate,
            cancellationToken
        );
        var dates = CandidateDates(floor, endDate, now.UtcDateTime, completed)
            .Take(Math.Max(1, _finraOptions.ShortVolumeBackfillDatesPerCycle))
            .ToList();

        _logger.LogInformation(
            "Reconciling {Attempted} FINRA daily short-volume partitions in {Start}..{End}; {Completed} already complete for scope {Scope}",
            dates.Count,
            floor,
            endDate,
            completed.Count,
            scopeKey
        );

        foreach (var date in dates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ImportSingleDay(date, tickerMap, scopeKey, now.UtcDateTime, cancellationToken);
        }
    }

    private static IEnumerable<DateOnly> CandidateDates(
        DateOnly floor,
        DateOnly endDate,
        DateTime now,
        IReadOnlyDictionary<DateOnly, FinraImportPartition> completed
    )
    {
        var refreshCutoff = endDate.AddDays(-CorrectionLookbackDays);
        var refreshBefore = now - RecentPartitionRefreshInterval;

        for (var date = endDate; date >= floor; date = date.AddDays(-1))
        {
            if (!UsMarketCalendar.IsTradingDay(date))
                continue;

            if (!completed.TryGetValue(date, out var partition))
            {
                yield return date;
                continue;
            }

            if (date >= refreshCutoff && partition.ImportedAt <= refreshBefore)
                yield return date;
        }
    }

    private async Task<bool> ImportSingleDay(
        DateOnly date,
        IReadOnlyDictionary<string, EquityListingReference> tickerMap,
        string scopeKey,
        DateTime importedAt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var records = await _finraClient.GetDailyShortVolume(date, cancellationToken);
            var aggregated = AggregateVolumesByStock(records, tickerMap, date);
            var collisionOnlyStocks = CollisionOnlyStocks(records, tickerMap, aggregated);
            var totalImported = await UpsertDay(
                aggregated.Values,
                collisionOnlyStocks,
                date,
                cancellationToken
            );
            await _partitionTracker.MarkImported(
                Dataset,
                scopeKey,
                date,
                importedAt,
                cancellationToken
            );

            _logger.LogInformation(
                "Imported {Count} short volume records for {Date}",
                totalImported,
                date
            );
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug(
                "FINRA short-volume files for {Date} are not published yet; leaving the partition retryable",
                date
            );
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch short volume for {Date}, skipping", date);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing short volume for {Date}", date);
            await _errorReporter.Report(
                ErrorSource.FinraScraper,
                "ShortVolume.ImportDate",
                ex,
                $"date: {date}"
            );
            return false;
        }
    }

    private async Task<int> UpsertDay(
        IEnumerable<DailyShortVolume> volumes,
        IReadOnlyDictionary<Guid, string> collisionOnlyListings,
        DateOnly date,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var listingRepo = scope.ServiceProvider.GetRequiredService<EquityListingRepository>();
        var repo = scope.ServiceProvider.GetRequiredService<DailyShortVolumeRepository>();

        var batch = volumes.ToList();
        var listingIds = batch.Select(row => row.EquityListingId).Distinct().ToList();
        var retainedIds = (
            await listingRepo
                .GetAll()
                .Where(row => listingIds.Contains(row.Id))
                .Select(row => row.Id)
                .ToListAsync(cancellationToken)
        ).ToHashSet();
        var validBatch = batch.Where(row => retainedIds.Contains(row.EquityListingId)).ToList();
        LogDroppedRows(batch.Count - validBatch.Count, date);

        var existing = await repo.GetByDate(date)
            .ToDictionaryAsync(volume => volume.EquityListingId, cancellationToken);
        // A current symbol map cannot prove another historical source spelling was the same instrument.
        // Validate the whole batch before changing tracked observations or marking the partition complete.
        foreach (var volume in validBatch)
            if (
                existing.TryGetValue(volume.EquityListingId, out var stored)
                && !string.Equals(
                    stored.ListedTicker,
                    volume.ListedTicker,
                    StringComparison.Ordinal
                )
            )
                throw new InvalidOperationException(
                    $"FINRA source attribution disagrees for listing {volume.EquityListingId}; original observations were retained."
                );
        foreach (var volume in validBatch)
            UpsertVolume(repo, existing, volume);

        var stale = existing
            .Values.Where(volume =>
                collisionOnlyListings.TryGetValue(volume.EquityListingId, out var sourceTicker)
                && string.Equals(volume.ListedTicker, sourceTicker, StringComparison.Ordinal)
            )
            .ToList();
        if (stale.Count > 0)
        {
            repo.Delete(stale);
            _logger.LogInformation(
                "Deleted {Count} case-fold-only short volume rows for {Date}",
                stale.Count,
                date
            );
        }

        await repo.SaveChanges();
        return validBatch.Count;
    }

    // Only repair observations attributed to the same source ticker as the current claim.
    // Stable listing IDs survive renames; an old ticker's legitimate history must survive too.
    private static Dictionary<Guid, string> CollisionOnlyStocks(
        List<ShortVolumeRecord> records,
        IReadOnlyDictionary<string, EquityListingReference> tickerMap,
        IReadOnlyDictionary<Guid, DailyShortVolume> aggregated
    )
    {
        var fileSymbolsOrdinal = new HashSet<string>(StringComparer.Ordinal);
        var fileSymbolsCaseInsensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (string.IsNullOrEmpty(record.Symbol))
                continue;
            fileSymbolsOrdinal.Add(record.Symbol);
            fileSymbolsCaseInsensitive.Add(record.Symbol);
        }

        var collisionOnly = new Dictionary<Guid, string>();
        foreach (var (ticker, listing) in tickerMap)
        {
            if (aggregated.ContainsKey(listing.EquityListingId))
                continue;
            // Deletion is unrecoverable, so it is confined to the population the case-fold
            // could actually corrupt: all-uppercase tickers (every stored ticker today). A
            // mixed-case ticker would permanently miss the ordinal map, and flagging it here
            // would delete its rows every day the file mentions its symbol.
            if (ticker.Any(char.IsLower))
                continue;
            // The ordinal-absence clause is redundant TODAY (a symbol equal to the ticker
            // would have aggregated, so the ContainsKey guard already skipped this stock) but
            // deliberately kept load-bearing: the moment AggregateVolumesByStock learns to
            // filter records (e.g. dropping zero-volume rows), a filtered-but-present exact
            // symbol must still protect the stock from deletion.
            if (fileSymbolsCaseInsensitive.Contains(ticker) && !fileSymbolsOrdinal.Contains(ticker))
                collisionOnly[listing.EquityListingId] = listing.ListedTicker;
        }

        return collisionOnly;
    }

    private void LogDroppedRows(int dropped, DateOnly date)
    {
        if (dropped == 0)
            return;

        _logger.LogWarning(
            "Dropped {Dropped} short volume rows for {Date} referencing listing IDs no longer in the database",
            dropped,
            date
        );
    }

    private static void UpsertVolume(
        DailyShortVolumeRepository repository,
        IReadOnlyDictionary<Guid, DailyShortVolume> existing,
        DailyShortVolume volume
    )
    {
        var key = volume.EquityListingId;
        if (!existing.TryGetValue(key, out var current))
        {
            repository.Add(volume);
            return;
        }

        current.ShortVolume = volume.ShortVolume;
        current.ShortExemptVolume = volume.ShortExemptVolume;
        current.TotalVolume = volume.TotalVolume;
        current.Market = volume.Market;
    }

    private static Dictionary<Guid, DailyShortVolume> AggregateVolumesByStock(
        List<ShortVolumeRecord> records,
        IReadOnlyDictionary<string, EquityListingReference> tickerMap,
        DateOnly currentDate
    )
    {
        var aggregated = new Dictionary<Guid, DailyShortVolume>();
        foreach (var record in records)
        {
            if (
                string.IsNullOrEmpty(record.Symbol)
                || !tickerMap.TryGetValue(record.Symbol, out var listing)
            )
            {
                continue;
            }

            if (!aggregated.TryGetValue(listing.EquityListingId, out var volume))
            {
                volume = new DailyShortVolume
                {
                    EquityListingId = listing.EquityListingId,
                    ListedTicker = listing.ListedTicker,
                    Date = currentDate,
                };
                aggregated[listing.EquityListingId] = volume;
            }

            volume.ShortVolume += record.ShortVolume ?? 0;
            volume.ShortExemptVolume += record.ShortExemptVolume ?? 0;
            volume.TotalVolume += record.TotalVolume ?? 0;
            volume.Market = MergeMarketCodes(volume.Market, record.MarketCode);
        }

        return aggregated;
    }

    private static string MergeMarketCodes(string current, string additional)
    {
        var markets = new[] { current, additional }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal);
        var merged = string.Join(',', markets);
        return merged.Length == 0 ? null : merged;
    }
}
