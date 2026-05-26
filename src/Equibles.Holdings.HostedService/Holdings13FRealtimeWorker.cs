using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Equibles.Holdings.HostedService;

/// <summary>
/// Near-real-time 13F-HR ingestion worker. Runs in parallel with the quarterly
/// <see cref="HoldingsScraperWorker"/>: this surfaces freshly filed 13F-HRs
/// within hours, while the bulk data set remains the authoritative source that
/// reconciles everything at quarter end. Both paths write through the same
/// import pipeline and upsert key, so the later bulk import updates rather than
/// duplicates anything this worker inserted.
/// </summary>
public class Holdings13FRealtimeWorker : BaseScraperWorker
{
    // Minimum lookback so the worker always re-sweeps recent days even when
    // the quarterly data set is up to date (catches late/amended filings).
    protected virtual int MinLookbackDays => 7;

    private readonly WorkerOptions _workerOptions;
    private readonly IConfiguration _configuration;

    protected override string WorkerName => "13F real-time ingestion";
    protected override TimeSpan SleepInterval => TimeSpan.FromHours(6);
    protected override ErrorSource ErrorSource => ErrorSource.HoldingsScraper;

    public Holdings13FRealtimeWorker(
        ILogger<Holdings13FRealtimeWorker> logger,
        IServiceScopeFactory scopeFactory,
        ErrorReporter errorReporter,
        IOptions<WorkerOptions> workerOptions,
        IConfiguration configuration
    )
        : base(logger, scopeFactory, errorReporter)
    {
        _workerOptions = workerOptions.Value;
        _configuration = configuration;
    }

    protected override bool ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_configuration["Sec:ContactEmail"]))
        {
            Logger.LogWarning(
                "13F real-time ingestion stopped: SEC_CONTACT_EMAIL not configured. Set it in your .env file."
            );
            return false;
        }
        return true;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        var startDate = _workerOptions.MinSyncDate ?? new DateTime(2020, 1, 1);
        var minReportDate = DateOnly.FromDateTime(startDate);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var lookbackDays = await ComputeLookbackDays(startDate, today);

        Logger.LogInformation(
            "13F real-time ingestion sweeping {LookbackDays} days of EDGAR daily index",
            lookbackDays
        );

        await using var scope = ScopeFactory.CreateAsyncScope();
        var ingestionService =
            scope.ServiceProvider.GetRequiredService<Realtime13FIngestionService>();

        var count = await ingestionService.IngestRecentFilings(
            today,
            lookbackDays,
            minReportDate,
            stoppingToken
        );

        Logger.LogInformation(
            "13F real-time ingestion cycle complete: {Count} filings processed",
            count
        );
    }

    /// <summary>
    /// Computes how many days of EDGAR daily index to sweep by finding the
    /// end date of the latest quarterly bulk data set that has been processed.
    /// The realtime worker covers the gap from that end date until today.
    /// </summary>
    private async Task<int> ComputeLookbackDays(DateTime startDate, DateOnly today)
    {
        var allFileNames = HoldingsDataSetClient.GetDataSetFileNames(startDate);
        if (allFileNames.Count == 0)
            return EffectiveMinLookback(today, MinLookbackDays);

        await using var scope = ScopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ProcessedDataSetRepository>();

        var processedFileNames = await repo.GetAll().Select(p => p.FileName).ToListAsync();

        var processedSet = new HashSet<string>(
            processedFileNames,
            StringComparer.OrdinalIgnoreCase
        );

        // Walk the file names in reverse (most recent first) to find the latest
        // processed data set whose coverage end date we can parse.
        DateOnly? latestEndDate = null;
        for (var i = allFileNames.Count - 1; i >= 0; i--)
        {
            if (!processedSet.Contains(allFileNames[i]))
                continue;

            var endDate = ParseDataSetEndDate(allFileNames[i]);
            if (endDate.HasValue)
            {
                latestEndDate = endDate.Value;
                break;
            }
        }

        if (!latestEndDate.HasValue)
            return EffectiveMinLookback(today, MinLookbackDays);

        // Sweep from the day after the last bulk data set's coverage ends
        var gap = today.DayNumber - latestEndDate.Value.DayNumber;
        return Math.Max(gap, EffectiveMinLookback(today, MinLookbackDays));
    }

    /// <summary>
    /// The lookback floor. When no processed bulk data set is available to
    /// measure from (fresh deploy, reset volume, or the startup race against
    /// <see cref="HoldingsScraperWorker"/>'s backfill), a flat 7-day window
    /// would skip a whole filing season's worth of submissions: 13F filers
    /// have 45 days after a quarter end to submit, so a window narrower than
    /// the gap to the latest completed quarter end misses on-time filings.
    /// Flooring at that gap keeps the current season covered regardless of
    /// tracking state, and makes the backfill race harmless.
    /// </summary>
    internal static int EffectiveMinLookback(DateOnly today, int minLookbackDays) =>
        Math.Max(minLookbackDays, today.DayNumber - LatestQuarterEnd(today).DayNumber);

    /// <summary>
    /// The end of the calendar quarter immediately preceding the one that
    /// contains <paramref name="today"/> — the latest 13F reporting period
    /// whose 45-day filing window is open (filings only appear after a period
    /// ends). Dates in Jan–Mar return the previous year's 31 Dec.
    /// </summary>
    internal static DateOnly LatestQuarterEnd(DateOnly today)
    {
        const int monthsPerQuarter = 3;
        var endMonth = (today.Month - 1) / monthsPerQuarter * monthsPerQuarter; // 0, 3, 6, 9
        return endMonth == 0
            ? new DateOnly(today.Year - 1, 12, 31)
            : new DateOnly(today.Year, endMonth, DateTime.DaysInMonth(today.Year, endMonth));
    }

    /// <summary>
    /// Extracts the coverage end date from a data set file name.
    /// New format: "01dec2025-28feb2026_form13f.zip" → 2026-02-28
    /// Old format: "2023q4_form13f.zip" → 2023-12-31
    /// </summary>
    internal static DateOnly? ParseDataSetEndDate(string fileName)
    {
        var name = fileName.Replace("_form13f.zip", "", StringComparison.OrdinalIgnoreCase);

        // New format: "01dec2025-28feb2026"
        var dashIndex = name.IndexOf('-');
        if (dashIndex > 0 && dashIndex < name.Length - 1)
        {
            var endPart = name[(dashIndex + 1)..];
            return TryParseDatePart(endPart);
        }

        // Old format: "2023q4"
        if (
            name.Length == 6
            && name[4] == 'q'
            && int.TryParse(name[..4], out var year)
            && int.TryParse(name[5..], out var quarter)
            && quarter is >= 1 and <= 4
            && year is >= 1 and <= 9999
        )
        {
            var endMonth = quarter * 3;
            return new DateOnly(year, endMonth, DateTime.DaysInMonth(year, endMonth));
        }

        return null;
    }

    private static DateOnly? TryParseDatePart(string part)
    {
        // Expected: "28feb2026" (ddMMMyyyy)
        if (part.Length < 9)
            return null;

        if (!int.TryParse(part[..2], out var day))
            return null;

        var monthStr = part[2..5].ToLowerInvariant();
        var monthNumber = monthStr switch
        {
            "jan" => 1,
            "feb" => 2,
            "mar" => 3,
            "apr" => 4,
            "may" => 5,
            "jun" => 6,
            "jul" => 7,
            "aug" => 8,
            "sep" => 9,
            "oct" => 10,
            "nov" => 11,
            "dec" => 12,
            _ => 0,
        };
        if (monthNumber == 0)
            return null;

        if (!int.TryParse(part[5..], out var year) || year is < 1 or > 9999)
            return null;

        if (day < 1 || day > DateTime.DaysInMonth(year, monthNumber))
            return null;

        return new DateOnly(year, monthNumber, day);
    }
}
