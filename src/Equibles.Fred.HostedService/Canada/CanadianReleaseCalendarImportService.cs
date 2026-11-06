using Equibles.Core.AutoWiring;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Equibles.Fred.HostedService.Canada;

// Reads the Canadian release calendar: Statistics Canada's major releases and the Bank of Canada's
// rate decisions. A publisher can move a date, so each read replaces that publisher's upcoming dates;
// one that cannot be read, or changed shape, keeps what is stored.
[Service]
public class CanadianReleaseCalendarImportService : IImporter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CanadianReleaseCalendarImportService> _logger;
    private readonly ErrorReporter _errorReporter;

    public CanadianReleaseCalendarImportService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        ILogger<CanadianReleaseCalendarImportService> logger,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _logger = logger;
        _errorReporter = errorReporter;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await ImportPublisher(
            IndicatorSource.StatisticsCanada,
            CanadianReleaseCalendarSources.StatCanSchedule,
            CanadianReleaseCalendarSources.StatCanPage,
            body => CanadianReleaseCalendarSources.ParseStatCan(body, today),
            today,
            cancellationToken
        );
        await ImportPublisher(
            IndicatorSource.BankOfCanada,
            CanadianReleaseCalendarSources.BankOfCanadaPage,
            CanadianReleaseCalendarSources.BankOfCanadaPage,
            body => CanadianReleaseCalendarSources.ParseBankOfCanada(body, today),
            today,
            cancellationToken
        );
    }

    private async Task ImportPublisher(
        IndicatorSource source,
        string address,
        string link,
        Func<string, IReadOnlyList<CanadianReleaseDate>> parse,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var client = _httpFactory.CreateClient(CanadianIndicatorImportService.ClientName);
            var body = await client.GetStringAsync(address, cancellationToken);
            await Store(source, link, parse(body), today, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to fetch the {Source} release calendar, skipping",
                source
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Error importing the {Source} release calendar", source);
            await _errorReporter.Report(
                ErrorSource.FredScraper,
                "CanadianReleaseCalendarImport.ImportPublisher",
                exception,
                $"source: {source}"
            );
        }
    }

    private async Task Store(
        IndicatorSource source,
        string link,
        IReadOnlyList<CanadianReleaseDate> dates,
        DateOnly today,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var releaseRepo = scope.ServiceProvider.GetRequiredService<FredReleaseRepository>();
        var dateRepo = scope.ServiceProvider.GetRequiredService<FredReleaseDateRepository>();
        var seriesRepo = scope.ServiceProvider.GetRequiredService<FredSeriesRepository>();

        var releases = await releaseRepo
            .GetAll()
            .Where(release => release.Source == source)
            .ToDictionaryAsync(release => release.SourceKey, cancellationToken);
        var ids = releases.Values.Select(release => release.Id).ToList();
        var stored = await dateRepo
            .GetAll()
            .Where(date => ids.Contains(date.FredReleaseId) && date.Date >= today)
            .ToListAsync(cancellationToken);
        // A source that lost a whole year still reads cleanly, and these dates replace the stored ones, so a read must
        // state every year it would replace. Last year's dates are not upcoming, so the new year passes this.
        var yearsRead = dates.Select(date => date.Date.Year).ToHashSet();
        if (stored.FirstOrDefault(date => !yearsRead.Contains(date.Date.Year)) is { } unstated)
            throw new InvalidDataException(
                $"The {source} release calendar states no {unstated.Date.Year} dates, though some are stored."
            );

        foreach (var name in dates.Select(date => date.Name).Distinct())
        {
            if (!releases.TryGetValue(name, out var release))
            {
                release = releaseRepo.Add(
                    new FredRelease
                    {
                        CountryCode = "CA",
                        Source = source,
                        SourceKey = name,
                    }
                );
                releases[name] = release;
            }
            release.Name = name;
            release.Link = link;
            release.Importance = CanadianReleaseCalendarSources.Importance(name);
        }
        await releaseRepo.SaveChanges();

        var read = dates.ToDictionary(date => (releases[date.Name].Id, date.Date));
        foreach (var date in stored)
        {
            if (read.Remove((date.FredReleaseId, date.Date), out var kept))
                date.ReferencePeriod = kept.ReferencePeriod;
            else
                dateRepo.Delete(date);
        }
        dateRepo.AddRange(
            read.Select(entry => new FredReleaseDate
            {
                FredReleaseId = entry.Key.Id,
                Date = entry.Key.Date,
                ReferencePeriod = entry.Value.ReferencePeriod,
            })
        );

        var linked = CanadianReleaseCalendarSources.ReleaseOfSeries;
        var seriesIds = linked.Keys.ToList();
        var series = await seriesRepo
            .GetAll()
            .Where(row => row.Source == source && seriesIds.Contains(row.SeriesId))
            .ToListAsync(cancellationToken);
        foreach (var row in series)
        {
            if (releases.TryGetValue(linked[row.SeriesId], out var release))
                row.FredReleaseId = release.Id;
        }
        await dateRepo.SaveChanges();
        _logger.LogInformation(
            "Imported {Count} {Source} release dates across {Releases} releases",
            dates.Count,
            source,
            releases.Count
        );
    }
}
