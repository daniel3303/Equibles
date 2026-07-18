using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories;
using Equibles.Core.AutoWiring;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Integrations.Cboe.Contracts;
using Equibles.Integrations.Cboe.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Cboe.HostedService.Services;

[Service]
public class CboeImportService : IImporter
{
    private const int InsertBatchSize = 1000;

    // CBOE's daily market-statistics page exposes data from 2019-10-07 onwards.
    // Earlier dates cannot be backfilled from the free feed.
    private static readonly DateOnly DailyPageMinDate = new(2019, 10, 7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CboeImportService> _logger;
    private readonly ICboeClient _cboeClient;
    private readonly ErrorReporter _errorReporter;
    private readonly Func<DateOnly> _today;

    public CboeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<CboeImportService> logger,
        ICboeClient cboeClient,
        ErrorReporter errorReporter
    )
        : this(scopeFactory, logger, cboeClient, errorReporter, null) { }

    // Seam for tests: lets a fixture pin "today" so date iteration stays
    // deterministic without freezing the process clock.
    internal CboeImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<CboeImportService> logger,
        ICboeClient cboeClient,
        ErrorReporter errorReporter,
        Func<DateOnly> today
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cboeClient = cboeClient;
        _errorReporter = errorReporter;
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        await ImportAllPutCallRatios(cancellationToken);
        await ImportVixHistory(cancellationToken);
    }

    private async Task ImportAllPutCallRatios(CancellationToken cancellationToken)
    {
        Dictionary<CboePutCallRatioType, DateOnly> latestPerType;
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<CboePutCallRatioRepository>();
            latestPerType = new Dictionary<CboePutCallRatioType, DateOnly>();
            foreach (var ratioType in Enum.GetValues<CboePutCallRatioType>())
            {
                latestPerType[ratioType] = await repo.GetLatestDate(ratioType)
                    .FirstOrDefaultAsync(cancellationToken);
            }
        }

        // Drive the catch-up cursor from the LEAST advanced type so a type that
        // briefly stopped reporting (or a fresh DB) gets backfilled too.
        var earliestKnown = latestPerType.Values.Min();
        var start = earliestKnown == default ? DailyPageMinDate : earliestKnown.AddDays(1);
        var today = _today();
        if (start > today)
        {
            _logger.LogDebug("CBOE put/call ratios are up to date");
            return;
        }

        var pending = new List<CboePutCallRatio>();
        var perTypeInserts = new Dictionary<CboePutCallRatioType, int>();
        var totalInserted = 0;

        // Persist as we go rather than once at the end. A cold-start backfill
        // walks years of trading days at ~10 requests/min, so deferring every
        // write to the end means a worker restart (or cancellation) mid-backfill
        // discards all scraped rows — and the next cycle, seeing an empty table,
        // restarts from DailyPageMinDate, so put/call data would never land.
        // Flushing each full batch makes progress durable and lets the next
        // cycle resume from the last persisted date.
        async Task Flush()
        {
            if (pending.Count == 0)
                return;

            await BatchPersister.Persist<CboePutCallRatio, CboePutCallRatioRepository>(
                pending,
                InsertBatchSize,
                _scopeFactory
            );
            totalInserted += pending.Count;
            pending.Clear();
        }

        for (var date = start; date <= today; date = date.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Skip weekends locally — CBOE returns the page skeleton for them
            // but with no optionsData; avoids ~30% of useless requests.
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                continue;

            Dictionary<CboePutCallProductType, CboePutCallRecord> dailyRecords;
            try
            {
                dailyRecords = await _cboeClient.DownloadDailyPutCallRatios(date);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to download CBOE put/call page for {Date}, skipping",
                    date
                );
                continue;
            }
            catch (Exception ex)
            {
                // Non-HTTP failure (e.g., page-shape change broke the parser).
                // Stop the loop instead of burning the rate-limit budget on
                // every subsequent date that would fail the same way.
                _logger.LogError(
                    ex,
                    "Error parsing CBOE put/call page for {Date}, aborting cycle",
                    date
                );
                await _errorReporter.Report(
                    ErrorSource.CboeScraper,
                    "CboeImport.ImportPutCallRatio",
                    ex,
                    $"date: {date}"
                );
                break;
            }

            StageNewRecords(dailyRecords, latestPerType, pending, perTypeInserts);

            if (pending.Count >= InsertBatchSize)
                await Flush();
        }

        await Flush();

        if (totalInserted == 0)
        {
            _logger.LogDebug("CBOE put/call ratios are up to date");
            return;
        }

        foreach (var (ratioType, count) in perTypeInserts)
        {
            _logger.LogInformation(
                "CBOE {Type} put/call: imported {Count} new records",
                ratioType,
                count
            );
        }
    }

    // Map each product's daily record to a CboePutCallRatio and stage the ones newer than
    // what's already stored, tallying inserts per type. pending/perTypeInserts are mutated in place.
    private static void StageNewRecords(
        Dictionary<CboePutCallProductType, CboePutCallRecord> dailyRecords,
        Dictionary<CboePutCallRatioType, DateOnly> latestPerType,
        List<CboePutCallRatio> pending,
        Dictionary<CboePutCallRatioType, int> perTypeInserts
    )
    {
        foreach (var (productType, record) in dailyRecords)
        {
            var ratioType = MapProduct(productType);
            if (record.Date <= latestPerType[ratioType])
                continue;

            pending.Add(
                new CboePutCallRatio
                {
                    RatioType = ratioType,
                    Date = record.Date,
                    CallVolume = record.CallVolume,
                    PutVolume = record.PutVolume,
                    TotalVolume = record.TotalVolume,
                    PutCallRatio = record.PutCallRatio,
                }
            );
            perTypeInserts.TryGetValue(ratioType, out var n);
            perTypeInserts[ratioType] = n + 1;
        }
    }

    private static CboePutCallRatioType MapProduct(CboePutCallProductType product) =>
        product switch
        {
            CboePutCallProductType.Total => CboePutCallRatioType.Total,
            CboePutCallProductType.Equity => CboePutCallRatioType.Equity,
            CboePutCallProductType.Index => CboePutCallRatioType.Index,
            CboePutCallProductType.Vix => CboePutCallRatioType.Vix,
            CboePutCallProductType.Etp => CboePutCallRatioType.Etp,
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, null),
        };

    private async Task ImportVixHistory(CancellationToken cancellationToken)
    {
        try
        {
            var records = await _cboeClient.DownloadVixHistory();
            _logger.LogDebug("CBOE VIX: downloaded {Count} records", records.Count);

            if (records.Count == 0)
                return;

            DateOnly latestStoredDate;
            using (var scope = _scopeFactory.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<CboeVixDailyRepository>();
                latestStoredDate = await repo.GetLatestDate()
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var newRecords =
                latestStoredDate != default
                    ? records.Where(r => r.Date > latestStoredDate).ToList()
                    : records;

            if (newRecords.Count == 0)
            {
                _logger.LogDebug("CBOE VIX history is up to date");
                return;
            }

            var totalInserted = await BatchPersister.Persist<CboeVixDaily, CboeVixDailyRepository>(
                newRecords.Select(r => new CboeVixDaily
                {
                    Date = r.Date,
                    Open = r.Open,
                    High = r.High,
                    Low = r.Low,
                    Close = r.Close,
                }),
                InsertBatchSize,
                _scopeFactory
            );

            _logger.LogInformation("CBOE VIX: imported {Count} new daily records", totalInserted);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to download CBOE VIX history CSV, skipping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing CBOE VIX history");
            await _errorReporter.Report(ErrorSource.CboeScraper, "CboeImport.ImportVixHistory", ex);
        }
    }
}
