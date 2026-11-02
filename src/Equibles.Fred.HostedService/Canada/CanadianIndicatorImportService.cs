using Equibles.Core.AutoWiring;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.Fred.HostedService.Services;
using Equibles.Fred.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Equibles.Fred.HostedService.Canada;

// Imports the curated series the Bank of Canada and Statistics Canada publish. Unlike FRED's
// append-only lane, each run re-reads a trailing window and rewrites any value the publisher
// revised, since Statistics Canada restates recent GDP and labour figures.
[Service]
public class CanadianIndicatorImportService : IImporter
{
    public const string ClientName = "CanadianIndicators";

    // Wide enough for the Labour Force Survey's yearly revision and GDP's rolling restatements.
    private const int RevisionWindowDays = 400;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CanadianIndicatorImportService> _logger;
    private readonly WorkerOptions _workerOptions;
    private readonly ErrorReporter _errorReporter;

    public CanadianIndicatorImportService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        ILogger<CanadianIndicatorImportService> logger,
        IOptions<WorkerOptions> workerOptions,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _logger = logger;
        _workerOptions = workerOptions.Value;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        foreach (
            var curated in CuratedSeriesRegistry.Series.Where(s => s.Source != IndicatorSource.Fred)
        )
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
                    "Failed to fetch Canadian series {SeriesId}, skipping",
                    curated.SeriesId
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Error importing Canadian series {SeriesId}",
                    curated.SeriesId
                );
                await _errorReporter.Report(
                    ErrorSource.FredScraper,
                    "CanadianIndicatorImport.ImportSeries",
                    ex,
                    $"seriesId: {curated.SeriesId}"
                );
            }
        }
    }

    private async Task ImportSeries(CuratedSeries curated, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();
        var observationRepo = scope.ServiceProvider.GetRequiredService<FredObservationRepository>();
        var series = await seriesRepo
            .GetBySeriesId(curated.SeriesId)
            .FirstOrDefaultAsync(cancellationToken);
        if (series == null)
        {
            series = new FredSeries { SeriesId = curated.SeriesId };
            seriesRepo.Add(series);
        }
        // The registry owns a non-FRED series' description, so an edited title or page reaches the stored row.
        series.Title = curated.Title;
        series.Category = curated.Category;
        series.CountryCode = curated.CountryCode;
        series.Source = curated.Source;
        series.SourceKey = curated.SourceKey;
        series.SourceUrl = curated.SourceUrl;
        series.Frequency = curated.Frequency;
        series.Units = curated.Units;
        series.SeasonalAdjustment = curated.SeasonalAdjustment;

        var latest =
            series.ObservationEnd == null
                ? default
                : await observationRepo
                    .GetLatestDate(series)
                    .FirstOrDefaultAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from =
            latest == default
                ? SyncDateResolver.Resolve(default, _workerOptions)
                : latest.AddDays(-RevisionWindowDays);
        var points = await Read(curated, from, today, cancellationToken);

        var stored =
            series.ObservationEnd == null
                ? new Dictionary<DateOnly, FredObservation>()
                : await observationRepo
                    .GetBySeries(series, from, today.AddYears(1))
                    .ToDictionaryAsync(o => o.Date, cancellationToken);
        int added = 0,
            revised = 0;
        foreach (var point in points)
        {
            if (stored.TryGetValue(point.Date, out var observation))
            {
                if (observation.Value == point.Value)
                    continue;
                observation.Value = point.Value;
                revised++;
                continue;
            }
            observationRepo.Add(
                new FredObservation
                {
                    FredSeries = series,
                    FredSeriesId = series.Id,
                    Date = point.Date,
                    Value = point.Value,
                }
            );
            added++;
        }
        if (points.Count > 0)
        {
            var first = points.Min(p => p.Date);
            var last = points.Max(p => p.Date);
            series.ObservationStart =
                series.ObservationStart == null || first < series.ObservationStart
                    ? first
                    : series.ObservationStart;
            series.ObservationEnd =
                series.ObservationEnd == null || last > series.ObservationEnd
                    ? last
                    : series.ObservationEnd;
        }
        series.LastUpdated = DateTime.UtcNow;
        await seriesRepo.SaveChanges();
        _logger.LogInformation(
            "Canadian series {SeriesId}: {Added} added, {Revised} revised from {From}",
            curated.SeriesId,
            added,
            revised,
            from
        );
    }

    private async Task<IReadOnlyList<IndicatorPoint>> Read(
        CuratedSeries curated,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken
    )
    {
        var client = _httpFactory.CreateClient(ClientName);
        return curated.Source switch
        {
            IndicatorSource.BankOfCanada => CanadianIndicatorSources.ParseValet(
                await Get(
                    client,
                    CanadianIndicatorSources.ValetAddress(curated.SourceKey, from),
                    cancellationToken
                ),
                curated.SourceKey
            ),
            IndicatorSource.StatisticsCanada => CanadianIndicatorSources.ParseStatCan(
                await Get(
                    client,
                    CanadianIndicatorSources.StatCanAddress(curated.SourceKey, from, to),
                    cancellationToken
                ),
                curated.SourceKey
            ),
            _ => throw new InvalidOperationException(
                $"{curated.SeriesId} is not a Canadian publisher's series."
            ),
        };
    }

    private static async Task<string> Get(
        HttpClient client,
        Uri address,
        CancellationToken cancellationToken
    )
    {
        using var response = await client.GetAsync(address, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
