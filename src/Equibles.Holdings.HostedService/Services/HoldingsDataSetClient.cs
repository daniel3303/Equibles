using System.Globalization;
using System.IO.Compression;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Sec.Contracts;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsDataSetClient {
    private const string BaseUrl = "https://www.sec.gov/files/structureddata/data/form-13f-data-sets";

    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<HoldingsDataSetClient> _logger;

    public HoldingsDataSetClient(ISecEdgarClient secEdgarClient, ILogger<HoldingsDataSetClient> logger) {
        _secEdgarClient = secEdgarClient;
        _logger = logger;
    }

    public async Task<ZipArchive> DownloadDataSet(string fileName, CancellationToken cancellationToken) {
        var url = $"{BaseUrl}/{fileName}";
        _logger.LogInformation("Downloading 13F data set: {Url}", url);

        await using var stream = await _secEdgarClient.DownloadStream(url);
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return new ZipArchive(memoryStream, ZipArchiveMode.Read);
    }

    /// <summary>
    /// Generates the list of data set file names from a start date to the current period.
    /// Old format (2013-2023): {year}q{quarter}_form13f.zip
    /// New format (2024+): 01{mon}{year}-{lastDay}{mon}{year}_form13f.zip
    /// SEC publishes 4 periods per year starting 2024: Jan-Feb, Mar-May, Jun-Aug, Sep-Nov,
    /// then Dec crosses into next year (Dec-Feb).
    /// </summary>
    public static List<string> GetDataSetFileNames(DateTime startDate) {
        var fileNames = new List<string>();
        var now = DateTime.UtcNow;

        // SEC 13F structured data sets start at 2013 — clamp earlier dates
        if (startDate.Year < 2013) {
            startDate = new DateTime(2013, 1, 1);
        }

        // Old format: 2013-2023
        var startYear = startDate.Year;
        var startQuarter = (startDate.Month - 1) / 3 + 1;
        var lastOldYear = Math.Min(2023, now.Year);

        for (var year = startYear; year <= lastOldYear; year++) {
            var firstQ = year == startYear ? startQuarter : 1;
            var lastQ = year == now.Year ? (now.Month - 1) / 3 + 1 : 4;

            for (var quarter = firstQ; quarter <= lastQ; quarter++) {
                fileNames.Add($"{year}q{quarter}_form13f.zip");
            }
        }

        // New format: 2024+
        // Periods: Jan-Feb, Mar-May, Jun-Aug, Sep-Nov, Dec-Feb(+1)
        if (now.Year >= 2024) {
            var newStartYear = Math.Max(2024, startDate.Year);
            var periods = GetNewFormatPeriods(newStartYear, now);
            fileNames.AddRange(periods);
        }

        return fileNames;
    }

    private static List<string> GetNewFormatPeriods(int startYear, DateTime now) {
        var fileNames = new List<string>();

        // 2024 had a one-time Jan-Feb transition period, then the regular cycle:
        // Dec(prev year)-Feb, Mar-May, Jun-Aug, Sep-Nov
        // We enumerate all periods as (startDate, endDate) pairs
        var periods = new List<(DateOnly Start, DateOnly End)>();

        // 2024 transition: Jan-Feb 2024
        if (startYear <= 2024) {
            periods.Add((new DateOnly(2024, 1, 1), new DateOnly(2024, 2, 29)));
        }

        // Regular cycle: for each year from 2024+, add Mar-May, Jun-Aug, Sep-Nov, Dec-Feb(+1)
        for (var year = Math.Max(2024, startYear); year <= now.Year + 1; year++) {
            periods.Add((new DateOnly(year, 3, 1), new DateOnly(year, 5, 31)));
            periods.Add((new DateOnly(year, 6, 1), new DateOnly(year, 8, 31)));
            periods.Add((new DateOnly(year, 9, 1), new DateOnly(year, 11, 30)));
            periods.Add((new DateOnly(year, 12, 1),
                new DateOnly(year + 1, 2, DateTime.IsLeapYear(year + 1) ? 29 : 28)));
        }

        var nowDate = DateOnly.FromDateTime(now);

        foreach (var (start, end) in periods) {
            // Only include periods that have ended
            if (end >= nowDate) continue;
            // Skip periods before the configured start year
            if (start.Year < startYear) continue;

            var startStr = FormatDatePart(start);
            var endStr = FormatDatePart(end);
            fileNames.Add($"{startStr}-{endStr}_form13f.zip");
        }

        return fileNames;
    }

    private static string FormatDatePart(DateOnly date) {
        return date.ToString("dd", CultureInfo.InvariantCulture)
            + date.ToString("MMM", CultureInfo.InvariantCulture).ToLower()
            + date.ToString("yyyy");
    }
}
