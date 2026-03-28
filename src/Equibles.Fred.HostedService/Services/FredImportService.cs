using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Fred.HostedService.Services;

[Service]
public class FredImportService {
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FredImportService> _logger;
    private readonly IFredClient _fredClient;
    private readonly WorkerOptions _workerOptions;

    public FredImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<FredImportService> logger,
        IFredClient fredClient,
        IOptions<WorkerOptions> workerOptions
    ) {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _fredClient = fredClient;
        _workerOptions = workerOptions.Value;
    }

    public async Task Import(CancellationToken cancellationToken) {
        foreach (var curated in CuratedSeriesRegistry.Series) {
            cancellationToken.ThrowIfCancellationRequested();

            try {
                await ImportSeries(curated, cancellationToken);
            } catch (HttpRequestException ex) {
                _logger.LogWarning(ex, "Failed to fetch FRED series {SeriesId}, skipping", curated.SeriesId);
            } catch (Exception ex) {
                _logger.LogError(ex, "Error importing FRED series {SeriesId}", curated.SeriesId);
                await ReportError("FredImport.ImportSeries", ex.Message, ex.StackTrace, $"seriesId: {curated.SeriesId}");
            }
        }
    }

    private async Task ImportSeries(CuratedSeries curated, CancellationToken cancellationToken) {
        // Ensure series metadata exists in DB
        FredSeries series;
        using (var scope = _scopeFactory.CreateScope()) {
            var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();
            series = await seriesRepo.GetBySeriesId(curated.SeriesId).FirstOrDefaultAsync(cancellationToken);

            if (series == null) {
                var metadata = await _fredClient.GetSeriesMetadata(curated.SeriesId);
                if (metadata == null) {
                    _logger.LogWarning("FRED series {SeriesId} not found in API, skipping", curated.SeriesId);
                    return;
                }

                series = new FredSeries {
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

                _logger.LogInformation("Created FRED series {SeriesId} ({Title})", series.SeriesId, series.Title);
            }
        }

        // Determine start date for observations
        DateOnly startDate;
        using (var scope = _scopeFactory.CreateScope()) {
            var obsRepo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
            var latestDate = await obsRepo.GetLatestDate(series).FirstOrDefaultAsync(cancellationToken);

            var minDate = _workerOptions.MinSyncDate != null
                ? DateOnly.FromDateTime(_workerOptions.MinSyncDate.Value)
                : new DateOnly(2020, 1, 1);

            // Start from the day after the latest observation, or minDate
            startDate = latestDate != default
                ? latestDate.AddDays(1)
                : minDate;

            _logger.LogDebug("FRED series {SeriesId}: latest stored={LatestDate}, startDate={StartDate}",
                curated.SeriesId, latestDate, startDate);
        }

        if (startDate > DateOnly.FromDateTime(DateTime.UtcNow)) {
            _logger.LogDebug("FRED series {SeriesId} is up to date (startDate {StartDate} > today)",
                curated.SeriesId, startDate);
            return;
        }

        // Fetch new observations from FRED API
        var records = await _fredClient.GetObservations(curated.SeriesId, startDate);
        _logger.LogDebug("FRED API returned {Count} observations for {SeriesId} from {StartDate}",
            records.Count, curated.SeriesId, startDate);

        if (records.Count == 0) {
            _logger.LogDebug("No new observations for FRED series {SeriesId}", curated.SeriesId);
            return;
        }

        // Parse all dates from API response to determine the range we need to check
        var apiDates = records
            .Select(r => DateOnly.TryParse(r.Date, out var d) ? d : (DateOnly?)null)
            .Where(d => d.HasValue)
            .Select(d => d.Value)
            .ToList();

        if (apiDates.Count == 0) {
            _logger.LogDebug("No parseable dates in FRED API response for {SeriesId}", curated.SeriesId);
            return;
        }

        var minApiDate = apiDates.Min();
        var maxApiDate = apiDates.Max();

        // Load existing dates that overlap with the API response range
        HashSet<DateOnly> existingDates;
        using (var scope = _scopeFactory.CreateScope()) {
            var obsRepo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
            existingDates = (await obsRepo.GetBySeries(series, minApiDate, maxApiDate)
                .Select(o => o.Date)
                .ToListAsync(cancellationToken)).ToHashSet();
        }

        _logger.LogDebug("FRED series {SeriesId}: API returned {ApiCount} observations ({MinDate} to {MaxDate}), {ExistingCount} already in DB",
            curated.SeriesId, records.Count, minApiDate, maxApiDate, existingDates.Count);

        // Build new observations, skipping any that already exist
        var batch = new List<FredObservation>(InsertBatchSize);
        var totalInserted = 0;
        var skipped = 0;
        var latestObservationDate = DateOnly.MinValue;

        foreach (var record in records) {
            if (!DateOnly.TryParse(record.Date, out var date)) continue;
            if (date > latestObservationDate) latestObservationDate = date;

            if (existingDates.Contains(date)) {
                skipped++;
                continue;
            }

            // FRED returns "." for missing values
            decimal? value = null;
            if (record.Value != "." && decimal.TryParse(record.Value, System.Globalization.CultureInfo.InvariantCulture, out var parsed)) {
                value = parsed;
            }

            batch.Add(new FredObservation {
                FredSeriesId = series.Id,
                Date = date,
                Value = value,
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

        // Update series metadata
        using (var scope = _scopeFactory.CreateScope()) {
            var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();
            var dbSeries = await seriesRepo.Get(series.Id);
            dbSeries.LastUpdated = DateTime.UtcNow;
            if (latestObservationDate != DateOnly.MinValue) {
                dbSeries.ObservationEnd = latestObservationDate;
            }
            await seriesRepo.SaveChanges();
        }

        if (skipped > 0) {
            _logger.LogDebug("FRED series {SeriesId}: skipped {Skipped} existing observations", curated.SeriesId, skipped);
        }

        _logger.LogInformation("Imported {Count} observations for FRED series {SeriesId}",
            totalInserted, curated.SeriesId);
    }

    private async Task FlushBatch(List<FredObservation> items) {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
        repo.AddRange(items);
        await repo.SaveChanges();
    }

    private static DateOnly? ParseDate(string value) {
        return DateOnly.TryParse(value, out var date) ? date : null;
    }

    private async Task ReportError(string context, string message, string stackTrace, string requestSummary = null) {
        try {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var errorManager = scope.ServiceProvider.GetRequiredService<ErrorManager>();
            await errorManager.Create(ErrorSource.FredScraper, context, message, stackTrace, requestSummary);
        } catch { }
    }
}
