using Equibles.CommonStocks.HostedService.Services;
using Equibles.Core.AutoWiring;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.FdaCatalysts.BusinessLogic;
using Equibles.FdaCatalysts.Data.Models;
using Equibles.FdaCatalysts.HostedService.Configuration;
using Equibles.FdaCatalysts.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;

namespace Equibles.FdaCatalysts.HostedService.Services;

/// <summary>
/// Imports FDA advisory-committee meetings from the FDA.gov advisory-committee
/// calendar. The calendar table is rendered client-side, so the page is fetched
/// through the headless stealth browser; the rendered HTML is then handed to the
/// authoritative-column parser and the resulting rows are upserted by their stable
/// per-meeting slug (<see cref="FdaCatalyst.SourceReference"/>). The whole calendar is
/// re-read every cycle and reconciled — there is no watermark, because the calendar
/// only carries a short forward window and meetings can be rescheduled in place.
/// </summary>
[Service]
public class FdaAdvisoryCommitteeCalendarImportService : IImporter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IStealthBrowserClient _stealthBrowser;
    private readonly ErrorReporter _errorReporter;
    private readonly ILogger<FdaAdvisoryCommitteeCalendarImportService> _logger;
    private readonly FdaCatalystScraperOptions _options;

    public FdaAdvisoryCommitteeCalendarImportService(
        IServiceScopeFactory scopeFactory,
        IStealthBrowserClient stealthBrowser,
        ErrorReporter errorReporter,
        ILogger<FdaAdvisoryCommitteeCalendarImportService> logger,
        IOptions<FdaCatalystScraperOptions> options
    )
    {
        _scopeFactory = scopeFactory;
        _stealthBrowser = stealthBrowser;
        _errorReporter = errorReporter;
        _logger = logger;
        _options = options.Value;
    }

    public async Task Import(CancellationToken cancellationToken)
    {
        if (!_stealthBrowser.IsEnabled)
        {
            // The calendar is client-rendered, so without a stealth/render engine there
            // is no way to read its rows — degrade to a no-op rather than scraping chrome.
            _logger.LogWarning(
                "FDA advisory-committee import skipped: no stealth browser configured "
                    + "(no IStealthBrowserClient engine is registered)."
            );
            return;
        }

        var html = await _stealthBrowser.FetchHtml(_options.CalendarUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
        {
            _logger.LogWarning(
                "FDA advisory-committee calendar render returned no HTML from {Url}",
                _options.CalendarUrl
            );
            return;
        }

        var parsed = FdaAdvisoryCommitteeCalendarParser.Parse(html);
        if (parsed.Count == 0)
        {
            // A render that yields zero rows is more likely a markup change than a genuinely
            // empty calendar, so surface it as an error rather than a silent success.
            await _errorReporter.Report(
                ErrorSource.FdaCatalystScraper,
                "FdaAdvisoryCommitteeCalendarImportService.Import",
                $"Parsed zero rows from the FDA advisory-committee calendar at {_options.CalendarUrl}",
                null
            );
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<FdaCatalystRepository>();

        var slugs = parsed.Select(c => c.SourceReference).ToList();
        var existing = await repository
            .GetAll()
            .Where(c => slugs.Contains(c.SourceReference))
            .ToListAsync(cancellationToken);
        var bySlug = existing.ToDictionary(c => c.SourceReference);

        var inserted = 0;
        var updated = 0;
        foreach (var catalyst in parsed)
        {
            if (bySlug.TryGetValue(catalyst.SourceReference, out var row))
            {
                if (Apply(catalyst, row))
                {
                    repository.Update(row);
                    updated++;
                }
            }
            else
            {
                repository.Add(catalyst);
                inserted++;
            }
        }

        await repository.SaveChanges();

        _logger.LogInformation(
            "FDA advisory-committee import complete: {Inserted} new, {Updated} updated, "
                + "{Total} on the calendar.",
            inserted,
            updated,
            parsed.Count
        );
    }

    /// <summary>
    /// Refreshes the mutable, calendar-sourced fields of an existing catalyst from a
    /// freshly parsed row, preserving the stored <c>Id</c>, <c>CreationTime</c>, and any
    /// resolved <c>CommonStockId</c>. Returns true only when something actually changed,
    /// so a row is marked dirty only on a real update.
    /// </summary>
    private static bool Apply(FdaCatalyst source, FdaCatalyst target)
    {
        var changed = false;
        if (target.CatalystType != source.CatalystType)
        {
            target.CatalystType = source.CatalystType;
            changed = true;
        }
        if (target.MeetingDate != source.MeetingDate)
        {
            target.MeetingDate = source.MeetingDate;
            changed = true;
        }
        if (target.EndDate != source.EndDate)
        {
            target.EndDate = source.EndDate;
            changed = true;
        }
        if (target.Center != source.Center)
        {
            target.Center = source.Center;
            changed = true;
        }
        if (target.Title != source.Title)
        {
            target.Title = source.Title;
            changed = true;
        }
        if (target.Summary != source.Summary)
        {
            target.Summary = source.Summary;
            changed = true;
        }
        if (target.SourceUrl != source.SourceUrl)
        {
            target.SourceUrl = source.SourceUrl;
            changed = true;
        }
        return changed;
    }
}
