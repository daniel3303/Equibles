using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Integrations.Fred.Models;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Fred.HostedService.Services;

[Service]
public class FredImportService
{
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FredImportService> _logger;
    private readonly IFredClient _fredClient;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;

    public FredImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<FredImportService> logger,
        IFredClient fredClient,
        IOptions<WorkerOptions> workerOptions,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _fredClient = fredClient;
        _workerOptions = workerOptions.Value;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        foreach (var curated in CuratedSeriesRegistry.Series)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ImportSeries(curated, cancellationToken);
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
                    ex.Message,
                    ex.StackTrace,
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

        DateOnly startDate;
        using (var scope = _scopeFactory.CreateScope())
        {
            var obsRepo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
            var latestDate = await obsRepo
                .GetLatestDate(series)
                .FirstOrDefaultAsync(cancellationToken);

            startDate = SyncDateResolver.Resolve(latestDate, _workerOptions);

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

        HashSet<DateOnly> existingDates;
        using (var scope = _scopeFactory.CreateScope())
        {
            var obsRepo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
            existingDates = (
                await obsRepo
                    .GetBySeries(series, minApiDate, maxApiDate)
                    .Select(o => o.Date)
                    .ToListAsync(cancellationToken)
            ).ToHashSet();
        }

        _logger.LogDebug(
            "FRED series {SeriesId}: API returned {ApiCount} observations ({MinDate} to {MaxDate}), {ExistingCount} already in DB",
            curated.SeriesId,
            records.Count,
            minApiDate,
            maxApiDate,
            existingDates.Count
        );

        var observations = new List<FredObservation>();
        var skipped = 0;
        var latestObservationDate = DateOnly.MinValue;

        foreach (var (record, date) in parsedRecords)
        {
            if (date > latestObservationDate)
                latestObservationDate = date;

            if (existingDates.Contains(date))
            {
                skipped++;
                continue;
            }

            observations.Add(
                new FredObservation
                {
                    FredSeriesId = series.Id,
                    Date = date,
                    Value = ParseFredValue(record.Value),
                }
            );
        }

        var totalInserted = await BatchPersister.Persist(
            observations,
            InsertBatchSize,
            async batch =>
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
                repo.AddRange(batch);
                await repo.SaveChanges();
            }
        );

        await UpdateSeriesMetadata(series.Id, latestObservationDate);

        if (skipped > 0)
        {
            _logger.LogDebug(
                "FRED series {SeriesId}: skipped {Skipped} existing observations",
                curated.SeriesId,
                skipped
            );
        }

        _logger.LogInformation(
            "Imported {Count} observations for FRED series {SeriesId}",
            totalInserted,
            curated.SeriesId
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
        return DateOnly.TryParse(value, out var date) ? date : null;
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
            if (DateOnly.TryParse(r.Date, out var date))
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
