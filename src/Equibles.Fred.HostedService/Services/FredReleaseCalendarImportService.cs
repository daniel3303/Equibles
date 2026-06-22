using System.Globalization;
using Equibles.Core.AutoWiring;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Integrations.Fred.Contracts;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Fred.HostedService.Services;

/// <summary>
/// Imports the FRED release calendar: links each stored series to its parent
/// release (/fred/series/release) and upserts the scheduled and realized
/// publication dates of every tracked release (/fred/releases/dates). Runs
/// after the observation import in the same scraper cycle.
/// </summary>
[Service]
public class FredReleaseCalendarImportService : IImporter
{
    private const int InsertBatchSize = 1000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FredReleaseCalendarImportService> _logger;
    private readonly IFredClient _fredClient;
    private readonly ErrorReporter _errorReporter;

    public FredReleaseCalendarImportService(
        IServiceScopeFactory scopeFactory,
        ILogger<FredReleaseCalendarImportService> logger,
        IFredClient fredClient,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _fredClient = fredClient;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        try
        {
            await LinkSeriesToReleases(cancellationToken);
            await ImportReleaseDates(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch the FRED release calendar, skipping cycle");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing the FRED release calendar");
            await _errorReporter.Report(
                ErrorSource.FredScraper,
                "FredReleaseCalendarImport.Import",
                ex.Message,
                ex.StackTrace
            );
        }
    }

    private async Task LinkSeriesToReleases(CancellationToken cancellationToken)
    {
        List<FredSeries> unlinkedSeries;
        using (var scope = _scopeFactory.CreateScope())
        {
            var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();
            unlinkedSeries = await seriesRepo
                .GetAll()
                .Where(s => s.FredReleaseId == null)
                .ToListAsync(cancellationToken);
        }

        foreach (var series in unlinkedSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var record = await _fredClient.GetSeriesRelease(series.SeriesId);
            if (record == null)
            {
                _logger.LogWarning(
                    "FRED series {SeriesId} has no release in the API, skipping link",
                    series.SeriesId
                );
                continue;
            }

            using var scope = _scopeFactory.CreateScope();
            var releaseRepo = scope.ServiceProvider.GetRequiredService<FredReleaseRepository>();
            var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();

            var release = await releaseRepo
                .GetByReleaseId(record.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (release == null)
            {
                release = new FredRelease
                {
                    ReleaseId = record.Id,
                    Name = record.Name,
                    Link = record.Link,
                    PressRelease = record.PressRelease,
                };
                releaseRepo.Add(release);
                await releaseRepo.SaveChanges();

                _logger.LogInformation(
                    "Created FRED release {ReleaseId} ({Name})",
                    release.ReleaseId,
                    release.Name
                );
            }

            var dbSeries = await seriesRepo.Get(series.Id);
            dbSeries.FredReleaseId = release.Id;
            await seriesRepo.SaveChanges();

            _logger.LogDebug(
                "Linked FRED series {SeriesId} to release {ReleaseId} ({Name})",
                series.SeriesId,
                release.ReleaseId,
                release.Name
            );
        }
    }

    private async Task ImportReleaseDates(CancellationToken cancellationToken)
    {
        Dictionary<int, Guid> trackedReleases;
        using (var scope = _scopeFactory.CreateScope())
        {
            var releaseRepo = scope.ServiceProvider.GetRequiredService<FredReleaseRepository>();
            trackedReleases = await releaseRepo
                .GetAll()
                .ToDictionaryAsync(r => r.ReleaseId, r => r.Id, cancellationToken);
        }

        if (trackedReleases.Count == 0)
        {
            _logger.LogDebug("No FRED releases tracked yet, skipping release-date import");
            return;
        }

        var records = await _fredClient.GetReleaseDates();

        // The endpoint returns dates for every FRED release; keep only the ones
        // belonging to releases our series actually link to.
        var parsed = new List<(Guid ReleaseId, DateOnly Date)>();
        foreach (var record in records)
        {
            if (!trackedReleases.TryGetValue(record.ReleaseId, out var releaseId))
                continue;
            if (
                !DateOnly.TryParse(
                    record.Date,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date
                )
            )
                continue;
            parsed.Add((releaseId, date));
        }

        if (parsed.Count == 0)
        {
            _logger.LogDebug("No FRED release dates for tracked releases");
            return;
        }

        // Drop releases whose value is merely carried forward every calendar day
        // rather than published on scheduled announcement dates. With
        // include_release_dates_with_no_data=true, FRED fills a "release date" for
        // EVERY day — including weekends — for releases backed by a continuously
        // carried-forward daily rate level (e.g. the FOMC Press Release driven by
        // the DFEDTARL/DFEDTARU target range). The daily level update is not an
        // announcement event, so projecting it onto the calendar produced a phantom
        // "FOMC Press Release" entry on every single day (the only row on weekends).
        //
        // The defensible, data-only discriminator: no genuine statistical release
        // (CPI, Employment Situation, GDP) — and no genuine business-day daily print
        // (EFFR, SOFR, VIXCLS) — is ever published on a Saturday or Sunday. A release
        // that FRED reports on any weekend day is therefore a 7-day carry-forward, not
        // an announcement. We never model the 8 real FOMC meeting dates from this feed,
        // so suppressing such a release entirely is the conservative correct outcome.
        var carriedForwardReleases = parsed
            .Where(p => p.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            .Select(p => p.ReleaseId)
            .ToHashSet();

        if (carriedForwardReleases.Count > 0)
        {
            var dropped = parsed.RemoveAll(p => carriedForwardReleases.Contains(p.ReleaseId));
            _logger.LogDebug(
                "Skipped {Count} carry-forward release dates across {Releases} continuously-updated releases",
                dropped,
                carriedForwardReleases.Count
            );

            if (parsed.Count == 0)
            {
                _logger.LogDebug(
                    "No scheduled FRED release dates after dropping carry-forward fills"
                );
                return;
            }
        }

        var minDate = parsed.Min(p => p.Date);
        var maxDate = parsed.Max(p => p.Date);

        HashSet<(Guid, DateOnly)> existing;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dateRepo = scope.ServiceProvider.GetRequiredService<FredReleaseDateRepository>();
            existing = (
                await dateRepo
                    .GetInRange(minDate, maxDate)
                    .Select(d => new { d.FredReleaseId, d.Date })
                    .ToListAsync(cancellationToken)
            )
                .Select(d => (d.FredReleaseId, d.Date))
                .ToHashSet();
        }

        var newDates = parsed
            .Where(p => !existing.Contains((p.ReleaseId, p.Date)))
            .Distinct()
            .Select(p => new FredReleaseDate { FredReleaseId = p.ReleaseId, Date = p.Date })
            .ToList();

        var totalInserted = await BatchPersister.Persist<
            FredReleaseDate,
            FredReleaseDateRepository
        >(newDates, InsertBatchSize, _scopeFactory);

        _logger.LogInformation(
            "Imported {Count} FRED release dates across {Releases} tracked releases",
            totalInserted,
            trackedReleases.Count
        );
    }
}
