using System.Globalization;
using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Configuration;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Integrations.Fred.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Fred.HostedService.Services;

[Service]
public class FredImportService : IImporter
{
    private const int InsertBatchSize = 1000;

    // How far behind the latest stored observation each cycle re-reads for revisions; BEA's
    // annual updates restate up to five years of PCE and GDP.
    public const int RevisionLookbackYears = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FredImportService> _logger;
    private readonly IFredClient _fredClient;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;
    private readonly DateOnly? _historyStart;

    public FredImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<FredImportService> logger,
        IFredClient fredClient,
        IOptions<WorkerOptions> workerOptions,
        ErrorReporter errorReporter,
        IOptions<FredScraperOptions> scraperOptions = null
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _fredClient = fredClient;
        _workerOptions = workerOptions.Value;
        _errorReporter = errorReporter;
        _historyStart = scraperOptions?.Value.HistoryStart is { } historyStart
            ? DateOnly.FromDateTime(historyStart)
            : null;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        foreach (
            var curated in CuratedSeriesRegistry.Series.Where(s => s.Source == IndicatorSource.Fred)
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ImportSeries(curated, cancellationToken);
            }
            catch (FredUnavailableException ex)
            {
                // An outage page says nothing about this series: every remaining one would fail
                // the same way and write its own Errors row. Rethrow so the worker's
                // consecutive-failure streak owns reporting and backoff.
                _logger.LogWarning(
                    "FRED unavailable while importing {SeriesId} ({Reason}); aborting this cycle",
                    curated.SeriesId,
                    ex.Message
                );
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to fetch FRED series {SeriesId}, skipping",
                    curated.SeriesId
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing FRED series {SeriesId}", curated.SeriesId);
                await _errorReporter.Report(
                    ErrorSource.FredScraper,
                    "FredImport.ImportSeries",
                    ex,
                    $"seriesId: {curated.SeriesId}"
                );
            }
        }
    }

    private async Task ImportSeries(CuratedSeries curated, CancellationToken cancellationToken)
    {
        var series = await EnsureSeriesExists(curated, cancellationToken);
        if (series == null)
            return;

        await ImportRecent(curated, series, cancellationToken);
        await BackfillHistory(curated, series, cancellationToken);
    }

    // The incremental import starts a new series at Worker:MinSyncDate, which left no year-ago base for its first
    // year and no long-run history. Each series is filled back once, below its earliest stored observation, to its
    // own first observation (or FredScraper:HistoryStart when that is later).
    private async Task BackfillHistory(
        CuratedSeries curated,
        FredSeries series,
        CancellationToken cancellationToken
    )
    {
        if (series.ObservationStart is not { } seriesStart)
            return;
        var floor = _historyStart is { } configured && configured > seriesStart
            ? configured
            : seriesStart;

        DateOnly earliest;
        using (var scope = _scopeFactory.CreateScope())
        {
            var obsRepo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
            earliest = await obsRepo.GetEarliestDate(series).FirstOrDefaultAsync(cancellationToken);
        }
        if (earliest == default || floor >= earliest)
            return;

        var records = await _fredClient.GetObservations(
            curated.SeriesId,
            floor,
            earliest.AddDays(-1)
        );
        // Saved newest first, batch by batch: a fill cut short leaves one block ending at the stored start, so the
        // next cycle resumes below it. Oldest first would store the series' first date and hide the gap for good.
        var older = ParseObservationDates(records)
            .Where(parsed => parsed.Date >= floor && parsed.Date < earliest)
            .GroupBy(parsed => parsed.Date)
            .Select(group => group.First())
            .OrderByDescending(parsed => parsed.Date)
            .ToList();
        if (older.Count == 0)
        {
            // FRED serves nothing before the stored start (a licensed series keeps a rolling window): the stored
            // start becomes the series' own, so the next cycle does not ask again.
            if (floor == seriesStart)
                await SetObservationStart(series.Id, earliest);
            return;
        }

        var inserted = await BatchPersister.Persist<FredObservation, FredObservationRepository>(
            older
                .Select(parsed => new FredObservation
                {
                    FredSeriesId = series.Id,
                    Date = parsed.Date,
                    Value = ParseFredValue(parsed.Record.Value),
                })
                .ToList(),
            InsertBatchSize,
            _scopeFactory
        );
        _logger.LogInformation(
            "Backfilled {Count} observations for FRED series {SeriesId} from {From} to {To}",
            inserted,
            curated.SeriesId,
            older[^1].Date,
            older[0].Date
        );
    }

    private async Task SetObservationStart(Guid seriesId, DateOnly observationStart)
    {
        using var scope = _scopeFactory.CreateScope();
        var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();
        var dbSeries = await seriesRepo.Get(seriesId);
        dbSeries.ObservationStart = observationStart;
        await seriesRepo.SaveChanges();
    }

    private async Task ImportRecent(
        CuratedSeries curated,
        FredSeries series,
        CancellationToken cancellationToken
    )
    {
        DateOnly startDate;
        using (var scope = _scopeFactory.CreateScope())
        {
            var obsRepo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
            var latestDate = await obsRepo
                .GetLatestDate(series)
                .FirstOrDefaultAsync(cancellationToken);

            startDate = SyncDateResolver.Resolve(latestDate, _workerOptions);
            // FRED revises history: a monthly PCE release restates the preceding months and the
            // annual update reaches back years, so a stored series is re-read over a trailing
            // window and changed values rewritten. Appending from the latest date alone froze the
            // first vintage of every observation.
            if (latestDate != default && latestDate != DateOnly.MaxValue)
            {
                var revisionWindowStart = latestDate.AddYears(-RevisionLookbackYears);
                if (revisionWindowStart < startDate)
                    startDate = revisionWindowStart;
            }

            _logger.LogDebug(
                "FRED series {SeriesId}: latest stored={LatestDate}, startDate={StartDate}",
                curated.SeriesId,
                latestDate,
                startDate
            );
        }

        if (startDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            _logger.LogDebug(
                "FRED series {SeriesId} is up to date (startDate {StartDate} > today)",
                curated.SeriesId,
                startDate
            );
            return;
        }

        var records = await _fredClient.GetObservations(curated.SeriesId, startDate);
        _logger.LogDebug(
            "FRED API returned {Count} observations for {SeriesId} from {StartDate}",
            records.Count,
            curated.SeriesId,
            startDate
        );

        if (records.Count == 0)
        {
            _logger.LogDebug("No new observations for FRED series {SeriesId}", curated.SeriesId);
            return;
        }

        var parsedRecords = ParseObservationDates(records);

        if (parsedRecords.Count == 0)
        {
            _logger.LogDebug(
                "No parseable dates in FRED API response for {SeriesId}",
                curated.SeriesId
            );
            return;
        }

        var minApiDate = parsedRecords.Min(p => p.Date);
        var maxApiDate = parsedRecords.Max(p => p.Date);

        var observations = new List<FredObservation>();
        var skipped = 0;
        var revised = 0;
        var latestObservationDate = DateOnly.MinValue;

        using (var scope = _scopeFactory.CreateScope())
        {
            var obsRepo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
            var existing = (
                await obsRepo
                    .GetBySeries(series, minApiDate, maxApiDate)
                    .ToListAsync(cancellationToken)
            )
                .GroupBy(o => o.Date)
                .ToDictionary(g => g.Key, g => g.First());

            _logger.LogDebug(
                "FRED series {SeriesId}: API returned {ApiCount} observations ({MinDate} to {MaxDate}), {ExistingCount} already in DB",
                curated.SeriesId,
                records.Count,
                minApiDate,
                maxApiDate,
                existing.Count
            );

            foreach (var (record, date) in parsedRecords)
            {
                if (date > latestObservationDate)
                    latestObservationDate = date;

                var value = ParseFredValue(record.Value);
                if (existing.TryGetValue(date, out var stored))
                {
                    if (stored.Value == value)
                    {
                        skipped++;
                        continue;
                    }

                    stored.Value = value;
                    revised++;
                    continue;
                }

                observations.Add(
                    new FredObservation
                    {
                        FredSeriesId = series.Id,
                        Date = date,
                        Value = value,
                    }
                );
            }

            if (revised > 0)
                await obsRepo.SaveChanges();
        }

        var totalInserted = await BatchPersister.Persist<
            FredObservation,
            FredObservationRepository
        >(observations, InsertBatchSize, _scopeFactory);

        await UpdateSeriesMetadata(series.Id, latestObservationDate);

        if (skipped > 0)
        {
            _logger.LogDebug(
                "FRED series {SeriesId}: skipped {Skipped} unchanged observations",
                curated.SeriesId,
                skipped
            );
        }

        _logger.LogInformation(
            "Imported {Count} observations for FRED series {SeriesId} ({Revised} revised)",
            totalInserted,
            curated.SeriesId,
            revised
        );
    }

    private async Task UpdateSeriesMetadata(Guid seriesId, DateOnly latestObservationDate)
    {
        using var scope = _scopeFactory.CreateScope();
        var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();
        var dbSeries = await seriesRepo.Get(seriesId);
        dbSeries.LastUpdated = DateTime.UtcNow;
        if (latestObservationDate != DateOnly.MinValue)
        {
            dbSeries.ObservationEnd = latestObservationDate;
        }
        await seriesRepo.SaveChanges();
    }

    private async Task<FredSeries> EnsureSeriesExists(
        CuratedSeries curated,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();
        var series = await seriesRepo
            .GetBySeriesId(curated.SeriesId)
            .FirstOrDefaultAsync(cancellationToken);

        if (series != null)
            return series;

        var metadata = await _fredClient.GetSeriesMetadata(curated.SeriesId);
        if (metadata == null)
        {
            _logger.LogWarning(
                "FRED series {SeriesId} not found in API, skipping",
                curated.SeriesId
            );
            return null;
        }

        series = new FredSeries
        {
            SeriesId = metadata.Id,
            Title = metadata.Title,
            Category = curated.Category,
            CountryCode = curated.CountryCode,
            Source = IndicatorSource.Fred,
            Frequency = metadata.FrequencyShort,
            Units = metadata.Units,
            SeasonalAdjustment = metadata.SeasonalAdjustmentShort,
            ObservationStart = ParseDate(metadata.ObservationStart),
            ObservationEnd = ParseDate(metadata.ObservationEnd),
        };

        seriesRepo.Add(series);
        await seriesRepo.SaveChanges();

        _logger.LogInformation(
            "Created FRED series {SeriesId} ({Title})",
            series.SeriesId,
            series.Title
        );

        return series;
    }

    private static DateOnly? ParseDate(string value)
    {
        return DateOnly.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date
        )
            ? date
            : null;
    }

    // Parse each FRED record's date once so callers can reuse the (record, date)
    // pairs for both the range computation and the dedup/insert loop without
    // re-parsing per use. Records with unparseable Date strings are dropped.
    private static List<(FredObservationRecord Record, DateOnly Date)> ParseObservationDates(
        List<FredObservationRecord> records
    )
    {
        var result = new List<(FredObservationRecord, DateOnly)>(records.Count);
        foreach (var r in records)
        {
            if (
                DateOnly.TryParse(
                    r.Date,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
            )
                result.Add((r, date));
        }
        return result;
    }

    // FRED uses the literal "." as its missing-observation sentinel.
    private static decimal? ParseFredValue(string raw)
    {
        if (raw == ".")
            return null;
        return decimal.TryParse(
            raw,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;
    }
}
