using System.Globalization;
using System.IO.Compression;
using System.Net;
using Equibles.Core.AutoWiring;
using Equibles.Integrations.Sec.Contracts;
using HtmlAgilityPack;

namespace Equibles.Holdings.HostedService.Services;

[Service]
public class HoldingsDataSetClient
{
    private const string BaseUrl =
        "https://www.sec.gov/files/structureddata/data/form-13f-data-sets";

    internal const string CatalogUrl =
        "https://www.sec.gov/data-research/sec-markets-data/form-13f-data-sets";

    // SEC switched the 13F data-set filename scheme from {year}q{quarter} to
    // period ranges starting with the 2024 publications.
    private const int FirstNewFormatYear = 2024;

    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<HoldingsDataSetClient> _logger;

    public HoldingsDataSetClient(
        ISecEdgarClient secEdgarClient,
        ILogger<HoldingsDataSetClient> logger
    )
    {
        _secEdgarClient = secEdgarClient;
        _logger = logger;
    }

    public async Task<ZipArchive> DownloadDataSet(
        string fileName,
        CancellationToken cancellationToken
    )
    {
        var url = $"{BaseUrl}/{fileName}";
        _logger.LogInformation("Downloading 13F data set: {Url}", url);

        await using var stream = await DownloadArchive(fileName, url, cancellationToken);
        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return new ZipArchive(memoryStream, ZipArchiveMode.Read);
    }

    private async Task<Stream> DownloadArchive(
        string fileName,
        string legacyUrl,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _secEdgarClient.DownloadStream(legacyUrl);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // The SEC can move a new archive without redirecting the historical directory.
            await using var catalog = await DownloadPublishedResource(CatalogUrl);
            using var reader = new StreamReader(catalog);
            var publishedUrl = ResolvePublishedUrl(
                await reader.ReadToEndAsync(cancellationToken),
                fileName
            );
            if (publishedUrl == null)
                throw;
            if (publishedUrl == legacyUrl)
                throw new InvalidDataException(
                    "SEC lists an archive whose download returned 404.",
                    exception
                );
            _logger.LogInformation("Following published SEC data-set link: {Url}", publishedUrl);
            return await DownloadPublishedResource(publishedUrl);
        }
    }

    private async Task<Stream> DownloadPublishedResource(string url)
    {
        try
        {
            return await _secEdgarClient.DownloadStream(url);
        }
        catch (HttpRequestException exception)
            when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // A broken catalog or advertised download is an error, not an unpublished period.
            throw new InvalidDataException(
                "Published SEC 13F resource returned 404: " + url,
                exception
            );
        }
    }

    internal static string ResolvePublishedUrl(string html, string fileName)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var anchors = document.DocumentNode.SelectNodes("//a[@href]");
        var published = (anchors?.AsEnumerable() ?? [])
            .Select(a => HtmlEntity.DeEntitize(a.GetAttributeValue("href", "")))
            .Select(href => Uri.TryCreate(new Uri(CatalogUrl), href, out var uri) ? uri : null)
            .Where(uri =>
                uri != null
                && uri.Scheme == Uri.UriSchemeHttps
                && uri.Host == "www.sec.gov"
                && uri.IsDefaultPort
                && uri.UserInfo.Length == 0
                && uri.AbsolutePath.EndsWith("_form13f.zip", StringComparison.OrdinalIgnoreCase)
            )
            .Select(uri => uri.AbsoluteUri)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (published.Count == 0)
            throw new InvalidDataException("SEC 13F catalog contains no published archive links.");
        var matches = published
            .Where(url =>
                Path.GetFileName(new Uri(url).AbsolutePath)
                    .Equals(fileName, StringComparison.Ordinal)
            )
            .ToList();
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidDataException(
                "SEC 13F catalog contains conflicting archive links."
            ),
        };
    }

    /// <summary>
    /// Generates the list of data set file names from a start date to the current period.
    /// Old format (2013-2023): {year}q{quarter}_form13f.zip
    /// New format (2024+): 01{mon}{year}-{lastDay}{mon}{year}_form13f.zip
    /// SEC publishes 4 periods per year starting 2024: Jan-Feb, Mar-May, Jun-Aug, Sep-Nov,
    /// then Dec crosses into next year (Dec-Feb).
    /// </summary>
    public static List<string> GetDataSetFileNames(DateTime startDate)
    {
        var fileNames = new List<string>();
        var now = DateTime.UtcNow;

        // SEC 13F structured data sets begin at Q2 2013 (May 2013); Q1 does not exist.
        var earliestAvailable = new DateTime(2013, 4, 1);
        if (startDate < earliestAvailable)
        {
            startDate = earliestAvailable;
        }

        // Old format: 2013-2023
        var startYear = startDate.Year;
        var startQuarter = (startDate.Month - 1) / 3 + 1;
        var lastOldYear = Math.Min(2023, now.Year);

        for (var year = startYear; year <= lastOldYear; year++)
        {
            var firstQ = year == startYear ? startQuarter : 1;
            var lastQ = year == now.Year ? (now.Month - 1) / 3 + 1 : 4;

            for (var quarter = firstQ; quarter <= lastQ; quarter++)
            {
                fileNames.Add($"{year}q{quarter}_form13f.zip");
            }
        }

        // New format: 2024+
        // Periods: Jan-Feb, Mar-May, Jun-Aug, Sep-Nov, Dec-Feb(+1)
        if (now.Year >= FirstNewFormatYear)
        {
            var newStartYear = Math.Max(FirstNewFormatYear, startDate.Year);
            var periods = GetNewFormatPeriods(newStartYear, now);
            fileNames.AddRange(periods);
        }

        return fileNames;
    }

    private static List<string> GetNewFormatPeriods(int startYear, DateTime now)
    {
        var fileNames = new List<string>();

        // 2024 had a one-time Jan-Feb transition period, then the regular cycle:
        // Dec(prev year)-Feb, Mar-May, Jun-Aug, Sep-Nov
        // We enumerate all periods as (startDate, endDate) pairs
        var periods = new List<(DateOnly Start, DateOnly End)>();

        // 2024 transition: Jan-Feb 2024
        if (startYear <= FirstNewFormatYear)
        {
            periods.Add(
                (new DateOnly(FirstNewFormatYear, 1, 1), new DateOnly(FirstNewFormatYear, 2, 29))
            );
        }

        // Regular cycle: for each year from 2024+, add Mar-May, Jun-Aug, Sep-Nov, Dec-Feb(+1)
        for (var year = Math.Max(FirstNewFormatYear, startYear); year <= now.Year + 1; year++)
        {
            periods.Add((new DateOnly(year, 3, 1), new DateOnly(year, 5, 31)));
            periods.Add((new DateOnly(year, 6, 1), new DateOnly(year, 8, 31)));
            periods.Add((new DateOnly(year, 9, 1), new DateOnly(year, 11, 30)));
            periods.Add(
                (
                    new DateOnly(year, 12, 1),
                    new DateOnly(year + 1, 2, DateTime.IsLeapYear(year + 1) ? 29 : 28)
                )
            );
        }

        var nowDate = DateOnly.FromDateTime(now);

        foreach (var (start, end) in periods)
        {
            // Only include periods that have ended
            if (end >= nowDate)
                continue;
            // Skip periods before the configured start year
            if (start.Year < startYear)
                continue;

            var startStr = FormatDatePart(start);
            var endStr = FormatDatePart(end);
            fileNames.Add($"{startStr}-{endStr}_form13f.zip");
        }

        return fileNames;
    }

    internal static string FormatDatePart(DateOnly date)
    {
        return date.ToString("dd", CultureInfo.InvariantCulture)
            + date.ToString("MMM", CultureInfo.InvariantCulture).ToLower()
            + date.ToString("yyyy", CultureInfo.InvariantCulture);
    }
}
